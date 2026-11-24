using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Codec;

namespace HoloRepository.Core.Imaging;

public sealed record DicomLoadResult(LoadedSeries Series, IReadOnlyList<string> Warnings);

/// <summary>
/// Reads a folder of DICOM files into a <see cref="Volume"/>. Files are grouped by SeriesInstanceUID,
/// the largest series is chosen and slices are ordered along the slice normal.
/// All processing happens locally; no pixel data leaves the machine.
/// </summary>
public static class DicomSeriesLoader
{
    private sealed record SliceFile(DicomDataset Dataset, double Position, int InstanceNumber, int Frames);

    public static Task<DicomLoadResult> LoadDirectoryAsync(string directory, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadDirectory(directory, progress, cancellationToken), cancellationToken);

    public static DicomLoadResult LoadDirectory(string directory, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Folder not found: {directory}");

        var warnings = new List<string>();
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToList();
        var bySeries = new Dictionary<string, List<SliceFile>>();
        int skipped = 0;

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(0.6 * i / Math.Max(1, files.Count));

            DicomFile file;
            try
            {
                if (!DicomFile.HasValidHeader(files[i]))
                {
                    skipped++;
                    continue;
                }
                file = DicomFile.Open(files[i]);
            }
            catch (Exception)
            {
                skipped++;
                continue;
            }

            var ds = file.Dataset;
            if (!ds.Contains(DicomTag.PixelData))
            {
                skipped++;
                continue;
            }

            string uid = ds.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "unknown-series");
            if (!bySeries.TryGetValue(uid, out var list)) bySeries[uid] = list = new List<SliceFile>();
            list.Add(new SliceFile(ds, SlicePosition(ds), ds.GetSingleValueOrDefault(DicomTag.InstanceNumber, 0),
                ds.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1)));
        }

        if (skipped > 0) warnings.Add($"Skipped {skipped} file(s) that were not DICOM images.");
        if (bySeries.Count == 0)
            throw new InvalidDataException("No DICOM image files were found in the selected folder.");
        if (bySeries.Count > 1)
            warnings.Add($"Found {bySeries.Count} series; loaded the one with the most images.");

        var slices = bySeries.Values.OrderByDescending(l => l.Count).First()
            .OrderBy(s => s.Position).ThenBy(s => s.InstanceNumber).ToList();

        var result = BuildVolume(directory, slices, warnings, progress, cancellationToken);
        progress?.Report(1);
        return new DicomLoadResult(result, warnings);
    }

    private static LoadedSeries BuildVolume(string sourceDirectory, List<SliceFile> slices, List<string> warnings, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var first = slices[0].Dataset;
        int rows = first.GetSingleValue<ushort>(DicomTag.Rows);
        int cols = first.GetSingleValue<ushort>(DicomTag.Columns);

        double rowSpacing = 1, colSpacing = 1;
        if (first.TryGetValues<double>(DicomTag.PixelSpacing, out var ps) && ps.Length >= 2)
        {
            rowSpacing = ps[0];
            colSpacing = ps[1];
        }
        else
        {
            warnings.Add("PixelSpacing missing; assuming 1 mm pixels.");
        }

        // Multi-frame objects store each slice as a frame of one file.
        bool multiFrame = slices.Count == 1 && slices[0].Frames > 1;
        int depth = multiFrame ? slices[0].Frames : slices.Count;
        double sliceSpacing = EstimateSliceSpacing(slices, first, multiFrame);

        var volume = new Volume(cols, rows, depth, new VoxelSpacing(colSpacing, rowSpacing, sliceSpacing))
        {
            Frame = FrameOf(first),
        };
        int perSlice = rows * cols;

        for (int z = 0; z < depth; z++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(0.6 + 0.4 * z / depth);

            var ds = multiFrame ? slices[0].Dataset : slices[z].Dataset;
            int frame = multiFrame ? z : 0;
            var pixels = ReadFrame(ds, frame, perSlice);
            Array.Copy(pixels, 0, volume.Voxels, (long)z * perSlice, perSlice);
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
            Source = "DICOM folder",
            SourcePath = sourceDirectory,
        };
        return new LoadedSeries(metadata, volume);
    }

    private static short[] ReadFrame(DicomDataset ds, int frame, int expected)
    {
        if (ds.InternalTransferSyntax.IsEncapsulated)
        {
            try
            {
                ds = ds.Clone(DicomTransferSyntax.ExplicitVRLittleEndian);
            }
            catch (Exception ex)
            {
                throw new NotSupportedException(
                    $"Compressed transfer syntax '{ds.InternalTransferSyntax.UID.Name}' is not supported by this prototype.", ex);
            }
        }

        var pixelData = DicomPixelData.Create(ds);
        byte[] bytes = pixelData.GetFrame(frame).Data;
        double slope = ds.GetSingleValueOrDefault(DicomTag.RescaleSlope, 1.0);
        double intercept = ds.GetSingleValueOrDefault(DicomTag.RescaleIntercept, 0.0);
        bool signed = pixelData.PixelRepresentation == PixelRepresentation.Signed;
        int bitsAllocated = pixelData.BitsAllocated;

        var result = new short[expected];
        for (int i = 0; i < expected; i++)
        {
            double raw;
            if (bitsAllocated == 16)
            {
                int o = i * 2;
                if (o + 1 >= bytes.Length) break;
                ushort u = (ushort)(bytes[o] | (bytes[o + 1] << 8));
                raw = signed ? (short)u : u;
            }
            else if (bitsAllocated == 8)
            {
                if (i >= bytes.Length) break;
                raw = signed ? (sbyte)bytes[i] : bytes[i];
            }
            else
            {
                throw new NotSupportedException($"BitsAllocated={bitsAllocated} is not supported.");
            }

            result[i] = (short)Math.Clamp(raw * slope + intercept, short.MinValue, short.MaxValue);
        }
        return result;
    }

    /// <summary>Patient frame from the first (most inferior along the normal) slice's position and orientation.</summary>
    private static PatientFrame FrameOf(DicomDataset first)
    {
        if (!first.TryGetValues<double>(DicomTag.ImagePositionPatient, out var pos) || pos.Length < 3)
            return PatientFrame.Identity;
        var origin = new Point3(pos[0], pos[1], pos[2]);
        if (!first.TryGetValues<double>(DicomTag.ImageOrientationPatient, out var o) || o.Length < 6)
            return PatientFrame.Identity with { Origin = origin };

        var row = new Point3(o[0], o[1], o[2]);
        var col = new Point3(o[3], o[4], o[5]);
        var normal = new Point3(row.Y * col.Z - row.Z * col.Y, row.Z * col.X - row.X * col.Z, row.X * col.Y - row.Y * col.X);
        return new PatientFrame(origin, row, col, normal);
    }

    /// <summary>Distance of the slice along its normal, so slices sort correctly for any acquisition plane.</summary>
    private static double SlicePosition(DicomDataset ds)
    {
        if (ds.TryGetValues<double>(DicomTag.ImagePositionPatient, out var pos) && pos.Length >= 3)
        {
            if (ds.TryGetValues<double>(DicomTag.ImageOrientationPatient, out var o) && o.Length >= 6)
            {
                double nx = o[1] * o[5] - o[2] * o[4];
                double ny = o[2] * o[3] - o[0] * o[5];
                double nz = o[0] * o[4] - o[1] * o[3];
                return pos[0] * nx + pos[1] * ny + pos[2] * nz;
            }
            return pos[2];
        }
        return ds.GetSingleValueOrDefault(DicomTag.InstanceNumber, 0);
    }

    private static double EstimateSliceSpacing(List<SliceFile> slices, DicomDataset first, bool multiFrame)
    {
        if (!multiFrame && slices.Count > 1)
        {
            var gaps = new List<double>();
            for (int i = 1; i < slices.Count; i++)
                gaps.Add(Math.Abs(slices[i].Position - slices[i - 1].Position));
            gaps.Sort();
            double median = gaps[gaps.Count / 2];
            if (median > 1e-3) return median;
        }

        double spacing = first.GetSingleValueOrDefault(DicomTag.SpacingBetweenSlices, 0.0);
        if (spacing <= 0) spacing = first.GetSingleValueOrDefault(DicomTag.SliceThickness, 1.0);
        return spacing > 0 ? spacing : 1.0;
    }
}
