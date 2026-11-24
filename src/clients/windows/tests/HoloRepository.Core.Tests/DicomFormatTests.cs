using System.IO.Compression;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Codec;
using FellowOakDicom.IO.Buffer;
using HoloRepository.Core.Imaging;

namespace HoloRepository.Core.Tests;

public class DicomFormatTests
{
    private static LoadedSeries Small(int depth = 6, string? seriesUid = null, string description = "Test series")
    {
        var s = PhantomGenerator.CreateChestSeries(24, 20, depth);
        return s with
        {
            Metadata = s.Metadata with
            {
                SeriesInstanceUid = seriesUid ?? DicomUIDGenerator.GenerateDerivedFromUUID().UID,
                SeriesDescription = description,
            },
        };
    }

    [Fact]
    public void Native_codecs_are_available()
    {
        DicomCodecs.EnsureRegistered();
        Assert.True(DicomCodecs.NativeCodecsAvailable);
    }

    public static TheoryData<string> CompressedSyntaxes => new()
    {
        DicomTransferSyntax.JPEGProcess14SV1.UID.UID,
        DicomTransferSyntax.JPEGLSLossless.UID.UID,
        DicomTransferSyntax.JPEG2000Lossless.UID.UID,
        DicomTransferSyntax.RLELossless.UID.UID,
    };

    [Theory]
    [MemberData(nameof(CompressedSyntaxes))]
    public void Loads_losslessly_compressed_series(string transferSyntaxUid)
    {
        DicomCodecs.EnsureRegistered();
        var syntax = DicomTransferSyntax.Lookup(DicomUID.Parse(transferSyntaxUid));
        using var dir = new TempDir();
        // JPEG 2000 needs images of at least 32×32 for its default number of resolution levels.
        var series = PhantomGenerator.CreateChestSeries(64, 64, 4);
        foreach (var path in DicomSeriesWriter.Write(series, dir.Path))
        {
            var source = DicomFile.Open(path, FileReadOption.ReadAll);
            // Scanners usually store CT as unsigned values with a -1024 intercept; the JPEG 2000 encoder needs that form.
            if (syntax == DicomTransferSyntax.JPEG2000Lossless) source = ToUnsignedWithIntercept(source);
            var compressed = new DicomTranscoder(DicomTransferSyntax.ExplicitVRLittleEndian, syntax).Transcode(source);
            Assert.True(compressed.Dataset.InternalTransferSyntax.IsEncapsulated);
            compressed.Save(path);
        }

        var loaded = DicomSeriesLoader.LoadDirectory(dir.Path).Series.Volume;
        Assert.Equal(series.Volume.Voxels, loaded.Voxels);
    }

    [Fact]
    public void Opens_individual_files_and_zip_archives()
    {
        using var dir = new TempDir();
        var series = Small();
        var files = DicomSeriesWriter.Write(series, System.IO.Path.Combine(dir.Path, "series"));

        var fromFiles = DicomSeriesLoader.Scan(files);
        var result = DicomSeriesLoader.Load(fromFiles, fromFiles.Largest!.SeriesInstanceUid);
        Assert.Equal(series.Volume.Voxels, result.Series.Volume.Voxels);
        Assert.Null(fromFiles.SourcePath);

        string zip = System.IO.Path.Combine(dir.Path, "study.zip");
        ZipFile.CreateFromDirectory(System.IO.Path.Combine(dir.Path, "series"), zip);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        using (var note = new StreamWriter(archive.CreateEntry("notes.txt").Open()))
            note.Write("Referral letter");
        var fromZip = DicomSeriesLoader.Scan([zip]);
        Assert.Contains(fromZip.Warnings, w => w.Contains("not DICOM"));
        Assert.Equal(series.Volume.Voxels, DicomSeriesLoader.Load(fromZip, fromZip.Largest!.SeriesInstanceUid).Series.Volume.Voxels);
    }

    [Fact]
    public void Lists_every_series_and_reports_unknown_paths()
    {
        using var dir = new TempDir();
        DicomSeriesWriter.Write(Small(6, description: "Axial lung"), System.IO.Path.Combine(dir.Path, "a"));
        DicomSeriesWriter.Write(Small(3, description: "Coronal reformat"), System.IO.Path.Combine(dir.Path, "b"));

        var scan = DicomSeriesLoader.Scan([dir.Path]);
        Assert.Equal(2, scan.Series.Count);
        Assert.Equal("Axial lung", scan.Largest!.Title);
        Assert.Equal(dir.Path, scan.SourcePath);
        Assert.Contains("6 images", scan.Largest.Detail);
        Assert.Throws<KeyNotFoundException>(() => DicomSeriesLoader.Load(scan, "1.2.3.4"));
        Assert.Throws<FileNotFoundException>(() => DicomSeriesLoader.Scan(["Z:\\missing\\file.dcm"]));
        Assert.Throws<ArgumentException>(() => DicomSeriesLoader.Scan([]));
    }

