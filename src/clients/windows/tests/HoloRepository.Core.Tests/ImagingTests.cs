using HoloRepository.Core.Imaging;

namespace HoloRepository.Core.Tests;

public class VolumeTests
{
    [Fact]
    public void Constructor_rejects_bad_dimensions_and_spacing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Volume(0, 1, 1, VoxelSpacing.Isotropic(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Volume(1, 1, 1, new VoxelSpacing(1, 0, 1)));
        Assert.Throws<ArgumentException>(() => new Volume(2, 2, 2, VoxelSpacing.Isotropic(1), new short[3]));
    }

    [Fact]
    public void Indexer_reads_and_writes_x_fastest()
    {
        var v = new Volume(3, 4, 5, new VoxelSpacing(1, 2, 3));
        v[2, 1, 4] = 123;
        Assert.Equal(123, v.Voxels[(4 * 4 + 1) * 3 + 2]);
        Assert.Equal(123, v[2, 1, 4]);
        Assert.Equal(3, v.PhysicalWidth);
        Assert.Equal(8, v.PhysicalHeight);
        Assert.Equal(15, v.PhysicalDepth);
        Assert.Equal(new Point3(1.5, 4, 7.5), v.Centre);
    }

    [Fact]
    public void SampleMm_returns_outside_value_beyond_bounds()
    {
        var v = new Volume(2, 2, 2, VoxelSpacing.Isotropic(2));
        v[1, 1, 1] = 50;
        Assert.Equal(50, v.SampleMm(3, 3, 3));
        Assert.Equal(-1024, v.SampleMm(-0.1, 1, 1));
        Assert.Equal(-7, v.SampleMm(10, 1, 1, outside: -7));
    }

    [Fact]
    public void Voxel_and_mm_conversion_round_trip_and_clamp()
    {
        var v = new Volume(10, 10, 10, new VoxelSpacing(1, 2, 3));
        var p = v.VoxelToMm(4, 5, 6);
        Assert.Equal((4, 5, 6), v.MmToVoxel(p));
        Assert.Equal((9, 0, 9), v.MmToVoxel(new Point3(1000, -5, 1000)));
    }

    [Fact]
    public void Range_and_distance()
    {
        var v = new Volume(2, 1, 1, VoxelSpacing.Isotropic(1), new short[] { -5, 9 });
        Assert.Equal(((short)-5, (short)9), v.Range());
        Assert.Equal(5, new Point3(0, 0, 0).DistanceTo(new Point3(3, 4, 0)), 6);
        Assert.Contains("mm", new Point3(1, 2, 3).ToString());
        Assert.Equal(1, new VoxelSpacing(3, 1, 2).Min);
    }
}

public class WindowLevelTests
{
    [Theory]
    [InlineData(-1000, 0)]
    [InlineData(40, 128)]
    [InlineData(1000, 255)]
    public void Apply_maps_hu_to_grey(double hu, byte expected)
    {
        Assert.Equal(expected, WindowLevel.Mediastinum.Apply(hu));
    }

    [Fact]
    public void Adjust_changes_width_and_centre_with_minimum_width()
    {
        var w = new WindowLevel(40, 400).Adjust(100, -20);
        Assert.Equal(new WindowLevel(20, 500), w);
        Assert.Equal(WindowLevel.MinimumWidth, new WindowLevel(0, 10).Adjust(-50, 0).Width);
        Assert.Equal(-160, new WindowLevel(40, 400).Lower);
        Assert.Equal(240, new WindowLevel(40, 400).Upper);
        Assert.Equal("W 400 / L 40", new WindowLevel(40, 400).Describe());
    }

    [Fact]
    public void ApplyToBgra_writes_opaque_grey_pixels()
    {
        var values = new short[] { -2000, 40, 2000 };
        var buffer = new byte[12];
        WindowLevel.Mediastinum.ApplyToBgra(values, buffer);
        Assert.Equal(new byte[] { 0, 0, 0, 255, 128, 128, 128, 255, 255, 255, 255, 255 }, buffer);
        Assert.Throws<ArgumentException>(() => WindowLevel.Lung.ApplyToBgra(values, new byte[4]));
    }

