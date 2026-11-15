using System.Globalization;
using System.Text;
using PacketDotNet;

namespace REA.PacketAnalyzer;

public sealed record PacketSummary(
    long Number,
    long Offset,
    DateTimeOffset Timestamp,
    int Length,
    int CapturedLength,
    string Protocol,
    string Source,
    string Destination,
    int? SourcePort,
    int? DestinationPort,
    string? TcpFlags,
    int PayloadLength,
    string Info);

public sealed record DecodedField(string Name, string Value);

public sealed record DecodedLayer(string Title, IReadOnlyList<DecodedField> Fields);

public sealed record PacketDetail(PacketSummary Summary, IReadOnlyList<DecodedLayer> Layers, byte[] Raw, byte[] Payload);

/// <summary>Elementos de um segmento TCP, usados pela reconstrução de fluxos.</summary>
public sealed record TcpSegmentInfo(
    long Frame, DateTimeOffset Timestamp, string SourceIp, int SourcePort, string DestinationIp, int DestinationPort,
    uint Sequence, uint Acknowledgment, bool Syn, bool Ack, bool Fin, bool Rst, bool Psh, byte[] Payload);

/// <summary>Decodifica quadros com PacketDotNet. Nunca lança: pacotes malformados viram protocolo "Malformado".</summary>
public static class PacketDecoder
{
    public static Packet? Parse(CapturedFrame frame)
    {
        try
        {
            return Packet.ParsePacket(ToLinkLayer(frame.LinkType), frame.Data);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static LinkLayers ToLinkLayer(ushort linkType) => linkType switch
    {
        LinkTypes.Raw => LinkLayers.Raw,
        _ => (LinkLayers)linkType,
    };

    public static PacketSummary Summarize(CapturedFrame frame) => Summarize(frame, Parse(frame));

    public static PacketSummary Summarize(CapturedFrame frame, Packet? packet)
    {
        string protocol = "Desconhecido", source = "—", destination = "—", info = "";
        int? sport = null, dport = null;
        string? flags = null;
        var payloadLength = 0;

        try
        {
            if (packet is null)
            {
                protocol = "Malformado";
                info = $"Link-layer {frame.LinkType} não decodificado";
            }
            else if (packet.Extract<EthernetPacket>() is { } eth && packet.Extract<IPPacket>() is null && packet.Extract<ArpPacket>() is null)
            {
                protocol = "Ethernet";
                source = eth.SourceHardwareAddress.ToString();
                destination = eth.DestinationHardwareAddress.ToString();
                info = $"EtherType {eth.Type}";
            }

            if (packet?.Extract<ArpPacket>() is { } arp)
            {
                protocol = "ARP";
                source = arp.SenderProtocolAddress.ToString();
                destination = arp.TargetProtocolAddress.ToString();
                info = arp.Operation == ArpOperation.Request
                    ? $"Quem tem {arp.TargetProtocolAddress}? Pergunta {arp.SenderProtocolAddress}"
                    : $"{arp.SenderProtocolAddress} está em {arp.SenderHardwareAddress}";
            }

            if (packet?.Extract<IPPacket>() is { } ip)
            {
                source = ip.SourceAddress.ToString();
                destination = ip.DestinationAddress.ToString();
                protocol = ip.Version == IPVersion.IPv6 ? "IPv6" : "IPv4";
                info = $"Protocolo {ip.Protocol}";

                if (packet.Extract<TcpPacket>() is { } tcp)
                {
                    protocol = "TCP";
                    sport = tcp.SourcePort;
                    dport = tcp.DestinationPort;
                    flags = TcpFlagsText(tcp);
                    payloadLength = tcp.PayloadData?.Length ?? 0;
                    info = $"{tcp.SourcePort} → {tcp.DestinationPort} [{flags}] Seq={tcp.SequenceNumber} " +
                           $"Ack={tcp.AcknowledgmentNumber} Win={tcp.WindowSize} Len={payloadLength}";
                }
                else if (packet.Extract<UdpPacket>() is { } udp)
                {
                    protocol = "UDP";
                    sport = udp.SourcePort;
                    dport = udp.DestinationPort;
                    payloadLength = udp.PayloadData?.Length ?? 0;
                    info = $"{udp.SourcePort} → {udp.DestinationPort} Len={payloadLength}";
                }
                else if (packet.Extract<IcmpV4Packet>() is { } icmp)
                {
                    protocol = "ICMP";
                    info = $"Tipo/código {icmp.TypeCode}";
                }
                else if (packet.Extract<IcmpV6Packet>() is { } icmp6)
                {
                    protocol = "ICMPv6";
                    info = $"Tipo {icmp6.Type}";
                }
            }
        }
        catch (Exception ex)
        {
            protocol = "Malformado";
            info = ex.Message;
        }

        return new PacketSummary(frame.Number, frame.Offset, frame.Timestamp, frame.OriginalLength, frame.Data.Length,
            protocol, source, destination, sport, dport, flags, payloadLength, info);
    }

    public static PacketDetail Detail(CapturedFrame frame)
    {
        var packet = Parse(frame);
        var summary = Summarize(frame, packet);
        var layers = new List<DecodedLayer>
        {
            new("Quadro", [
                new("Número", frame.Number.ToString(CultureInfo.InvariantCulture)),
                new("Horário (local)", frame.Timestamp.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss.ffffff")),
                new("Tamanho original", $"{frame.OriginalLength} bytes"),
                new("Tamanho capturado", $"{frame.Data.Length} bytes" + (frame.Data.Length < frame.OriginalLength ? " (cortado pelo snaplen)" : "")),
                new("Link-layer", $"{frame.LinkType} ({ToLinkLayer(frame.LinkType)})"),
            ]),
        };
        byte[] payload = [];

        try
        {
            if (packet?.Extract<NullPacket>() is { } nul)
                layers.Add(new("Loopback (Null/BSD)", [new("Família", nul.Protocol.ToString())]));
            if (packet?.Extract<EthernetPacket>() is { } eth)
                layers.Add(new("Ethernet II", [
                    new("Origem", eth.SourceHardwareAddress.ToString()),
                    new("Destino", eth.DestinationHardwareAddress.ToString()),
                    new("Tipo", eth.Type.ToString()),
                ]));
            if (packet?.Extract<ArpPacket>() is { } arp)
                layers.Add(new("ARP", [
                    new("Operação", arp.Operation.ToString()),
                    new("Remetente", $"{arp.SenderProtocolAddress} ({arp.SenderHardwareAddress})"),
                    new("Alvo", $"{arp.TargetProtocolAddress} ({arp.TargetHardwareAddress})"),
                ]));
            if (packet?.Extract<IPv4Packet>() is { } ip4)
                layers.Add(new("IPv4", [
                    new("Origem", ip4.SourceAddress.ToString()),
                    new("Destino", ip4.DestinationAddress.ToString()),
                    new("Protocolo", ip4.Protocol.ToString()),
                    new("TTL", ip4.TimeToLive.ToString(CultureInfo.InvariantCulture)),
                    new("Tamanho total", ip4.TotalLength.ToString(CultureInfo.InvariantCulture)),
                    new("Identificação", $"0x{ip4.Id:X4}"),
                ]));
            if (packet?.Extract<IPv6Packet>() is { } ip6)
                layers.Add(new("IPv6", [
                    new("Origem", ip6.SourceAddress.ToString()),
                    new("Destino", ip6.DestinationAddress.ToString()),
                    new("Próximo cabeçalho", ip6.NextHeader.ToString()),
                    new("Hop limit", ip6.HopLimit.ToString(CultureInfo.InvariantCulture)),
                ]));
            if (packet?.Extract<TcpPacket>() is { } tcp)
            {
                layers.Add(new("TCP", [
                    new("Porta de origem", tcp.SourcePort.ToString(CultureInfo.InvariantCulture)),
                    new("Porta de destino", tcp.DestinationPort.ToString(CultureInfo.InvariantCulture)),
                    new("Sequência", tcp.SequenceNumber.ToString(CultureInfo.InvariantCulture)),
                    new("Confirmação (ACK)", tcp.AcknowledgmentNumber.ToString(CultureInfo.InvariantCulture)),
                    new("Flags", TcpFlagsText(tcp)),
                    new("Janela", tcp.WindowSize.ToString(CultureInfo.InvariantCulture)),
                    new("Tamanho do cabeçalho", $"{tcp.DataOffset * 4} bytes"),
                    new("Dados (payload)", $"{tcp.PayloadData?.Length ?? 0} bytes"),
                ]));
                payload = tcp.PayloadData ?? [];
            }
            else if (packet?.Extract<UdpPacket>() is { } udp)
            {
                layers.Add(new("UDP", [
                    new("Porta de origem", udp.SourcePort.ToString(CultureInfo.InvariantCulture)),
                    new("Porta de destino", udp.DestinationPort.ToString(CultureInfo.InvariantCulture)),
                    new("Tamanho", udp.Length.ToString(CultureInfo.InvariantCulture)),
                    new("Dados (payload)", $"{udp.PayloadData?.Length ?? 0} bytes"),
                ]));
                payload = udp.PayloadData ?? [];
            }
            else if (packet?.Extract<IcmpV4Packet>() is { } icmp)
                layers.Add(new("ICMP", [new("Tipo/código", icmp.TypeCode.ToString())]));
        }
        catch (Exception ex)
        {
            layers.Add(new("Erro de decodificação", [new("Mensagem", ex.Message)]));
        }

        if (payload.Length > 0)
            layers.Add(new("Dados", [
                new("Tamanho", $"{payload.Length} bytes"),
                new("Texto imprimível", Printable(payload, 120)),
                new("Observação", "Conteúdo exibido como bytes brutos. Se estiver cifrado, não é possível interpretá-lo sem as chaves."),
            ]));

        return new PacketDetail(summary, layers, frame.Data, payload);
    }

    public static TcpSegmentInfo? TcpSegment(CapturedFrame frame, Packet? packet)
    {
        if (packet?.Extract<TcpPacket>() is not { } tcp || packet.Extract<IPPacket>() is not { } ip) return null;
        return new TcpSegmentInfo(frame.Number, frame.Timestamp,
            ip.SourceAddress.ToString(), tcp.SourcePort, ip.DestinationAddress.ToString(), tcp.DestinationPort,
            tcp.SequenceNumber, tcp.AcknowledgmentNumber, tcp.Synchronize, tcp.Acknowledgment, tcp.Finished, tcp.Reset,
            tcp.Push, tcp.PayloadData ?? []);
    }

    public static string TcpFlagsText(TcpPacket tcp)
    {
        var parts = new List<string>(6);
        if (tcp.Synchronize) parts.Add("SYN");
        if (tcp.Finished) parts.Add("FIN");
        if (tcp.Reset) parts.Add("RST");
        if (tcp.Push) parts.Add("PSH");
        if (tcp.Acknowledgment) parts.Add("ACK");
        if (tcp.Urgent) parts.Add("URG");
        return parts.Count == 0 ? "nenhuma" : string.Join(", ", parts);
    }

    public static string Printable(ReadOnlySpan<byte> data, int max)
    {
        var sb = new StringBuilder(Math.Min(data.Length, max));
        foreach (var b in data[..Math.Min(data.Length, max)]) sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        if (data.Length > max) sb.Append('…');
        return sb.ToString();
    }
}

public static class HexDump
{
    /// <summary>Formato clássico: deslocamento, 16 bytes em hexadecimal, ASCII.</summary>
    public static string Format(ReadOnlySpan<byte> data, int maxBytes = 64 * 1024)
    {
        var len = Math.Min(data.Length, maxBytes);
        var sb = new StringBuilder(len / 16 * 78 + 80);
        for (var row = 0; row < len; row += 16)
        {
            sb.Append(row.ToString("X6", CultureInfo.InvariantCulture)).Append("  ");
            for (var i = 0; i < 16; i++)
            {
                if (row + i < len) sb.Append(data[row + i].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
                else sb.Append("   ");
                if (i == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (var i = 0; i < 16 && row + i < len; i++)
            {
                var b = data[row + i];
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }
            sb.Append('\n');
        }
        if (data.Length > maxBytes) sb.Append($"… {data.Length - maxBytes} bytes adicionais não exibidos. Use Exportar.\n");
        return sb.ToString();
    }
}