    [Fact]
    public void Ignores_scouts_and_duplicate_positions()
    {
        using var dir = new TempDir();
        string uid = DicomUIDGenerator.GenerateDerivedFromUUID().UID;
        var series = Small(5, uid);
        DicomSeriesWriter.Write(series, System.IO.Path.Combine(dir.Path, "main"));
        // A second copy at the same positions (like another phase) and a differently sized scout.
        DicomSeriesWriter.Write(series, System.IO.Path.Combine(dir.Path, "phase2"));
        var scout = PhantomGenerator.CreateChestSeries(40, 40, 1);
        DicomSeriesWriter.Write(scout with { Metadata = scout.Metadata with { SeriesInstanceUid = uid } }, System.IO.Path.Combine(dir.Path, "scout"));

        var result = DicomSeriesLoader.LoadDirectory(dir.Path);
        Assert.Equal(5, result.Series.Volume.Depth);
        Assert.Equal(24, result.Series.Volume.Width);
        Assert.Contains(result.Warnings, w => w.Contains("different size or orientation"));
        Assert.Contains(result.Warnings, w => w.Contains("positions already used"));
    }

    [Fact]
    public void Reads_enhanced_multiframe_with_per_frame_positions()
    {
        using var dir = new TempDir();
        var ds = BaseDataset(4, 4, 16, 12, signed: false);
        ds.AddOrUpdate(DicomTag.NumberOfFrames, 4);
        ds.AddOrUpdate(DicomTag.Modality, "CT");

        var measures = new DicomDataset { { DicomTag.PixelSpacing, new[] { 0.5m, 0.75m } }, { DicomTag.SliceThickness, 2m } };
        var orientation = new DicomDataset { { DicomTag.ImageOrientationPatient, new[] { 1m, 0m, 0m, 0m, 1m, 0m } } };
        var transform = new DicomDataset { { DicomTag.RescaleSlope, 1m }, { DicomTag.RescaleIntercept, -1024m } };
        ds.Add(new DicomSequence(DicomTag.SharedFunctionalGroupsSequence, new DicomDataset
        {
            new DicomSequence(DicomTag.PixelMeasuresSequence, measures),
            new DicomSequence(DicomTag.PlaneOrientationSequence, orientation),
            new DicomSequence(DicomTag.PixelValueTransformationSequence, transform),
        }));

        // Frames are stored top-down (z = 6, 4, 2, 0); frame f has value 100·f.
        var perFrame = Enumerable.Range(0, 4).Select(f => new DicomDataset
        {
            new DicomSequence(DicomTag.PlanePositionSequence,
                new DicomDataset { { DicomTag.ImagePositionPatient, new[] { 10m, 20m, (decimal)(6 - 2 * f) } } }),
        }).ToArray();
        ds.Add(new DicomSequence(DicomTag.PerFrameFunctionalGroupsSequence, perFrame));

        var pixels = DicomPixelData.Create(ds, true);
        for (int f = 0; f < 4; f++) pixels.AddFrame(Frame16(16, (ushort)(100 * f)));
        string path = System.IO.Path.Combine(dir.Path, "enhanced.dcm");
        new DicomFile(ds).Save(path);

        var volume = DicomSeriesLoader.LoadDirectory(dir.Path).Series.Volume;
        Assert.Equal(4, volume.Depth);
        Assert.Equal(300 - 1024, volume[0, 0, 0]); // lowest z is the last frame
        Assert.Equal(0 - 1024, volume[0, 0, 3]);
        Assert.Equal(new VoxelSpacing(0.75, 0.5, 2), volume.Spacing);
        Assert.Equal(new Point3(10, 20, 0), volume.Frame.Origin);
    }

    [Fact]
    public void Masks_unused_bits_and_inverts_monochrome1()
    {
        var ds = BaseDataset(2, 1, 16, 12, signed: false);
        var pixels = DicomPixelData.Create(ds, true);
        // 0xF123: the top four bits are outside BitsStored and must be ignored.
        pixels.AddFrame(new MemoryByteBuffer([0x23, 0xF1, 0xFF, 0x0F]));
        var target = new short[2];

        DicomSeriesLoader.ReadFrame(pixels, 0, target, 1, 0, monochrome1: false);
        Assert.Equal(new short[] { 0x123, 0xFFF }, target);

        DicomSeriesLoader.ReadFrame(pixels, 0, target, 1, 0, monochrome1: true);
        Assert.Equal(new short[] { 4095 - 0x123, 0 }, target);
    }

