using System.Globalization;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;

namespace HoloRepository.Core.Imaging;

/// <summary>
/// Writes a volume as a single-frame CT DICOM series. Used to export the synthetic phantom so the
/// DICOM ingestion path can be exercised end-to-end without real patient data.
/// </summary>
public static class DicomSeriesWriter
{
    public static Task<IReadOnlyList<string>> WriteAsync(LoadedSeries series, string directory,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Write(series, directory, cancellationToken), cancellationToken);

    public static IReadOnlyList<string> Write(LoadedSeries series, string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var v = series.Volume;
        var m = series.Metadata;
        string studyUid = string.IsNullOrEmpty(m.StudyInstanceUid) ? DicomUIDGenerator.GenerateDerivedFromUUID().UID : m.StudyInstanceUid;
        string seriesUid = string.IsNullOrEmpty(m.SeriesInstanceUid) ? DicomUIDGenerator.GenerateDerivedFromUUID().UID : m.SeriesInstanceUid;
        var paths = new List<string>();
        int perSlice = v.Width * v.Height;

        for (int z = 0; z < v.Depth; z++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ds = new DicomDataset(DicomTransferSyntax.ExplicitVRLittleEndian)
            {
                { DicomTag.SOPClassUID, DicomUID.CTImageStorage },
                { DicomTag.SOPInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
                { DicomTag.StudyInstanceUID, studyUid },
                { DicomTag.SeriesInstanceUID, seriesUid },
                { DicomTag.PatientName, m.PatientName },
                { DicomTag.PatientID, m.PatientId },
                { DicomTag.Modality, m.Modality },
                { DicomTag.StudyDescription, m.StudyDescription },
                { DicomTag.SeriesDescription, m.SeriesDescription },
                { DicomTag.InstanceNumber, z + 1 },
                { DicomTag.Rows, (ushort)v.Height },
                { DicomTag.Columns, (ushort)v.Width },
                { DicomTag.SamplesPerPixel, (ushort)1 },
                { DicomTag.PhotometricInterpretation, PhotometricInterpretation.Monochrome2.Value },
                { DicomTag.BitsAllocated, (ushort)16 },
                { DicomTag.BitsStored, (ushort)16 },
                { DicomTag.HighBit, (ushort)15 },
                { DicomTag.PixelRepresentation, (ushort)1 },
                { DicomTag.RescaleIntercept, "0" },
                { DicomTag.RescaleSlope, "1" },
                { DicomTag.WindowCenter, "40" },
                { DicomTag.WindowWidth, "400" },
            };
            if (m.StudyDate is DateTime date) ds.Add(DicomTag.StudyDate, date);

            ds.Add(DicomTag.PixelSpacing, Ds(v.Spacing.Y), Ds(v.Spacing.X));
            ds.Add(DicomTag.SliceThickness, Ds(v.Spacing.Z));
            var f = v.Frame;
            var position = v.ToPatient(v.VoxelToMm(0, 0, z));
            ds.Add(DicomTag.ImagePositionPatient, Ds(position.X), Ds(position.Y), Ds(position.Z));
            ds.Add(DicomTag.ImageOrientationPatient,
                Ds(f.XDirection.X), Ds(f.XDirection.Y), Ds(f.XDirection.Z),
                Ds(f.YDirection.X), Ds(f.YDirection.Y), Ds(f.YDirection.Z));

            var bytes = new byte[perSlice * 2];
            Buffer.BlockCopy(v.Voxels, z * perSlice * 2, bytes, 0, bytes.Length);
            var pixelData = DicomPixelData.Create(ds, true);
            pixelData.AddFrame(new MemoryByteBuffer(bytes));

            string path = Path.Combine(directory, $"IM{z + 1:0000}.dcm");
            new DicomFile(ds).Save(path);
            paths.Add(path);
        }
        return paths;
    }

    private static string Ds(double value) => Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);
}
