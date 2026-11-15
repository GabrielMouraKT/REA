using System.Text;

namespace REA.ProtocolAnalysis;

public sealed record ProtoFieldDef(int Number, string Name, string Type, string Label);

public sealed record ProtoMessageDef(string FullName, IReadOnlyDictionary<int, ProtoFieldDef> Fields);

public sealed record ProtoEnumDef(string FullName, IReadOnlyDictionary<long, string> Values);

/// <summary>
/// Esquema lido de um arquivo .proto (proto2/proto3). Cobre message, enum, oneof, map, nested e reserved;
/// options, services e imports são ignorados (apenas registrados em <see cref="Notes"/>).
/// </summary>
public sealed class ProtoSchema
{
    public string? Package { get; private set; }
    public Dictionary<string, ProtoMessageDef> Messages { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, ProtoEnumDef> Enums { get; } = new(StringComparer.Ordinal);
    public List<string> Notes { get; } = [];

    public static ProtoSchema Parse(string source)
    {
        var schema = new ProtoSchema();
        var t = new Tokens(Tokenize(source));
        while (!t.End)
        {
            var word = t.Next();
            switch (word)
            {
                case "syntax" or "edition": t.SkipStatement(); break;
                case "package": schema.Package = t.Next(); t.Expect(";"); break;
                case "import": schema.Notes.Add($"import ignorado: {t.Peek()}"); t.SkipStatement(); break;
                case "option": t.SkipStatement(); break;
                case "message": schema.ParseMessage(t, schema.Package); break;
                case "enum": schema.ParseEnum(t, schema.Package); break;
                case "service" or "extend": schema.Notes.Add($"{word} {t.Peek()} ignorado"); t.Next(); t.SkipBlock(); break;
                case ";": break;
                default: throw new FormatException($"Token inesperado no .proto: '{word}' (linha {t.Line}).");
            }
        }
        return schema;
    }

    /// <summary>Localiza uma mensagem pelo nome completo ou pelo nome curto (se não for ambíguo).</summary>
    public ProtoMessageDef? FindMessage(string name)
    {
        if (Messages.TryGetValue(name.TrimStart('.'), out var m)) return m;
        var matches = Messages.Values.Where(x => x.FullName == name || x.FullName.EndsWith("." + name, StringComparison.Ordinal)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Resolve um nome de tipo usado dentro de <paramref name="scope"/> pelas regras de escopo do protobuf.</summary>
    public string? ResolveType(string type, string scope)
    {
        if (type.StartsWith('.')) return type[1..];
        var s = scope;
        while (true)
        {
            var candidate = string.IsNullOrEmpty(s) ? type : s + "." + type;
            if (Messages.ContainsKey(candidate) || Enums.ContainsKey(candidate)) return candidate;
            if (string.IsNullOrEmpty(s)) return null;
            var dot = s.LastIndexOf('.');
            s = dot < 0 ? "" : s[..dot];
        }
    }

    private void ParseMessage(Tokens t, string? scope)
    {
        var fullName = Join(scope, t.Next());
        t.Expect("{");
        var fields = new Dictionary<int, ProtoFieldDef>();
        ParseMessageBody(t, fullName, fields);
        Messages[fullName] = new ProtoMessageDef(fullName, fields);
    }

    private void ParseMessageBody(Tokens t, string fullName, Dictionary<int, ProtoFieldDef> fields)
    {
        while (true)
        {
            var w = t.Next();
            switch (w)
            {
                case "}": return;
                case ";": continue;
                case "message": ParseMessage(t, fullName); continue;
                case "enum": ParseEnum(t, fullName); continue;
                case "option" or "reserved" or "extensions": t.SkipStatement(); continue;
                case "extend": t.Next(); t.SkipBlock(); continue;
                case "oneof": t.Next(); t.Expect("{"); ParseMessageBody(t, fullName, fields); continue;
            }

            var label = "";
            if (w is "repeated" or "optional" or "required") { label = w; w = t.Next(); }
            string type;
            if (w == "map")
            {
                t.Expect("<");
                var k = t.Next(); t.Expect(",");
                var v = t.Next(); t.Expect(">");
                type = $"map<{k}, {v}>";
            }
            else if (w == "group") throw new FormatException($"Campos 'group' não são suportados (linha {t.Line}).");
            else type = w;

            var name = t.Next();
            t.Expect("=");
            var numberToken = t.Next();
            if (!int.TryParse(numberToken, out var number) || number < 1 || number > ProtobufWire.MaxFieldNumber)
                throw new FormatException($"Número de campo inválido '{numberToken}' em {fullName}.{name} (linha {t.Line}).");
            if (t.Peek() == "[") t.SkipUntil("]");
            t.Expect(";");
            if (!fields.TryAdd(number, new ProtoFieldDef(number, name, type, label)))
                throw new FormatException($"Número de campo {number} repetido em {fullName} (linha {t.Line}).");
        }
    }

    private void ParseEnum(Tokens t, string? scope)
    {
        var fullName = Join(scope, t.Next());
        t.Expect("{");
        var values = new Dictionary<long, string>();
        while (true)
        {
            var w = t.Next();
            if (w == "}") break;
            if (w == ";") continue;
            if (w is "option" or "reserved") { t.SkipStatement(); continue; }
            t.Expect("=");
            var n = t.Next();
            if (n == "-") n = "-" + t.Next();
            if (!long.TryParse(n, out var value)) throw new FormatException($"Valor de enum inválido '{n}' (linha {t.Line}).");
            if (t.Peek() == "[") t.SkipUntil("]");
            t.Expect(";");
            values.TryAdd(value, w); // allow_alias: o primeiro nome é o exibido
        }
        Enums[fullName] = new ProtoEnumDef(fullName, values);
    }

    private static string Join(string? scope, string name) => string.IsNullOrEmpty(scope) ? name : scope + "." + name;

    private static List<(string Text, int Line)> Tokenize(string s)
    {
        var tokens = new List<(string, int)>();
        var line = 1;
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) throw new FormatException($"Comentário /* sem fechamento (linha {line}).");
                line += s.AsSpan(i, end - i).Count('\n');
                i = end + 2;
                continue;
            }
            if (c is '"' or '\'')
            {
                var sb = new StringBuilder().Append(c);
                i++;
                while (i < s.Length && s[i] != c) { if (s[i] == '\\' && i + 1 < s.Length) sb.Append(s[i++]); sb.Append(s[i++]); }
                if (i >= s.Length) throw new FormatException($"Texto entre aspas sem fechamento (linha {line}).");
                sb.Append(s[i++]);
                tokens.Add((sb.ToString(), line));
                continue;
            }
            if (char.IsLetterOrDigit(c) || c is '_' or '.')
            {
                var start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '.')) i++;
                tokens.Add((s[start..i], line));
                continue;
            }
            tokens.Add((c.ToString(), line));
            i++;
        }
        return tokens;
    }

    private sealed class Tokens(List<(string Text, int Line)> items)
    {
        private int _i;
        public bool End => _i >= items.Count;
        public int Line => items.Count == 0 ? 1 : items[Math.Min(_i, items.Count - 1)].Line;
        public string Peek() => End ? "" : items[_i].Text;
        public string Next() => End ? throw new FormatException("Fim inesperado do arquivo .proto.") : items[_i++].Text;
        public void Expect(string s)
        {
            var got = Next();
            if (got != s) throw new FormatException($"Esperado '{s}', encontrado '{got}' (linha {Line}).");
        }
        public void SkipStatement() { while (Next() != ";") { } }
        public void SkipUntil(string s) { while (Next() != s) { } }
        public void SkipBlock()
        {
            Expect("{");
            var depth = 1;
            while (depth > 0) { var w = Next(); if (w == "{") depth++; else if (w == "}") depth--; }
        }
    }
}

