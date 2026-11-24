using System.IO.Compression;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Codec;

namespace HoloRepository.Core.Imaging;

public sealed record DicomLoadResult(LoadedSeries Series, IReadOnlyList<string> Warnings);

/// <summary>A series found while scanning files, folders or ZIP archives.</summary>
public sealed record DicomSeriesSummary(
    string SeriesInstanceUid,
    string Description,
    string Modality,
    string PatientName,
    string PatientId,
    DateTime? StudyDate,
    int SeriesNumber,
    int ImageCount,
    int FrameCount,
    string? UnsupportedReason)
{
    public bool IsSupported => UnsupportedReason is null;

    public string Title => string.IsNullOrWhiteSpace(Description) ? $"Series {SeriesNumber}" : Description;

    public string Detail
    {
        get
        {
            string images = FrameCount > ImageCount ? $"{FrameCount} images (multi-frame)" : $"{ImageCount} images";
            string date = StudyDate is DateTime d ? $" · {d:dd MMM yyyy}" : string.Empty;
            return $"{Modality} · {images}{date}" + (UnsupportedReason is null ? string.Empty : $" · {UnsupportedReason}");
        }
    }
}

/// <summary>Result of scanning for DICOM data. Pass it to <see cref="DicomSeriesLoader.Load"/> with the chosen series.</summary>
public sealed class DicomScanResult
{
    internal DicomScanResult(IReadOnlyList<DicomSeriesSummary> series, IReadOnlyList<string> warnings,
        Dictionary<string, List<DicomDataset>> instances, string? sourcePath)
    {
        Series = series;
        Warnings = warnings;
        Instances = instances;
        SourcePath = sourcePath;
    }

    public IReadOnlyList<DicomSeriesSummary> Series { get; }
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>The single folder that was scanned, if any (used when re-uploading the original files).</summary>
    public string? SourcePath { get; }

    internal Dictionary<string, List<DicomDataset>> Instances { get; }

    /// <summary>The supported series with the most images, which is the sensible default to open.</summary>
    public DicomSeriesSummary? Largest =>
        Series.Where(s => s.IsSupported).OrderByDescending(s => Math.Max(s.ImageCount, s.FrameCount)).FirstOrDefault();
}

