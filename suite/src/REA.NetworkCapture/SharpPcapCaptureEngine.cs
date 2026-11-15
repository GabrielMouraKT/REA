using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using REA.Core;
using REA.PacketAnalyzer;
using SharpPcap;
using SharpPcap.LibPcap;

namespace REA.NetworkCapture;

/// <summary>Captura real via Npcap + SharpPcap. Somente captura passiva: nenhum pacote é enviado.</summary>
public sealed class SharpPcapCaptureEngine : ILiveCaptureEngine
{
    public string? UnavailableReason
    {
        get
        {
            var npcap = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Npcap", "wpcap.dll");
            if (!File.Exists(npcap))
                return "Npcap não está instalado. Instale de npcap.com marcando \"Support loopback traffic capture\".";
            try
            {
                _ = LibPcapLiveDeviceList.Instance.Count;
                return null;
            }
            catch (Exception ex)
            {
                return $"Npcap indisponível: {ex.Message}";
            }
        }
    }

    public IReadOnlyList<CaptureInterfaceInfo> ListInterfaces() =>
        LibPcapLiveDeviceList.New().Select(d => new CaptureInterfaceInfo(
            d.Name,
            d.Description ?? d.Name,
            d.Interface?.FriendlyName,
            d.Addresses.Select(a => a.Addr?.ipAddress?.ToString()).OfType<string>().ToList(),
            d.Loopback || d.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)))
        .ToList();

    public string? ValidateFilter(string interfaceName, string filter)
    {
        var device = Find(interfaceName);
        if (device is null) return "Interface não encontrada.";
        try
        {
            device.Open(new DeviceConfiguration { Mode = DeviceModes.None, ReadTimeout = 100 });
            device.Filter = filter;
            return null;
        }
        catch (PcapException ex)
        {
            return ex.Message;
        }
        finally
        {
            if (device.Opened) device.Close();
        }
    }

    public ILiveCapture Start(LiveCaptureOptions options, string outputPcapngPath)
    {
        var device = Find(options.InterfaceName) ?? throw new InvalidOperationException("Interface não encontrada.");
        return LiveCapture.Start(device, options, outputPcapngPath);
    }

    private static LibPcapLiveDevice? Find(string name) => LibPcapLiveDeviceList.New().FirstOrDefault(d => d.Name == name);
}

internal sealed class LiveCapture : ILiveCapture
{
    private const int UiBufferLimit = 5000;
    private readonly LibPcapLiveDevice _device;
    private readonly LiveCaptureOptions _options;
    private readonly PcapNgWriter _writer;
    private readonly object _writeLock = new();
    private readonly ConcurrentQueue<LivePacket> _uiQueue = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TaskCompletionSource<LiveCaptureResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Timer _timer;
    private long _packets, _bytes;
    private int _stopping;
    private string? _error;

    public Task<LiveCaptureResult> Completion => _completion.Task;