/// <summary>Campo decodificado com o rótulo do .proto, quando o número existe no esquema.</summary>
public sealed record LabeledField(
    ProtoField Field,
    string? Name,
    string? DeclaredType,
    string? DisplayValue,
    IReadOnlyList<LabeledField> Children)
{
    public bool IsDeclared => Name is not null;
}

public static class ProtoLabeler
{
    private static readonly HashSet<string> Scalars =
    [
        "int32", "int64", "uint32", "uint64", "sint32", "sint64", "bool", "fixed32", "fixed64", "sfixed32",
        "sfixed64", "float", "double", "string", "bytes",
    ];

    /// <summary>
    /// Aplica nomes do .proto aos campos decodificados. Campos cujo número não existe na mensagem ficam sem nome
    /// (nunca se inventa nome). Se o wire type não bate com o tipo declarado, o valor declarado não é aplicado.
    /// </summary>
    public static IReadOnlyList<LabeledField> Label(IReadOnlyList<ProtoField> fields, ProtoSchema schema, string messageName)
    {
        var msg = schema.FindMessage(messageName)
                  ?? throw new KeyNotFoundException($"Mensagem '{messageName}' não existe no .proto.");
        return Label(fields, schema, msg);
    }

    private static List<LabeledField> Label(IReadOnlyList<ProtoField> fields, ProtoSchema schema, ProtoMessageDef msg)
    {
        var scope = msg.FullName;
        var result = new List<LabeledField>();
        foreach (var f in fields)
        {
            if (!msg.Fields.TryGetValue(f.Number, out var def))
            {
                result.Add(new LabeledField(f, null, null, null, Unlabeled(f.Nested)));
                continue;
            }

            var type = def.Type;
            string? display = null;
            IReadOnlyList<LabeledField> children = [];
            if (Scalars.Contains(type)) display = Scalar(f, type);
            else if (type.StartsWith("map<", StringComparison.Ordinal))
            {
                children = Unlabeled(f.Nested);
                display = "entrada de map (campo 1 = chave, 2 = valor)";
            }
            else if (schema.ResolveType(type, scope) is { } resolved)
            {
                if (schema.Enums.TryGetValue(resolved, out var en) && f.Varint is { } ev)
                    display = en.Values.TryGetValue((long)ev, out var n) ? $"{n} ({(long)ev})" : $"{(long)ev} (valor fora do enum)";
                else if (schema.Messages.TryGetValue(resolved, out var sub) && f.WireType == WireType.LengthDelimited)
                {
                    // O .proto diz que é mensagem: decodifica direto, sem depender da heurística de aninhamento.
                    var inner = ProtobufWire.Decode(f.Bytes);
                    if (inner.Complete) children = Label(inner.Fields, schema, sub);
                    else display = "não decodifica como a mensagem declarada";
                }
            }
            result.Add(new LabeledField(f, def.Name, (def.Label == "" ? "" : def.Label + " ") + type, display, children));
        }
        return result;
    }