/// <summary>
/// Reads DICOM data into a <see cref="Volume"/>: single files, folders (recursively, including DICOMDIR layouts)
/// and ZIP archives. Handles compressed transfer syntaxes via native codecs, Enhanced multi-frame CT/MR,
/// MONOCHROME1 data and series that contain scout images. All processing happens locally.
/// </summary>
public static class DicomSeriesLoader
{
    private static readonly HashSet<string> NotDicomExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".xml", ".html", ".htm", ".pdf", ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".exe", ".dll", ".ini", ".json", ".csv", ".md",
        ".nii", ".gz", ".mz3", ".log", ".db", ".lnk",
    };

    static DicomSeriesLoader() => DicomCodecs.EnsureRegistered();

    // ---- Scanning ------------------------------------------------------------------------------

    public static Task<DicomScanResult> ScanAsync(IEnumerable<string> paths, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(paths, progress, cancellationToken), cancellationToken);

    /// <summary>Finds every DICOM series in the given files, folders and ZIP archives.</summary>
    public static DicomScanResult Scan(IEnumerable<string> paths, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = paths.ToList();
        if (inputs.Count == 0) throw new ArgumentException("Nothing to open.", nameof(paths));
        foreach (var p in inputs)
            if (!File.Exists(p) && !Directory.Exists(p)) throw new FileNotFoundException($"Not found: {p}", p);

        var files = inputs.SelectMany(p => Directory.Exists(p)
            ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)
            : [p]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var bySeries = new Dictionary<string, List<DicomDataset>>();
        int notDicom = 0, noImage = 0;

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((double)i / Math.Max(1, files.Count));
            string path = files[i];

            if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var ds in ReadZip(path, ref notDicom, cancellationToken)) Add(ds);
                continue;
            }
            if (NotDicomExtensions.Contains(Path.GetExtension(path)) || Path.GetFileName(path).StartsWith('.'))
            {
                notDicom++;
                continue;
            }

            var dataset = TryOpen(path);
            if (dataset is null) notDicom++;
            else Add(dataset);
        }

        void Add(DicomDataset ds)
        {
            if (!ds.Contains(DicomTag.PixelData))
            {
                noImage++;
                return;
            }
            string uid = ds.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "unknown-series");
            if (!bySeries.TryGetValue(uid, out var list)) bySeries[uid] = list = new List<DicomDataset>();
            list.Add(ds);
        }

        var warnings = new List<string>();
        if (notDicom > 0) warnings.Add($"Skipped {notDicom} file(s) that were not DICOM.");
        if (noImage > 0) warnings.Add($"Skipped {noImage} DICOM object(s) without images (e.g. DICOMDIR or reports).");
        if (bySeries.Count == 0) throw new InvalidDataException("No DICOM images were found.");

        var summaries = bySeries.Select(kv => Summarise(kv.Key, kv.Value))
            .OrderBy(s => s.StudyDate ?? DateTime.MaxValue).ThenBy(s => s.SeriesNumber).ToList();
        progress?.Report(1);
        string? source = inputs.Count == 1 && Directory.Exists(inputs[0]) ? inputs[0] : null;
        return new DicomScanResult(summaries, warnings, bySeries, source);
    }

    private static DicomDataset? TryOpen(string path)
    {
        try
        {
            return DicomFile.Open(path).Dataset;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<DicomDataset> ReadZip(string zipPath, ref int notDicom, CancellationToken cancellationToken)
    {
        var found = new List<DicomDataset>();
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Length == 0 || entry.FullName.EndsWith('/')) continue;
            if (NotDicomExtensions.Contains(Path.GetExtension(entry.Name)))
            {
                notDicom++;
                continue;
            }
            try
            {
                using var s = entry.Open();
                var ms = new MemoryStream();
                s.CopyTo(ms);
                ms.Position = 0;
                found.Add(DicomFile.Open(ms, FileReadOption.ReadAll).Dataset);
            }
            catch (Exception)
            {
                notDicom++;
            }
        }
        return found;
    }

    private static DicomSeriesSummary Summarise(string uid, List<DicomDataset> items)
    {
        var first = items[0];
        int frames = items.Sum(d => d.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1));
        string? unsupported = null;
        int samples = first.GetSingleValueOrDefault<ushort>(DicomTag.SamplesPerPixel, 1);
        int bits = first.GetSingleValueOrDefault<ushort>(DicomTag.BitsAllocated, 16);
        if (samples > 1) unsupported = "colour images are not supported yet";
        else if (bits is not (8 or 16 or 32)) unsupported = $"{bits}-bit images are not supported";

        return new DicomSeriesSummary(
            uid,
            first.GetSingleValueOrDefault(DicomTag.SeriesDescription, string.Empty).Trim(),
            first.GetSingleValueOrDefault(DicomTag.Modality, "OT"),
            first.GetSingleValueOrDefault(DicomTag.PatientName, "Anonymous").Replace('^', ' ').Trim(),
            first.GetSingleValueOrDefault(DicomTag.PatientID, "UNKNOWN"),
            first.TryGetSingleValue(DicomTag.StudyDate, out DateTime date) ? date : null,
            first.GetSingleValueOrDefault(DicomTag.SeriesNumber, 0),
            items.Count,
            frames,
            unsupported);
    }

    // ---- Loading -------------------------------------------------------------------------------

    public static Task<DicomLoadResult> LoadDirectoryAsync(string directory, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadDirectory(directory, progress, cancellationToken), cancellationToken);

    /// <summary>Scans a folder and loads its largest supported series.</summary>
    public static DicomLoadResult LoadDirectory(string directory, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Folder not found: {directory}");
        var scan = Scan([directory], null, cancellationToken);
        var chosen = scan.Largest ?? throw new InvalidDataException(
            "The DICOM images found are not supported: " + scan.Series[0].UnsupportedReason + ".");
        var result = Load(scan, chosen.SeriesInstanceUid, progress, cancellationToken);
        if (scan.Series.Count > 1)
            return result with { Warnings = [.. result.Warnings, $"Found {scan.Series.Count} series; loaded the one with the most images."] };
        return result;
    }

    public static Task<DicomLoadResult> LoadAsync(DicomScanResult scan, string seriesInstanceUid, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(scan, seriesInstanceUid, progress, cancellationToken), cancellationToken);

    public static DicomLoadResult Load(DicomScanResult scan, string seriesInstanceUid, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!scan.Instances.TryGetValue(seriesInstanceUid, out var items))
            throw new KeyNotFoundException("That series is not part of this scan.");
        var summary = scan.Series.First(s => s.SeriesInstanceUid == seriesInstanceUid);
        if (!summary.IsSupported) throw new NotSupportedException($"This series cannot be opened: {summary.UnsupportedReason}.");

        var warnings = scan.Warnings.ToList();
        var slices = items.Count == 1 && items[0].GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1) > 1
            ? MultiFrameSlices(items[0], warnings)
            : SingleFrameSlices(items, warnings);

        var series = BuildVolume(slices, items[0], scan.SourcePath, warnings, progress, cancellationToken);
        progress?.Report(1);
        return new DicomLoadResult(series, warnings);
    }

    /// <summary>One image plane: which dataset/frame it comes from and where it sits in the patient.</summary>
    private sealed record Slice(DicomDataset Dataset, int Frame, Point3? Position, Point3 Row, Point3 Column,
        double RowSpacing, double ColumnSpacing, double Slope, double Intercept, int InstanceNumber)
    {
        public Point3 Normal => Cross(Row, Column);

        public double Distance => Position is Point3 p ? Dot(p, Normal) : InstanceNumber;
    }

    private static List<Slice> SingleFrameSlices(List<DicomDataset> items, List<string> warnings)
    {
        var all = items.Select(ds =>
        {
            var (row, col) = Orientation(ds) ?? (new Point3(1, 0, 0), new Point3(0, 1, 0));
            var (rs, cs) = PixelSpacing(ds, warnings);
            return new Slice(ds, 0, Position(ds), row, col, rs, cs,
                ds.GetSingleValueOrDefault(DicomTag.RescaleSlope, 1.0), ds.GetSingleValueOrDefault(DicomTag.RescaleIntercept, 0.0),
                ds.GetSingleValueOrDefault(DicomTag.InstanceNumber, 0));
        }).ToList();

        // Keep the largest group with the same size and orientation; drops scouts and localisers.
        var groups = all.GroupBy(s => (
                Rows: s.Dataset.GetSingleValueOrDefault<ushort>(DicomTag.Rows, 0),
                Cols: s.Dataset.GetSingleValueOrDefault<ushort>(DicomTag.Columns, 0),
                Orientation: $"{s.Row.X:0.00},{s.Row.Y:0.00},{s.Row.Z:0.00},{s.Column.X:0.00},{s.Column.Y:0.00},{s.Column.Z:0.00}"))
            .OrderByDescending(g => g.Count()).ToList();
        var kept = groups[0].ToList();
        int dropped = all.Count - kept.Count;
        if (dropped > 0)
            warnings.Add($"Ignored {dropped} image(s) with a different size or orientation (for example scout images).");

        var ordered = kept.OrderBy(s => s.Distance).ThenBy(s => s.InstanceNumber).ToList();

        // Several images at one position (e.g. multiple phases or echoes): keep the first of each.
        var unique = new List<Slice>();
        foreach (var s in ordered)
            if (unique.Count == 0 || Math.Abs(unique[^1].Distance - s.Distance) > 1e-3 || s.Position is null)
                unique.Add(s);
        if (unique.Count < ordered.Count)
            warnings.Add($"The series has {ordered.Count - unique.Count} extra image(s) at positions already used (for example other phases); showing the first at each position.");
        return unique;
    }

    /// <summary>Enhanced (and legacy) multi-frame objects: one file, many frames.</summary>
    private static List<Slice> MultiFrameSlices(DicomDataset ds, List<string> warnings)
    {
        int frames = ds.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1);
        var shared = FirstItem(ds, DicomTag.SharedFunctionalGroupsSequence);
        ds.TryGetSequence(DicomTag.PerFrameFunctionalGroupsSequence, out var perFrame);

        var slices = new List<Slice>(frames);
        for (int f = 0; f < frames; f++)
        {
            var frameGroup = perFrame is not null && f < perFrame.Items.Count ? perFrame.Items[f] : null;
            DicomDataset? Group(DicomTag tag) => (frameGroup is null ? null : FirstItem(frameGroup, tag)) ?? (shared is null ? null : FirstItem(shared, tag));

            var orientationSource = Group(DicomTag.PlaneOrientationSequence) ?? ds;
            var (row, col) = Orientation(orientationSource) ?? (new Point3(1, 0, 0), new Point3(0, 1, 0));
            var position = Group(DicomTag.PlanePositionSequence) is { } pp ? Position(pp) : null;
            var (rs, cs) = PixelSpacing(Group(DicomTag.PixelMeasuresSequence) ?? ds, f == 0 ? warnings : null);
            var transform = Group(DicomTag.PixelValueTransformationSequence) ?? ds;
            slices.Add(new Slice(ds, f, position, row, col, rs, cs,
                transform.GetSingleValueOrDefault(DicomTag.RescaleSlope, 1.0), transform.GetSingleValueOrDefault(DicomTag.RescaleIntercept, 0.0), f));
        }

        if (slices.All(s => s.Position is not null)) slices = slices.OrderBy(s => s.Distance).ToList();
        return slices;
    }

    private static LoadedSeries BuildVolume(List<Slice> slices, DicomDataset first, string? sourcePath, List<string> warnings,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var head = slices[0];
        int rows = head.Dataset.GetSingleValue<ushort>(DicomTag.Rows);
        int cols = head.Dataset.GetSingleValue<ushort>(DicomTag.Columns);
        double sliceSpacing = EstimateSliceSpacing(slices, head.Dataset);

        var frame = head.Position is Point3 origin
            ? new PatientFrame(origin, head.Row, head.Column, head.Normal)
            : PatientFrame.Identity;
        var volume = new Volume(cols, rows, slices.Count, new VoxelSpacing(head.ColumnSpacing, head.RowSpacing, sliceSpacing)) { Frame = frame };

        int perSlice = rows * cols;
        var decoded = new Dictionary<DicomDataset, DicomPixelData>(ReferenceEqualityComparer.Instance);
        bool monochrome1 = first.GetSingleValueOrDefault(DicomTag.PhotometricInterpretation, "MONOCHROME2").Trim() == "MONOCHROME1";

        for (int z = 0; z < slices.Count; z++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((double)z / slices.Count);
            var s = slices[z];
            if (!decoded.TryGetValue(s.Dataset, out var pixelData))
            {
                pixelData = DicomPixelData.Create(Decompress(s.Dataset));
                decoded[s.Dataset] = pixelData;
            }
            ReadFrame(pixelData, s.Frame, volume.Voxels.AsSpan(z * perSlice, perSlice), s.Slope, s.Intercept, monochrome1);
            if (!ReferenceEquals(s.Dataset, slices[Math.Min(z + 1, slices.Count - 1)].Dataset)) decoded.Remove(s.Dataset);
        }

        var metadata = new StudyMetadata
        {
            PatientName = first.GetSingleValueOrDefault(DicomTag.PatientName, "Anonymous").Replace('^', ' ').Trim(),
            PatientId = first.GetSingleValueOrDefault(DicomTag.PatientID, "UNKNOWN"),
            Modality = first.GetSingleValueOrDefault(DicomTag.Modality, "OT"),
            StudyDescription = first.GetSingleValueOrDefault(DicomTag.StudyDescription, string.Empty),
            SeriesDescription = first.GetSingleValueOrDefault(DicomTag.SeriesDescription, string.Empty),
            StudyInstanceUid = first.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty),
            SeriesInstanceUid = first.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty),
            StudyDate = first.TryGetSingleValue(DicomTag.StudyDate, out DateTime date) ? date : null,
            Source = "DICOM",
            SourcePath = sourcePath,
            DefaultWindow = DefaultWindow(first, volume, monochrome1),
        };
        return new LoadedSeries(metadata, volume);
    }

    /// <summary>Converts compressed pixel data to explicit little endian using the registered codecs.</summary>
    private static DicomDataset Decompress(DicomDataset ds)
    {
        if (!ds.InternalTransferSyntax.IsEncapsulated) return ds;
        try
        {
            return new DicomTranscoder(ds.InternalTransferSyntax, DicomTransferSyntax.ExplicitVRLittleEndian).Transcode(ds);
        }
        catch (Exception ex)
        {
            throw new NotSupportedException(
                $"The images use a compression ({ds.InternalTransferSyntax.UID.Name}) that could not be decoded on this computer.", ex);
        }
    }

    /// <summary>Decodes one frame into <paramref name="target"/>, honouring BitsStored, sign, rescale and MONOCHROME1.</summary>
    internal static void ReadFrame(DicomPixelData pixelData, int frame, Span<short> target, double slope, double intercept, bool monochrome1)
    {
        byte[] bytes = pixelData.GetFrame(frame).Data;
        int bitsAllocated = pixelData.BitsAllocated;
        int bitsStored = Math.Clamp((int)pixelData.BitsStored, 1, bitsAllocated);
        int highBit = Math.Clamp((int)pixelData.HighBit, bitsStored - 1, bitsAllocated - 1);
        int shift = highBit + 1 - bitsStored;
        long mask = (1L << bitsStored) - 1;
        long signBit = 1L << (bitsStored - 1);
        bool signed = pixelData.PixelRepresentation == PixelRepresentation.Signed;
        int bytesPer = bitsAllocated / 8;

        for (int i = 0; i < target.Length; i++)
        {
            int o = i * bytesPer;
            if (o + bytesPer > bytes.Length) break;
            long raw = bytesPer switch
            {
                1 => bytes[o],
                2 => bytes[o] | (bytes[o + 1] << 8),
                _ => (uint)(bytes[o] | (bytes[o + 1] << 8) | (bytes[o + 2] << 16) | (bytes[o + 3] << 24)),
            };
            raw = (raw >> shift) & mask;
            if (signed && (raw & signBit) != 0) raw -= 1L << bitsStored;
            if (monochrome1) raw = signed ? -raw : mask - raw;
            target[i] = (short)Math.Clamp(raw * slope + intercept, short.MinValue, short.MaxValue);
        }
    }

    /// <summary>
    /// The window the series should open with: the one stored in the DICOM header when present, a CT preset
    /// otherwise for CT, and a percentile-based window for MR, X-ray and other modalities.
    /// </summary>
    internal static WindowLevel? DefaultWindow(DicomDataset ds, Volume volume, bool monochrome1)
    {
        if (!monochrome1 && ds.TryGetValues<double>(DicomTag.WindowCenter, out var c) && c.Length > 0 &&
            ds.TryGetValues<double>(DicomTag.WindowWidth, out var w) && w.Length > 0 && w[0] > 1)
            return new WindowLevel(c[0], w[0]);

        if (ds.GetSingleValueOrDefault(DicomTag.Modality, "OT") == "CT") return null;

        var voxels = volume.Voxels;
        int step = Math.Max(1, voxels.Length / 200_000);
        var sample = new List<short>(voxels.Length / step + 1);
        for (int i = 0; i < voxels.Length; i += step) sample.Add(voxels[i]);
        sample.Sort();
        double lo = sample[(int)(sample.Count * 0.01)], hi = sample[(int)(sample.Count * 0.99)];
        return hi - lo < 1 ? null : new WindowLevel((lo + hi) / 2, hi - lo);
    }

    private static double EstimateSliceSpacing(List<Slice> slices, DicomDataset first)
    {
        if (slices.Count > 1 && slices.All(s => s.Position is not null))
        {
            var gaps = new List<double>();
            for (int i = 1; i < slices.Count; i++) gaps.Add(Math.Abs(slices[i].Distance - slices[i - 1].Distance));
            gaps.Sort();
            double median = gaps[gaps.Count / 2];
            if (median > 1e-3) return median;
        }

        var measures = FirstItem(first, DicomTag.SharedFunctionalGroupsSequence) is { } shared ? FirstItem(shared, DicomTag.PixelMeasuresSequence) : null;
        foreach (var source in new[] { measures, first })
        {
            if (source is null) continue;
            double spacing = source.GetSingleValueOrDefault(DicomTag.SpacingBetweenSlices, 0.0);
            if (spacing <= 0) spacing = source.GetSingleValueOrDefault(DicomTag.SliceThickness, 0.0);
            if (spacing > 0) return spacing;
        }
        return 1.0;
    }

    private static (double Row, double Column) PixelSpacing(DicomDataset ds, List<string>? warnings)
    {
        if (ds.TryGetValues<double>(DicomTag.PixelSpacing, out var ps) && ps.Length >= 2 && ps[0] > 0 && ps[1] > 0) return (ps[0], ps[1]);
        if (ds.TryGetValues<double>(DicomTag.ImagerPixelSpacing, out var ips) && ips.Length >= 2 && ips[0] > 0 && ips[1] > 0) return (ips[0], ips[1]);
        if (warnings is not null && !warnings.Contains(MissingSpacing)) warnings.Add(MissingSpacing);
        return (1, 1);
    }

    private const string MissingSpacing = "Pixel spacing is missing; measurements assume 1 mm pixels.";

    private static Point3? Position(DicomDataset ds) =>
        ds.TryGetValues<double>(DicomTag.ImagePositionPatient, out var p) && p.Length >= 3 ? new Point3(p[0], p[1], p[2]) : null;

    private static (Point3 Row, Point3 Column)? Orientation(DicomDataset ds) =>
        ds.TryGetValues<double>(DicomTag.ImageOrientationPatient, out var o) && o.Length >= 6
            ? (new Point3(o[0], o[1], o[2]), new Point3(o[3], o[4], o[5]))
            : null;

    private static DicomDataset? FirstItem(DicomDataset ds, DicomTag sequence) =>
        ds.TryGetSequence(sequence, out var seq) && seq.Items.Count > 0 ? seq.Items[0] : null;

    private static Point3 Cross(Point3 a, Point3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static double Dot(Point3 a, Point3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
