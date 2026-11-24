using System.Text.Json;
using HoloRepository.Core.Imaging;
using HoloRepository.Core.Segmentation;

namespace HoloRepository.Core.Tests;

public class NiiVueSceneTests
{
    private static Volume Oblique()
    {
        // A non-trivial frame: shifted origin and patient axes not aligned with LPS.
        var v = new Volume(12, 10, 8, new VoxelSpacing(0.8, 0.9, 2.5))
        {
            Frame = new PatientFrame(new Point3(-120, 35, 410), new Point3(0, 1, 0), new Point3(1, 0, 0), new Point3(0, 0, -1)),
        };
        for (int i = 0; i < v.Voxels.Length; i++) v.Voxels[i] = (short)(i % 2000 - 1000);
        return v;
    }

    [Fact]
    public void Volume_round_trips_with_the_same_patient_position()
    {
        using var dir = new TempDir();
        var v = Oblique();
        var scene = NiiVueSceneWriter.Write(dir.Path, v, WindowLevel.Lung);

        var nifti = NiftiImage.Load(System.IO.Path.Combine(dir.Path, scene.VolumeUrl));
        Assert.Equal((12, 10, 8), (nifti.Nx, nifti.Ny, nifti.Nz));
        Assert.Equal(v[3, 4, 5], (short)nifti[3, 4, 5]);

        // Every voxel lands at the same RAS position as the original volume says it should.
        foreach (var (i, j, k) in new[] { (0, 0, 0), (11, 9, 7), (3, 4, 5) })
        {
            var expected = PatientFrame.LpsToRas(v.ToPatient(v.VoxelToMm(i, j, k)));
            var actual = nifti.VoxelToRas(i, j, k);
            Assert.Equal(expected.X, actual.X, 3);
            Assert.Equal(expected.Y, actual.Y, 3);
            Assert.Equal(expected.Z, actual.Z, 3);
        }

        // Re-importing gives a volume whose voxels map to the same patient positions.
        var back = nifti.ToSeries(new StudyMetadata()).Volume;
        var p = v.ToPatient(v.VoxelToMm(2, 3, 4));
        var q = back.FromPatient(p);
        var (x, y, z) = back.MmToVoxel(q);
        Assert.Equal(v[2, 3, 4], back[x, y, z]);

        Assert.Equal(WindowLevel.Lung.Lower, scene.CalMin);
        Assert.Equal(WindowLevel.Lung.Upper, scene.CalMax);
        Assert.Null(scene.LabelsUrl);
        Assert.Empty(scene.Meshes);
    }

    [Fact]
    public async Task Segmentation_exports_labels_colours_and_meshes_in_ras()
    {
        using var dir = new TempDir();
        var series = PhantomGenerator.CreateChestSeries(80, 64, 60);
        var seg = await new DemoSegmentationService().SegmentAsync(new SegmentationRequest("s", series, SegmentationModelInfo.WholeBodyCt));
        var bones = seg.Structures.Single(s => s.Name == "Bones");

        var scene = NiiVueSceneWriter.Write(dir.Path, series.Volume, WindowLevel.Bone, seg, new HashSet<byte> { bones.Label });

        // Hidden structures are left out of both the overlay and the meshes.
        Assert.DoesNotContain(scene.Meshes, m => m.Label == bones.Label);
        Assert.Equal(seg.Structures.Count(s => s.Mesh is not null) - 1, scene.Meshes.Count);
        var labels = NiftiImage.Load(System.IO.Path.Combine(dir.Path, scene.LabelsUrl!)).ResampleLabelsTo(series.Volume);
        Assert.DoesNotContain(labels, l => l == bones.Label);
        Assert.Contains(labels, l => l == DemoSegmentationService.Lungs);

        // Colour table: background transparent, each label gets its structure colour.
        var c = scene.LabelColours!;
        Assert.Equal(0, c.A[0]);
        var lungs = seg.Structures.Single(s => s.Label == DemoSegmentationService.Lungs);
        Assert.Equal(lungs.Colour.R, c.R[lungs.Label]);
        Assert.Equal(lungs.Name, c.Labels[lungs.Label]);
        Assert.Equal(c.R.Length, c.I.Length);

        // Mesh vertices are written in RAS millimetres.
        var lesion = scene.Meshes.Single(m => m.Label == DemoSegmentationService.Lesion);
        var mesh = Mz3.Load(System.IO.Path.Combine(dir.Path, lesion.Url));
        var original = seg.Structures.Single(s => s.Label == DemoSegmentationService.Lesion).Mesh!;
        var expected = PatientFrame.LpsToRas(series.Volume.ToPatient(original.Vertex(0)));
        Assert.Equal(expected.X, mesh.Vertex(0).X, 3);
        Assert.Equal(expected.Z, mesh.Vertex(0).Z, 3);
        Assert.Equal(255, lesion.Rgba255[3]);
    }

    [Fact]
    public void Scene_serialises_to_camel_case_json()
    {
        var scene = new NiiVueSceneDescription("volume.nii", -100, 300, null, null, [new NiiVueMesh("mesh-1.mz3", "Lungs", [1, 2, 3, 77], 1)]);
        using var doc = JsonDocument.Parse(scene.ToJson());
        Assert.Equal("volume.nii", doc.RootElement.GetProperty("volumeUrl").GetString());
        Assert.Equal(300, doc.RootElement.GetProperty("calMax").GetDouble());
        Assert.Equal(77, doc.RootElement.GetProperty("meshes")[0].GetProperty("rgba255")[3].GetInt32());
    }
}
