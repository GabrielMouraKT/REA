using System.Buffers.Binary;
using REA.ProtocolAnalysis;

namespace REA.ProtocolTests;

public class ProtobufWireTests
{
    private static byte[] Sample() => new ProtoWriter()
        .Varint(1, 150)
        .String(2, "Gabriel")
        .SInt(3, -42)
        .Fixed32(4, BitConverter.SingleToUInt32Bits(1.5f))
        .Fixed64(5, 123456789012345UL)
        .Message(6, m => m.Varint(1, 7).String(2, "espada"))
        .Varint(7, 1)
        .ToArray();

    [Fact]
    public void Known_encoding_matches_spec_example()
    {
        // Exemplo clássico da documentação do Protobuf: campo 1 = 150 → 08 96 01.
        Assert.Equal(new byte[] { 0x08, 0x96, 0x01 }, new ProtoWriter().Varint(1, 150).ToArray());
    }

    [Fact]
    public void Roundtrip_decodes_every_wire_type_without_names()
    {
        var r = ProtobufWire.Decode(Sample());
        Assert.True(r.Complete);
        Assert.Null(r.Error);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], r.Fields.Select(f => f.Number));
        Assert.Equal(150ul, r.Fields[0].Varint);
        Assert.Contains("string: \"Gabriel\"", r.Fields[1].Interpretations);
        Assert.Contains("sint (zigzag): -42", r.Fields[2].Interpretations);
        Assert.Contains("float: 1.5", r.Fields[3].Interpretations);
        Assert.Contains("fixed64: 123456789012345", r.Fields[4].Interpretations);
        var nested = r.Fields[5].Nested;
        Assert.NotNull(nested);
        Assert.Equal(2, nested!.Count);
        Assert.Contains("bool: true", r.Fields[6].Interpretations);
    }

    [Fact]
    public void Truncated_input_keeps_partial_fields_and_reports_where()
    {
        var data = Sample();
        var r = ProtobufWire.Decode(data.AsMemory(0, data.Length - 3));
        Assert.False(r.Complete);
        Assert.NotEmpty(r.Fields);
        Assert.Contains("byte", r.Error);
    }

    [Theory]
    [InlineData(new byte[] { 0x00 })]                  // campo 0
    [InlineData(new byte[] { 0x0B })]                  // wire type 3 (grupo)
    [InlineData(new byte[] { 0x0A, 0x05, 0x01 })]      // tamanho maior que os dados
    [InlineData(new byte[] { 0x08, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 })] // varint > 10 bytes
    public void Invalid_inputs_are_not_messages(byte[] data) => Assert.False(ProtobufWire.IsCompleteMessage(data));

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(1L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void Zigzag_roundtrip(long v) => Assert.Equal(v, ProtobufWire.ZigZagDecode(ProtobufWire.ZigZagEncode(v)));
}

public class ProtoSchemaTests
{
    private const string Proto = """
        syntax = "proto3";
        package game.net;
        import "google/protobuf/timestamp.proto";
        option csharp_namespace = "Game";

        /* comentário
           de várias linhas */
        enum Class { CLASS_UNKNOWN = 0; WARRIOR = 1; MAGE = 2; }

        message Login {
          uint64 account_id = 1; // comentário de linha
          string name = 2;
          sint32 delta = 3;
          float speed = 4;
          fixed64 token = 5 [deprecated = true];
          Item weapon = 6;
          bool ok = 7;
          message Item {
            uint32 id = 1;
            string label = 2;
          }
          oneof extra { Class klass = 8; }
          map<string, int32> stats = 9;
          reserved 10, 11;
          repeated uint32 ids = 12;
        }
        service Ignored { rpc Do (Login) returns (Login); }
        """;

    [Fact]
    public void Parses_messages_nested_enums_oneof_and_map()
    {
        var s = ProtoSchema.Parse(Proto);
        Assert.Equal("game.net", s.Package);
        var login = s.FindMessage("Login")!;
        Assert.Equal("game.net.Login", login.FullName);
        Assert.Equal("weapon", login.Fields[6].Name);
        Assert.Equal("klass", login.Fields[8].Name);
        Assert.Equal("map<string, int32>", login.Fields[9].Type);
        Assert.Equal("repeated", login.Fields[12].Label);
        Assert.NotNull(s.FindMessage("game.net.Login.Item"));
        Assert.Equal("MAGE", s.Enums["game.net.Class"].Values[2]);
        Assert.Equal("game.net.Login.Item", s.ResolveType("Item", "game.net.Login"));
        Assert.Contains(s.Notes, n => n.Contains("Ignored"));
    }