    private LiveCapture(LibPcapLiveDevice device, LiveCaptureOptions options, PcapNgWriter writer)
    {
        _device = device;
        _options = options;
        _writer = writer;
        _timer = new Timer(_ => OnTick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public static LiveCapture Start(LibPcapLiveDevice device, LiveCaptureOptions options, string path)
    {
        device.Open(new DeviceConfiguration
        {
            Mode = options.Promiscuous ? DeviceModes.Promiscuous : DeviceModes.None,
            ReadTimeout = 250,
            Snaplen = options.SnapLength,
        });
        try
        {
            if (!string.IsNullOrWhiteSpace(options.BpfFilter)) device.Filter = options.BpfFilter;
            var writer = new PcapNgWriter(path, (ushort)device.LinkType, options.SnapLength, device.Name,
                device.Interface?.FriendlyName ?? device.Description);
            var capture = new LiveCapture(device, options, writer);
            device.OnPacketArrival += capture.OnPacketArrival;
            device.OnCaptureStopped += capture.OnCaptureStopped;
            device.StartCapture();
            return capture;
        }
        catch
        {
            device.Close();
            throw;
        }
    }

    public LiveCaptureStats Stats
    {
        get
        {
            long dropped = 0, ifDropped = 0;
            try
            {
                if (_device.Opened && _device.Statistics is { } s) { dropped = s.DroppedPackets; ifDropped = s.InterfaceDroppedPackets; }
            }
            catch (PcapException) { }
            return new LiveCaptureStats(Interlocked.Read(ref _packets), Interlocked.Read(ref _bytes), dropped, ifDropped, _clock.Elapsed);
        }
    }

    public IReadOnlyList<LivePacket> DrainNewPackets(int max)
    {
        var list = new List<LivePacket>(Math.Min(max, _uiQueue.Count));
        while (list.Count < max && _uiQueue.TryDequeue(out var p)) list.Add(p);
        return list;
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        if (Volatile.Read(ref _stopping) != 0) return;
        var raw = e.GetPacket();
        var timestamp = new DateTimeOffset(raw.Timeval.Date, TimeSpan.Zero);
        long number;
        lock (_writeLock)
        {
            // Limites valem dentro da trava: pacotes que chegam depois do limite não são gravados.
            if (_options.MaxPackets is { } limitP && _packets >= limitP) return;
            if (_options.MaxBytes is { } limitB && _bytes >= limitB) return;
            try
            {
                _writer.WritePacket(timestamp, raw.Data, raw.PacketLength);
            }
            catch (Exception ex)
            {
                _error = $"Falha ao gravar o arquivo: {ex.Message}";
                _ = Task.Run(() => StopCore(LiveCaptureEndReason.Error));
                return;
            }
            number = Interlocked.Increment(ref _packets);
            Interlocked.Add(ref _bytes, raw.PacketLength);
        }

        var summary = PacketDecoder.Summarize(new CapturedFrame(number, 0, timestamp, (ushort)raw.LinkLayerType, raw.PacketLength, raw.Data));
        _uiQueue.Enqueue(new LivePacket(number, timestamp, raw.PacketLength,
            $"{summary.Protocol}  {summary.Source} → {summary.Destination}  {summary.Info}"));
        while (_uiQueue.Count > UiBufferLimit) _uiQueue.TryDequeue(out _);

        if (_options.MaxPackets is { } maxP && number >= maxP) _ = Task.Run(() => StopCore(LiveCaptureEndReason.PacketLimit));
        else if (_options.MaxBytes is { } maxB && Interlocked.Read(ref _bytes) >= maxB) _ = Task.Run(() => StopCore(LiveCaptureEndReason.ByteLimit));
    }

    private void OnCaptureStopped(object sender, CaptureStoppedEventStatus status)
    {
        if (status == CaptureStoppedEventStatus.ErrorWhileCapturing)
        {
            _error ??= "A interface parou de entregar pacotes (desconectada ou erro do driver).";
            _ = Task.Run(() => StopCore(LiveCaptureEndReason.Error));
        }
    }

    private void OnTick()
    {
        lock (_writeLock)
        {
            try { _writer.Flush(); } catch (IOException) { }
        }
        if (_options.MaxDuration is { } d && _clock.Elapsed >= d) _ = Task.Run(() => StopCore(LiveCaptureEndReason.DurationLimit));
    }

    public Task StopAsync() => Task.Run(() => StopCore(LiveCaptureEndReason.Stopped));

    private void StopCore(LiveCaptureEndReason reason)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        _timer.Dispose();
        var stats = Stats;
        try
        {
            if (_device.Started) _device.StopCapture();
        }
        catch (PcapException ex) { _error ??= ex.Message; }
        finally
        {
            _device.OnPacketArrival -= OnPacketArrival;
            _device.OnCaptureStopped -= OnCaptureStopped;
            if (_device.Opened) _device.Close();
            lock (_writeLock) _writer.Dispose();
        }
        _clock.Stop();
        _completion.TrySetResult(new LiveCaptureResult(_error is null ? reason : LiveCaptureEndReason.Error,
            stats with { Elapsed = _clock.Elapsed }, _error));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}

public static class NetworkCaptureServiceCollectionExtensions
{
    public static IServiceCollection AddReaNetworkCapture(this IServiceCollection services)
    {
        services.AddSingleton<ILiveCaptureEngine, SharpPcapCaptureEngine>();
        return services;
    }
}