    [Fact]
    public void Presets_are_named_and_distinct()
    {
        Assert.Equal(5, WindowLevel.Presets.Count);
        Assert.Equal(WindowLevel.Presets.Count, WindowLevel.Presets.Select(p => p.Value).Distinct().Count());
        Assert.Contains(WindowLevel.Presets, p => p.Name == "Brain" && p.Value == WindowLevel.Brain);
    }
}

public class PhantomTests
{
    private static readonly Volume Phantom = PhantomGenerator.CreateChest(80, 64, 60);

    [Fact]
    public void Corners_are_air_and_spine_is_bone()
    {
        Assert.True(Phantom[0, 0, 0] < -900);
        var spine = new Point3(Phantom.PhysicalWidth / 2, Phantom.PhysicalHeight / 2 + Phantom.PhysicalHeight * 0.33, Phantom.PhysicalDepth * 0.5 + 15);
        Assert.True(Phantom.SampleMm(spine.X, spine.Y, spine.Z) > 200);
    }

    [Fact]
    public void Nodule_is_soft_tissue_inside_aerated_lung()
    {
        var c = PhantomGenerator.NoduleCentre(Phantom);
        Assert.InRange(Phantom.SampleMm(c.X, c.Y, c.Z), -20, 80);
        // Lung parenchyma lateral to the nodule.
        Assert.True(Phantom.SampleMm(c.X + 20, c.Y + 15, c.Z) < -600);
    }

    [Fact]
    public void Series_metadata_is_synthetic_and_deterministic()
    {
        var a = PhantomGenerator.CreateChestSeries(40, 32, 30);
        var b = PhantomGenerator.CreateChestSeries(40, 32, 30);
        Assert.True(a.Metadata.IsSynthetic);
        Assert.Equal("CT", a.Metadata.Modality);
        Assert.Equal(a.Volume.Voxels, b.Volume.Voxels);
    }
}

public class MprSlicerTests
{
    private static Volume Ramp()
    {
        var v = new Volume(4, 3, 5, new VoxelSpacing(1, 2, 3));
        for (int z = 0; z < 5; z++)
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 4; x++)
            v[x, y, z] = (short)(x + 10 * y + 100 * z);
        return v;
    }

    [Theory]
    [InlineData(SlicePlane.Axial, 5, 4, 3)]
    [InlineData(SlicePlane.Coronal, 3, 4, 5)]
    [InlineData(SlicePlane.Sagittal, 4, 3, 5)]
    public void Slice_dimensions_match_plane(SlicePlane plane, int count, int width, int height)
    {
        var v = Ramp();
        Assert.Equal(count, MprSlicer.SliceCount(v, plane));
        Assert.Equal(count / 2, MprSlicer.MiddleSlice(v, plane));
        var s = MprSlicer.Extract(v, plane, 1);
        Assert.Equal(width, s.Width);
        Assert.Equal(height, s.Height);
        Assert.Equal(width * height, s.Pixels.Length);
    }

    [Fact]
    public void Axial_coronal_sagittal_pick_correct_voxels_with_head_up()
    {
        var v = Ramp();
        var axial = MprSlicer.Extract(v, SlicePlane.Axial, 2);
        Assert.Equal(v[3, 2, 2], axial.Pixels[2 * 4 + 3]);

        var coronal = MprSlicer.Extract(v, SlicePlane.Coronal, 1);
        Assert.Equal(v[2, 1, 4], coronal.Pixels[0 * 4 + 2]); // top row is the most cranial slice
        Assert.Equal(3, coronal.PixelHeightMm);

        var sagittal = MprSlicer.Extract(v, SlicePlane.Sagittal, 3);
        Assert.Equal(v[3, 2, 0], sagittal.Pixels[4 * 3 + 2]);
        Assert.Equal(6, sagittal.PhysicalWidth);
        Assert.Equal(15, sagittal.PhysicalHeight);
    }

    [Fact]
    public void Extract_clamps_index()
    {
        var v = Ramp();
        Assert.Equal(4, MprSlicer.Extract(v, SlicePlane.Axial, 99).Index);
        Assert.Equal(0, MprSlicer.Extract(v, SlicePlane.Axial, -3).Index);
    }

    [Theory]
    [InlineData(SlicePlane.Axial)]
    [InlineData(SlicePlane.Coronal)]
    [InlineData(SlicePlane.Sagittal)]
    public void Slice_point_conversion_round_trips(SlicePlane plane)
    {
        var v = Ramp();
        var p = MprSlicer.SliceToPoint(v, plane, 1, 1.5, 2.25);
        var (col, row, index) = MprSlicer.PointToSlice(v, plane, p);
        Assert.Equal(1.5, col, 6);
        Assert.Equal(2.25, row, 6);
        Assert.Equal(1, index);
    }

    [Fact]
    public void Crosshair_indices_locate_point_in_every_plane()
    {
        var v = Ramp();
        var p = v.VoxelToMm(2, 1, 3);
        var idx = MprSlicer.CrosshairIndices(v, p);
        Assert.Equal(3, idx[SlicePlane.Axial]);
        Assert.Equal(1, idx[SlicePlane.Coronal]);
        Assert.Equal(2, idx[SlicePlane.Sagittal]);
    }
}