    [Fact]
    public void Labels_known_fields_and_never_invents_names()
    {
        var s = ProtoSchema.Parse(Proto);
        var bytes = new ProtoWriter()
            .Varint(1, 150).String(2, "Gabriel").SInt(3, -42)
            .Fixed32(4, BitConverter.SingleToUInt32Bits(1.5f))
            .Message(6, m => m.Varint(1, 7).String(2, "espada"))
            .Varint(8, 2)
            .Varint(99, 5) // não existe no .proto
            .ToArray();
        var labeled = ProtoLabeler.Label(ProtobufWire.Decode(bytes).Fields, s, "Login");

        Assert.Equal("Gabriel", labeled.Single(l => l.Name == "name").DisplayValue);
        Assert.Equal("-42", labeled.Single(l => l.Name == "delta").DisplayValue);
        Assert.Equal("1.5", labeled.Single(l => l.Name == "speed").DisplayValue);
        Assert.Equal("MAGE (2)", labeled.Single(l => l.Name == "klass").DisplayValue);
        var weapon = labeled.Single(l => l.Name == "weapon");
        Assert.Equal("espada", weapon.Children.Single(c => c.Name == "label").DisplayValue);
        var unknown = labeled.Single(l => l.Field.Number == 99);
        Assert.False(unknown.IsDeclared);
        Assert.Null(unknown.Name);
    }

    [Theory]
    [InlineData("message A { string x = 0; }")]
    [InlineData("message A { string x = 1; int32 y = 1; }")]
    [InlineData("message A { string x = 1;")]
    [InlineData("/* sem fim")]
    public void Invalid_proto_is_rejected_with_message(string src) =>
        Assert.Throws<FormatException>(() => ProtoSchema.Parse(src));
}

public class FramingDetectorTests
{
    /// <summary>Quadros sintéticos: [u16 LE tamanho total, incluindo cabeçalho][u16 LE opcode][corpo Protobuf].</summary>
    private static byte[] Frames(int count)
    {
        var ms = new MemoryStream();
        for (var i = 0; i < count; i++)
        {
            var opcode = i % 3 == 0 ? 0x0101 : 0x0205;
            var body = new ProtoWriter().Varint(1, (ulong)(i * 37)).String(2, new string('x', i % 7 + 1)).ToArray();
            var frame = new byte[4 + body.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)frame.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), (ushort)opcode);
            body.CopyTo(frame, 4);
            ms.Write(frame);
        }
        return ms.ToArray();
    }

    [Fact]
    public void Detects_u16_le_length_including_header_with_opcode_and_protobuf()
    {
        var best = FramingDetector.Analyze(Frames(30))[0];
        Assert.Equal(new FramingHypothesis(0, 2, true, true), best.Hypothesis);
        Assert.Equal(Confidence.Alta, best.Confidence);
        Assert.Equal(30, best.FrameCount);
        Assert.Equal(0, best.TrailingBytes);
        Assert.Equal([0x0205, 0x0101], best.OpcodeCandidates.Select(o => o.Value));
        Assert.Equal(20, best.OpcodeCandidates[0].Count);
        Assert.Equal(1.0, best.ProtobufBodyRatio);
    }

    [Fact]
    public void Cut_capture_lowers_confidence_and_reports_trailing_bytes()
    {
        var data = Frames(30);
        var r = FramingDetector.Evaluate(data.AsMemory(0, data.Length - 3), new FramingHypothesis(0, 2, true, true))!;
        Assert.Equal(29, r.FrameCount);
        Assert.True(r.TrailingBytes > 0);
        Assert.NotEqual(Confidence.Alta, r.Confidence);
        Assert.Contains(r.Notes, n => n.Contains("não formam um quadro completo"));
    }

    [Fact]
    public void Random_bytes_do_not_produce_high_confidence()
    {
        var data = new byte[4096];
        new Random(1234).NextBytes(data);
        Assert.DoesNotContain(FramingDetector.Analyze(data), r => r.Confidence == Confidence.Alta);
    }

    [Fact]
    public void Zero_padding_is_flagged_as_degenerate()
    {
        var r = FramingDetector.Evaluate(new byte[64], new FramingHypothesis(0, 2, true, false))!;
        Assert.Equal(Confidence.Baixa, r.Confidence);
        Assert.Contains(r.Notes, n => n.Contains("preenchimento"));
    }
}