    [Fact]
    public void Sign_extends_signed_data_with_fewer_stored_bits()
    {
        var ds = BaseDataset(1, 1, 16, 12, signed: true);
        var pixels = DicomPixelData.Create(ds, true);
        pixels.AddFrame(new MemoryByteBuffer([0xFF, 0x0F])); // 12-bit two's complement for -1
        var target = new short[1];
        DicomSeriesLoader.ReadFrame(pixels, 0, target, 1, 0, monochrome1: false);
        Assert.Equal(-1, target[0]);
    }

    [Fact]
    public void Chooses_a_sensible_default_window()
    {
        var volume = new Volume(10, 10, 1, VoxelSpacing.Isotropic(1), Enumerable.Range(0, 100).Select(i => (short)(i * 10)).ToArray());
        var ct = new DicomDataset { { DicomTag.Modality, "CT" } };
        Assert.Null(DicomSeriesLoader.DefaultWindow(ct, volume, false));

        ct.Add(DicomTag.WindowCenter, "40");
        ct.Add(DicomTag.WindowWidth, "400");
        Assert.Equal(new WindowLevel(40, 400), DicomSeriesLoader.DefaultWindow(ct, volume, false));

        var mr = new DicomDataset { { DicomTag.Modality, "MR" } };
        var window = DicomSeriesLoader.DefaultWindow(mr, volume, false)!.Value;
        Assert.InRange(window.Centre, 450, 550);
        Assert.InRange(window.Width, 900, 1000);
    }

    [Fact]
    public void Colour_series_are_listed_but_not_opened()
    {
        using var dir = new TempDir();
        var ds = BaseDataset(2, 2, 8, 8, signed: false);
        ds.AddOrUpdate(DicomTag.SamplesPerPixel, (ushort)3);
        ds.AddOrUpdate(DicomTag.PhotometricInterpretation, "RGB");
        ds.AddOrUpdate(DicomTag.PlanarConfiguration, (ushort)0);
        DicomPixelData.Create(ds, true).AddFrame(new MemoryByteBuffer(new byte[12]));
        new DicomFile(ds).Save(System.IO.Path.Combine(dir.Path, "colour.dcm"));

        var scan = DicomSeriesLoader.Scan([dir.Path]);
        var only = Assert.Single(scan.Series);
        Assert.False(only.IsSupported);
        Assert.Contains("colour", only.Detail);
        Assert.Null(scan.Largest);
        Assert.Throws<NotSupportedException>(() => DicomSeriesLoader.Load(scan, only.SeriesInstanceUid));
        Assert.Throws<InvalidDataException>(() => DicomSeriesLoader.LoadDirectory(dir.Path));
    }

    private static DicomFile ToUnsignedWithIntercept(DicomFile file)
    {
        var ds = file.Dataset;
        var old = DicomPixelData.Create(ds);
        byte[] bytes = old.GetFrame(0).Data;
        var shifted = new byte[bytes.Length];
        for (int i = 0; i < bytes.Length; i += 2)
        {
            short v = BitConverter.ToInt16(bytes, i);
            BitConverter.GetBytes((ushort)(v + 1024)).CopyTo(shifted, i);
        }
        ds.AddOrUpdate(DicomTag.PixelRepresentation, (ushort)0);
        ds.AddOrUpdate(DicomTag.RescaleIntercept, "-1024");
        ds.Remove(DicomTag.PixelData);
        DicomPixelData.Create(ds, true).AddFrame(new MemoryByteBuffer(shifted));
        return new DicomFile(ds);
    }

    private static DicomDataset BaseDataset(int columns, int rows, ushort bitsAllocated, ushort bitsStored, bool signed) =>
        new(DicomTransferSyntax.ExplicitVRLittleEndian)
        {
            { DicomTag.SOPClassUID, DicomUID.EnhancedCTImageStorage },
            { DicomTag.SOPInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
            { DicomTag.SeriesInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
            { DicomTag.Modality, "OT" },
            { DicomTag.Rows, (ushort)rows },
            { DicomTag.Columns, (ushort)columns },
            { DicomTag.SamplesPerPixel, (ushort)1 },
            { DicomTag.PhotometricInterpretation, "MONOCHROME2" },
            { DicomTag.BitsAllocated, bitsAllocated },
            { DicomTag.BitsStored, bitsStored },
            { DicomTag.HighBit, (ushort)(bitsStored - 1) },
            { DicomTag.PixelRepresentation, (ushort)(signed ? 1 : 0) },
        };

    private static MemoryByteBuffer Frame16(int count, ushort value)
    {
        var bytes = new byte[count * 2];
        for (int i = 0; i < count; i++) BitConverter.GetBytes(value).CopyTo(bytes, i * 2);
        return new MemoryByteBuffer(bytes);
    }
}
