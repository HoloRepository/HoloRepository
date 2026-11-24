using HoloRepository.Core.Imaging;

namespace HoloRepository.Core.Daemon;

/// <summary>Hardware acceleration targets named in the brief (FR-04 / FR-08).</summary>
public enum ComputeBackend
{
    Cpu,
    NvidiaCuda,
    IntelArc,
    RtxSparkArm,
}

public static class ComputeBackendExtensions
{
    public static string DisplayName(this ComputeBackend backend) => backend switch
    {
        ComputeBackend.Cpu => "CPU",
        ComputeBackend.NvidiaCuda => "NVIDIA CUDA",
        ComputeBackend.IntelArc => "Intel ARC (OpenVINO / oneAPI)",
        ComputeBackend.RtxSparkArm => "RTX Spark (ARM64)",
        _ => backend.ToString(),
    };
}

public sealed record BackendCapability(ComputeBackend Backend, bool Detected, bool Active, string Detail)
{
    public string Name => Backend.DisplayName();
}

public sealed record DaemonStatus(
    string Mode,
    string Endpoint,
    bool Online,
    bool CloudEgressDisabled,
    ComputeBackend ActiveBackend,
    IReadOnlyList<BackendCapability> Backends,
    int LoadedSeriesCount);

/// <summary>Summary of a series held by the daemon. Pixel data stays on the daemon side.</summary>
public sealed record SeriesInfo(
    string Id,
    StudyMetadata Metadata,
    int Width,
    int Height,
    int Depth,
    VoxelSpacing Spacing,
    IReadOnlyList<string> Warnings)
{
    public string Dimensions => $"{Width} × {Height} × {Depth}";
    public string SpacingText => $"{Spacing.X:0.##} × {Spacing.Y:0.##} × {Spacing.Z:0.##} mm";
}

/// <summary>
/// Client-side contract for the HoloRepository local compute daemon (FR-03).
/// The prototype ships <see cref="InProcessDaemonClient"/>; a gRPC/REST/WebSocket client for the real
/// daemon can implement the same interface without UI changes.
/// </summary>
public interface IComputeDaemonClient
{
    IReadOnlyList<SeriesInfo> LoadedSeries { get; }

    ComputeBackend PreferredBackend { get; set; }

    Task<DaemonStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<SeriesInfo> LoadSampleAsync(CancellationToken cancellationToken = default);

    Task<SeriesInfo> LoadDicomDirectoryAsync(string directory, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Finds the DICOM series in any mix of files, folders and ZIP archives.</summary>
    Task<DicomScanResult> ScanDicomAsync(IEnumerable<string> paths, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Loads one series from a previous scan.</summary>
    Task<SeriesInfo> LoadDicomSeriesAsync(DicomScanResult scan, string seriesInstanceUid, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task ExportSeriesAsDicomAsync(string seriesId, string directory, CancellationToken cancellationToken = default);

    Task<SliceImage> GetSliceAsync(string seriesId, SlicePlane plane, int index, CancellationToken cancellationToken = default);

    Task<RenderedImage> RenderVolumeAsync(string seriesId, VolumeRenderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns the volume geometry needed for coordinate conversion (voxels are not transferred by a remote daemon).</summary>
    Volume GetGeometry(string seriesId);
}
