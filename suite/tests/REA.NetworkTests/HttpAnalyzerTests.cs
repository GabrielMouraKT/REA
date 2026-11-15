using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using REA.PacketAnalyzer;

namespace REA.NetworkTests;

public class HttpAnalyzerTests
{
    private const string Har = """
        {"log":{"version":"1.2","entries":[
          {"startedDateTime":"2026-11-15T12:00:00.000Z","time":42.5,
           "request":{"method":"POST","url":"https://login.example.test/api/session","httpVersion":"HTTP/1.1",
             "headers":[{"name":"Authorization","value":"Bearer SEGREDO"},{"name":"Cookie","value":"sid=SEGREDO"},
                        {"name":"User-Agent","value":"Launcher/1.0"}],
             "cookies":[{"name":"sid","value":"SEGREDO"}],"bodySize":17,
             "postData":{"mimeType":"application/json","text":"{\"senha\":\"SEGREDO\"}"}},
           "response":{"status":200,"statusText":"OK","httpVersion":"HTTP/1.1",
             "headers":[{"name":"Set-Cookie","value":"sid=SEGREDO; HttpOnly"},{"name":"Content-Type","value":"application/json"}],
             "content":{"size":120,"mimeType":"application/json","text":"SEGREDO"},"bodySize":120}},
          {"startedDateTime":"2026-11-15T12:00:01.000Z","time":-1,
           "request":{"method":"GET","url":"https://cdn.example.test/patch/manifest.json","httpVersion":"HTTP/2","headers":[],"bodySize":-1},
           "response":{"status":0,"headers":[],"content":{"size":-1}}},
          {"response":{}}
        ]}}
        """;

    [Fact]
    public void Har_is_parsed_and_secrets_never_leave_the_analyzer()
    {
        using var doc = JsonDocument.Parse(Har);
        var r = HttpAnalyzer.ParseHar(doc.RootElement);

        Assert.Equal(2, r.Exchanges.Count);
        var login = r.Exchanges[0];
        Assert.Equal("POST", login.Method);
        Assert.Equal("login.example.test", login.Host);
        Assert.Equal(200, login.Status);
        Assert.Equal(42.5, login.DurationMs);
        Assert.Equal(120, login.ResponseBodySize);
        Assert.All(login.RequestHeaders.Where(h => h.Name is "Authorization" or "Cookie"), h => Assert.True(h.Redacted));
        Assert.True(login.ResponseHeaders.Single(h => h.Name == "Set-Cookie").Redacted);
        Assert.Equal("Launcher/1.0", login.RequestHeaders.Single(h => h.Name == "User-Agent").Value);

        // Nenhum valor secreto aparece em lugar nenhum do resultado (cabeçalhos, cookies, corpo).
        Assert.DoesNotContain("SEGREDO", JsonSerializer.Serialize(r));

        var cdn = r.Exchanges[1];
        Assert.Null(cdn.Status);
        Assert.Null(cdn.DurationMs);
        Assert.Null(cdn.RequestBodySize);
        Assert.Contains(r.Notes, n => n.Contains("sem 'request'"));
        Assert.Contains(r.Notes, n => n.Contains("ocultados"));
    }

    [Fact]
    public void Invalid_har_is_rejected()
    {
        using var doc = JsonDocument.Parse("""{"foo":1}""");
        Assert.Throws<InvalidDataException>(() => HttpAnalyzer.ParseHar(doc.RootElement));
    }

    [Fact]
    public async Task Real_tshark_extracts_http_and_lists_tls_as_encrypted()
    {
        Assert.SkipWhen(TsharkCrossCheck.Locate() is null, "TShark não instalado.");
        using var dir = new TempDir();
        const string c = "10.0.0.2", s = "10.0.0.1", t = "10.0.0.3";
        var request = Encoding.ASCII.GetBytes(
            "GET /patch/list.txt HTTP/1.1\r\nHost: patch.example.test\r\nCookie: sid=SEGREDO\r\nUser-Agent: REA-Teste\r\n\r\n");
        var body = "arquivo1\narquivo2\n";
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nSet-Cookie: sid=SEGREDO\r\n\r\n{body}");
        var hello = ClientHello("jogo.example.test");

        var path = Frames.WritePcapNg(dir.File("http.pcapng"),
            Frames.Tcp(c, 50000, s, 80, 100, 0, "S"),
            Frames.Tcp(s, 80, c, 50000, 500, 101, "SA"),
            Frames.Tcp(c, 50000, s, 80, 101, 501, "A"),
            Frames.Tcp(c, 50000, s, 80, 101, 501, "PA", request),
            Frames.Tcp(s, 80, c, 50000, 501, 101 + (uint)request.Length, "PA", response),
            Frames.Tcp(c, 50001, t, 443, 900, 0, "S"),
            Frames.Tcp(t, 443, c, 50001, 700, 901, "SA"),
            Frames.Tcp(c, 50001, t, 443, 901, 701, "A"),
            Frames.Tcp(c, 50001, t, 443, 901, 701, "PA", hello));

        var r = await HttpAnalyzer.FromCaptureAsync(path, TestContext.Current.CancellationToken);

        var x = Assert.Single(r.Exchanges);
        Assert.Equal("GET", x.Method);
        Assert.Equal("http://patch.example.test/patch/list.txt", x.Url);
        Assert.Equal(200, x.Status);
        Assert.Equal("text/plain", x.ContentType);
        Assert.Equal(body.Length, x.ResponseBodySize);
        Assert.True(x.RequestHeaders.Single(h => h.Name == "Cookie").Redacted);
        Assert.True(x.ResponseHeaders.Single(h => h.Name == "Set-Cookie").Redacted);
        Assert.Equal("REA-Teste", x.RequestHeaders.Single(h => h.Name == "User-Agent").Value);
        Assert.DoesNotContain("SEGREDO", JsonSerializer.Serialize(r));

        var tls = Assert.Single(r.Encrypted);
        Assert.Equal("jogo.example.test", tls.ServerName);
        Assert.Equal("10.0.0.3:443", tls.Destination);
        Assert.Contains(r.Notes, n => n.Contains("cifrado"));
    }

    /// <summary>Registro TLS 1.2 com ClientHello mínimo e extensão SNI.</summary>
    private static byte[] ClientHello(string serverName)
    {
        var name = Encoding.ASCII.GetBytes(serverName);
        var sni = new List<byte>();
        sni.AddRange(U16(name.Length + 3));
        sni.Add(0);
        sni.AddRange(U16(name.Length));
        sni.AddRange(name);
        var ext = new List<byte>();
        ext.AddRange(U16(0));
        ext.AddRange(U16(sni.Count));
        ext.AddRange(sni);

        var body = new List<byte> { 0x03, 0x03 };
        body.AddRange(new byte[32]);
        body.Add(0);
        body.AddRange(U16(2)); body.AddRange([0x13, 0x01]);
        body.AddRange([1, 0]);
        body.AddRange(U16(ext.Count));
        body.AddRange(ext);

        var hs = new List<byte> { 1, 0, (byte)(body.Count >> 8), (byte)body.Count };
        hs.AddRange(body);
        var record = new List<byte> { 0x16, 0x03, 0x01 };
        record.AddRange(U16(hs.Count));
        record.AddRange(hs);
        return [.. record];
    }

    private static byte[] U16(int v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)v);
        return b;
    }
}