public class VolumeRendererTests
{
    private static Volume Cube()
    {
        var v = new Volume(20, 20, 20, VoxelSpacing.Isotropic(1));
        Array.Fill(v.Voxels, (short)-1000);
        for (int z = 8; z < 12; z++)
        for (int y = 8; y < 12; y++)
        for (int x = 8; x < 12; x++)
            v[x, y, z] = 1000;
        return v;
    }

    [Fact]
    public void Mip_shows_dense_centre_and_dark_edges()
    {
        var img = VolumeRenderer.Render(Cube(), new VolumeRenderRequest(0, 0, 32, WindowLevel.Bone));
        Assert.Equal(32 * 32 * 4, img.Bgra.Length);
        int centre = (16 * 32 + 16) * 4;
        Assert.True(img.Bgra[centre] > 200);
        Assert.Equal(0, img.Bgra[0]);
        Assert.Equal(255, img.Bgra[3]);
    }

    [Fact]
    public void Composite_mode_renders_coloured_centre_over_background()
    {
        var img = VolumeRenderer.Render(Cube(), new VolumeRenderRequest(30, 20, 32, WindowLevel.Mediastinum, RenderMode.Composite));
        int centre = (16 * 32 + 16) * 4;
        int corner = 0;
        Assert.True(img.Bgra[centre + 2] > img.Bgra[corner + 2]);
        Assert.Equal(255, img.Bgra[corner + 3]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 0)]
    [InlineData(45, 60)]
    public void Project_maps_volume_centre_to_image_centre(double yaw, double pitch)
    {
        var v = Cube();
        var req = new VolumeRenderRequest(yaw, pitch, 100, WindowLevel.Bone, Zoom: 1.5);
        var (x, y) = VolumeRenderer.Project(v, req, v.Centre);
        Assert.Equal(50, x, 6);
        Assert.Equal(50, y, 6);
    }

    [Fact]
    public void Project_puts_head_at_top_in_anterior_view()
    {
        var v = Cube();
        var req = new VolumeRenderRequest(0, 0, 100, WindowLevel.Bone);
        var (_, yHead) = VolumeRenderer.Project(v, req, new Point3(10, 10, 19));
        var (_, yFeet) = VolumeRenderer.Project(v, req, new Point3(10, 10, 1));
        Assert.True(yHead < yFeet);
    }

    [Fact]
    public void Render_validates_size_and_honours_cancellation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VolumeRenderer.Render(Cube(), new VolumeRenderRequest(0, 0, 0, WindowLevel.Bone)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            VolumeRenderer.Render(Cube(), new VolumeRenderRequest(0, 0, 16, WindowLevel.Bone), cts.Token));
    }
}