    private static IReadOnlyList<LabeledField> Unlabeled(IReadOnlyList<ProtoField>? nested) =>
        nested?.Select(n => new LabeledField(n, null, null, null, Unlabeled(n.Nested))).ToList() ?? [];

    private static string? Scalar(ProtoField f, string type)
    {
        switch (f.WireType)
        {
            case WireType.Varint when f.Varint is { } v:
                return type switch
                {
                    "sint32" or "sint64" => ProtobufWire.ZigZagDecode(v).ToString(),
                    "int32" => ((int)(long)v).ToString(),
                    "int64" => ((long)v).ToString(),
                    "bool" => v != 0 ? "true" : "false",
                    "uint32" or "uint64" => v.ToString(),
                    _ => null,
                };
            case WireType.LengthDelimited when type == "string":
                try { return new UTF8Encoding(false, true).GetString(f.Bytes.Span); }
                catch (DecoderFallbackException) { return null; }
            case WireType.LengthDelimited when type == "bytes":
                return Convert.ToHexString(f.Bytes.Span);
            case WireType.Fixed32 or WireType.Fixed64:
                var match = f.Interpretations.FirstOrDefault(i => i.StartsWith(type + ":", StringComparison.Ordinal));
                return match?[(type.Length + 2)..];
            default:
                return null;
        }
    }
}
