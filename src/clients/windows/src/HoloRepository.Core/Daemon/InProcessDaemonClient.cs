using System.Collections.Concurrent;
using HoloRepository.Core.Imaging;

namespace HoloRepository.Core.Daemon;

/// <summary>
/// Runs the daemon's responsibilities inside the app process. It behaves like the real daemon:
/// series are referenced by id, slices and renders are requested asynchronously and nothing
/// is sent off the machine.
/// </summary>
public sealed class InProcessDaemonClient : IComputeDaemonClient
{
    private readonly ConcurrentDictionary<string, LoadedSeries> _series = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _warnings = new();
    private readonly List<string> _order = new();
    private readonly object _gate = new();
    private readonly HardwareProbe _probe;

    public InProcessDaemonClient(HardwareProbe? probe = null)
    {
        _probe = probe ?? new HardwareProbe();
    }

    public ComputeBackend PreferredBackend { get; set; } = ComputeBackend.Cpu;

    public IReadOnlyList<SeriesInfo> LoadedSeries
    {
        get
        {
            lock (_gate) return _order.Select(Describe).ToList();
        }
    }

    public Task<DaemonStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        // The prototype only implements the CPU path, so it is always the active backend.
        var backends = _probe.Probe(ComputeBackend.Cpu);
        var status = new DaemonStatus(
            Mode: "In-process (prototype)",
            Endpoint: "in-proc://holorepository-daemon",
            Online: true,
            CloudEgressDisabled: true,
            ActiveBackend: ComputeBackend.Cpu,
            Backends: backends,
            LoadedSeriesCount: _series.Count);
        return Task.FromResult(status);
    }

    public Task<SeriesInfo> LoadSampleAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Register(PhantomGenerator.CreateChestSeries(), Array.Empty<string>()), cancellationToken);

    public async Task<SeriesInfo> LoadDicomDirectoryAsync(string directory, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await DicomSeriesLoader.LoadDirectoryAsync(directory, progress, cancellationToken).ConfigureAwait(false);
        return Register(result.Series, result.Warnings);
    }

    public Task<DicomScanResult> ScanDicomAsync(IEnumerable<string> paths, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        DicomSeriesLoader.ScanAsync(paths, progress, cancellationToken);

    public async Task<SeriesInfo> LoadDicomSeriesAsync(DicomScanResult scan, string seriesInstanceUid, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await DicomSeriesLoader.LoadAsync(scan, seriesInstanceUid, progress, cancellationToken).ConfigureAwait(false);
        return Register(result.Series, result.Warnings);
    }

    public Task ExportSeriesAsDicomAsync(string seriesId, string directory, CancellationToken cancellationToken = default) =>
        DicomSeriesWriter.WriteAsync(Get(seriesId), directory, cancellationToken);

    public Task<SliceImage> GetSliceAsync(string seriesId, SlicePlane plane, int index, CancellationToken cancellationToken = default)
    {
        var volume = Get(seriesId).Volume;
        return Task.Run(() => MprSlicer.Extract(volume, plane, index), cancellationToken);
    }

    public Task<RenderedImage> RenderVolumeAsync(string seriesId, VolumeRenderRequest request, CancellationToken cancellationToken = default)
    {
        var volume = Get(seriesId).Volume;
        return Task.Run(() => VolumeRenderer.Render(volume, request, cancellationToken), cancellationToken);
    }

    public Volume GetGeometry(string seriesId) => Get(seriesId).Volume;

    public StudyMetadata GetMetadata(string seriesId) => Get(seriesId).Metadata;

    private LoadedSeries Get(string seriesId) =>
        _series.TryGetValue(seriesId, out var s) ? s : throw new KeyNotFoundException($"Series '{seriesId}' is not loaded.");

    private SeriesInfo Register(LoadedSeries series, IReadOnlyList<string> warnings)
    {
        string id = Guid.NewGuid().ToString("N")[..12];
        _series[id] = series;
        _warnings[id] = warnings;
        lock (_gate) _order.Add(id);
        return Describe(id);
    }

    private SeriesInfo Describe(string id)
    {
        var s = _series[id];
        var v = s.Volume;
        return new SeriesInfo(id, s.Metadata, v.Width, v.Height, v.Depth, v.Spacing,
            _warnings.TryGetValue(id, out var w) ? w : Array.Empty<string>());
    }
}
