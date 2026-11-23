using FellowOakDicom;
using HoloRepository.Core.Imaging;

namespace HoloRepository.Core.Tests;

public sealed class TempDir : IDisposable
{
    public TempDir() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "holo-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch (IOException) { }
    }
}

public class DicomRoundTripTests
{
    [Fact]
    public void Written_series_loads_back_with_same_geometry_and_values()
    {
        using var dir = new TempDir();
        var series = PhantomGenerator.CreateChestSeries(32, 24, 12);
        var files = DicomSeriesWriter.Write(series, dir.Path);
        Assert.Equal(12, files.Count);

        // A stray non-DICOM file should be skipped with a warning.
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "readme.txt"), "not dicom");

        var result = DicomSeriesLoader.LoadDirectory(dir.Path);
        var v = result.Series.Volume;
        Assert.Equal(series.Volume.Width, v.Width);
        Assert.Equal(series.Volume.Height, v.Height);
        Assert.Equal(series.Volume.Depth, v.Depth);
        Assert.Equal(series.Volume.Spacing.X, v.Spacing.X, 4);
        Assert.Equal(series.Volume.Spacing.Y, v.Spacing.Y, 4);
        Assert.Equal(series.Volume.Spacing.Z, v.Spacing.Z, 4);
        Assert.Equal(series.Volume.Voxels, v.Voxels);
        Assert.Equal("PHANTOM CHEST", result.Series.Metadata.PatientName);
        Assert.Equal("CT", result.Series.Metadata.Modality);
        Assert.Contains(result.Warnings, w => w.Contains("Skipped 1"));
    }

    [Fact]
    public async Task Loader_orders_slices_by_position_not_file_name()
    {
        using var dir = new TempDir();
        var series = PhantomGenerator.CreateChestSeries(16, 16, 6);
        var files = await DicomSeriesWriter.WriteAsync(series, dir.Path);
        // Reverse the file names so alphabetical order disagrees with anatomical order.
        for (int i = 0; i < files.Count; i++) File.Move(files[i], System.IO.Path.Combine(dir.Path, $"tmp{i}.dcm"));
        for (int i = 0; i < files.Count; i++)
            File.Move(System.IO.Path.Combine(dir.Path, $"tmp{i}.dcm"), System.IO.Path.Combine(dir.Path, $"z{files.Count - i:00}.dcm"));

        double lastProgress = 0;
        var progress = new SynchronousProgress(p => lastProgress = p);
        var result = await DicomSeriesLoader.LoadDirectoryAsync(dir.Path, progress);
        Assert.Equal(series.Volume.Voxels, result.Series.Volume.Voxels);
        Assert.Equal(1, lastProgress);
    }

    [Fact]
    public void Loader_applies_rescale_and_picks_largest_series()
    {
        using var dir = new TempDir();
        var big = new LoadedSeries(new StudyMetadata { SeriesInstanceUid = "1.2.3.1" }, Filled(8, 8, 4, 100));
        var small = new LoadedSeries(new StudyMetadata { SeriesInstanceUid = "1.2.3.2" }, Filled(8, 8, 2, 5));
        DicomSeriesWriter.Write(big, System.IO.Path.Combine(dir.Path, "a"));
        DicomSeriesWriter.Write(small, System.IO.Path.Combine(dir.Path, "b"));

        // Rewrite one slice of the big series with slope 2 / intercept -10.
        var first = System.IO.Path.Combine(dir.Path, "a", "IM0001.dcm");
        var f = DicomFile.Open(first);
        f.Dataset.AddOrUpdate(DicomTag.RescaleSlope, "2");
        f.Dataset.AddOrUpdate(DicomTag.RescaleIntercept, "-10");
        f.Save(first);

        var result = DicomSeriesLoader.LoadDirectory(dir.Path);
        Assert.Equal(4, result.Series.Volume.Depth);
        Assert.Equal(190, result.Series.Volume[0, 0, 0]);
        Assert.Equal(100, result.Series.Volume[0, 0, 1]);
        Assert.Contains(result.Warnings, w => w.Contains("2 series"));
    }

    [Fact]
    public void Loader_reports_missing_folder_and_empty_folder()
    {
        Assert.Throws<DirectoryNotFoundException>(() => DicomSeriesLoader.LoadDirectory("Z:\\definitely\\missing\\folder"));
        using var dir = new TempDir();
        Assert.Throws<InvalidDataException>(() => DicomSeriesLoader.LoadDirectory(dir.Path));
    }

    [Fact]
    public void Loader_honours_cancellation()
    {
        using var dir = new TempDir();
        DicomSeriesWriter.Write(new LoadedSeries(new StudyMetadata(), Filled(4, 4, 2, 1)), dir.Path);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DicomSeriesLoader.LoadDirectory(dir.Path, null, cts.Token));
    }

    private static Volume Filled(int w, int h, int d, short value)
    {
        var v = new Volume(w, h, d, new VoxelSpacing(0.5, 0.5, 2));
        Array.Fill(v.Voxels, value);
        return v;
    }

    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
