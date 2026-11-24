using System.IO.Compression;
using HoloRepository.Core.Imaging;

namespace HoloRepository.Core.Segmentation;

/// <summary>
/// Runs segmentation on the HoloRepository server: zips the series as DICOM, uploads it with the chosen model,
/// waits for the job, then downloads the segmented NIfTI and the MZ3 mesh and aligns them with the series.
/// </summary>
public sealed class RemoteSegmentationService : ISegmentationService
{
    private readonly HoloRepositoryApiClient _api;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public RemoteSegmentationService(HoloRepositoryApiClient api, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _api = api;
        _delay = delay ?? Task.Delay;
    }

    public string Name => "HoloRepository server";

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(30);

    public async Task<SegmentationResult> SegmentAsync(SegmentationRequest request, IProgress<SegmentationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var volume = request.Series.Volume;
        string work = Path.Combine(Path.GetTempPath(), "HoloRepository", "upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            progress?.Report(new SegmentationProgress(SegmentationStage.Preparing, "Preparing the scan for upload…"));
            string zip = await Task.Run(() => PrepareZip(request.Series, work, cancellationToken), cancellationToken).ConfigureAwait(false);

            // The contract does not return a record id with the job, so note which records exist beforehand.
            var before = (await _api.GetRecordsAsync(cancellationToken).ConfigureAwait(false)).Select(r => r.Id).ToHashSet();

            progress?.Report(new SegmentationProgress(SegmentationStage.Uploading, "Uploading the scan securely…"));
            ApiJob job;
            await using (var file = File.OpenRead(zip))
                job = await _api.CreateJobAsync(file, "series.zip", ApiFileType.Dicom, request.Model.Id, cancellationToken).ConfigureAwait(false);

            await WaitForJobAsync(job.Id, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report(new SegmentationProgress(SegmentationStage.Downloading, "Downloading the results…"));
            var records = await _api.GetRecordsAsync(cancellationToken).ConfigureAwait(false);
            var record = records.LastOrDefault(r => !before.Contains(r.Id))
                         ?? throw new InvalidOperationException("The server finished but no new results were found.");

            byte[]? labels = null;
            SurfaceMesh? serverMesh = null;
            foreach (var f in record.Files)
            {
                if (f.FileType == ApiFileType.NiftiSegmented)
                {
                    var bytes = await _api.DownloadFileAsync(f.Id, cancellationToken).ConfigureAwait(false);
                    var nifti = NiftiImage.Read(new MemoryStream(bytes));
                    labels = await Task.Run(() => nifti.ResampleLabelsTo(volume), cancellationToken).ConfigureAwait(false);
                }
                else if (f.FileType == ApiFileType.Mz3)
                {
                    var bytes = await _api.DownloadFileAsync(f.Id, cancellationToken).ConfigureAwait(false);
                    // Server meshes are in RAS millimetres (NiiVue convention); convert to this volume's space.
                    serverMesh = Mz3.Read(new MemoryStream(bytes))
                        .Transform(ras => volume.FromPatient(PatientFrame.RasToLps(ras)));
                }
            }

            progress?.Report(new SegmentationProgress(SegmentationStage.Building, "Building 3D surfaces…"));
            var structures = await Task.Run(() => BuildStructures(volume, labels, serverMesh), cancellationToken).ConfigureAwait(false);
            progress?.Report(new SegmentationProgress(SegmentationStage.Done, "Done"));

            return new SegmentationResult
            {
                SeriesId = request.SeriesId,
                Model = request.Model,
                Labels = labels,
                Structures = structures,
                Source = Name,
                Note = structures.Count == 0 ? "The model did not find any structures in this scan." : null,
            };
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private async Task WaitForJobAsync(string jobId, IProgress<SegmentationProgress>? progress, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            var status = await _api.GetJobStatusAsync(jobId, cancellationToken).ConfigureAwait(false);
            switch (status.Status)
            {
                case ApiJobState.Done:
                    return;
                case ApiJobState.Error:
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(status.Msg)
                        ? "The server could not process this scan."
                        : $"The server could not process this scan: {status.Msg}");
                case ApiJobState.InQueue:
                    progress?.Report(new SegmentationProgress(SegmentationStage.Queued, "Waiting for the server…"));
                    break;
                default:
                    progress?.Report(new SegmentationProgress(SegmentationStage.Processing, status.Msg ?? "Analysing the scan…"));
                    break;
            }

            if (DateTime.UtcNow - started > Timeout)
                throw new TimeoutException("The server is taking too long. Please try again later.");
            await _delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Zips the original DICOM folder when there is one; otherwise writes the series out as DICOM first.</summary>
    internal static string PrepareZip(LoadedSeries series, string work, CancellationToken cancellationToken)
    {
        string source = series.Metadata.SourcePath is { } path && Directory.Exists(path) ? path : Path.Combine(work, "dicom");
        if (source != series.Metadata.SourcePath) DicomSeriesWriter.Write(series, source, cancellationToken);
        string zip = Path.Combine(work, "series.zip");
        ZipFile.CreateFromDirectory(source, zip, CompressionLevel.Fastest, includeBaseDirectory: false);
        return zip;
    }

    /// <summary>
    /// Uses the label map for per-structure colours and volumes. The server's mesh merges every label, so it is
    /// used directly only when there is a single structure or no label map.
    /// </summary>
    internal static IReadOnlyList<SegmentedStructure> BuildStructures(Volume volume, byte[]? labels, SurfaceMesh? serverMesh)
    {
        if (labels is not null)
        {
            var structures = SegmentationResult.DescribeLabels(volume, labels);
            if (structures.Count == 1 && serverMesh is not null)
                return [structures[0] with { Mesh = serverMesh }];
            return structures;
        }

        if (serverMesh is not null)
            return [new SegmentedStructure(1, "Segmented structures", LabelPalette.ColourFor(1, "structures"), 0, 0, serverMesh)];

        return Array.Empty<SegmentedStructure>();
    }
}
