using System.Net;
using System.Text;
using HoloRepository.Core.Imaging;
using HoloRepository.Core.Segmentation;

namespace HoloRepository.Core.Tests;

public class PatientFrameTests
{
    [Fact]
    public void Identity_frame_round_trips_and_converts_ras()
    {
        var v = new Volume(4, 4, 4, new VoxelSpacing(1, 2, 3));
        var p = v.ToPatient(v.VoxelToMm(1, 2, 3));
        Assert.Equal(new Point3(1, 4, 9), p);
        var back = v.FromPatient(p);
        Assert.Equal(v.VoxelToMm(1, 2, 3).X, back.X, 6);
        Assert.Equal(new Point3(-1, -2, 3), PatientFrame.RasToLps(new Point3(1, 2, 3)));
        Assert.Equal(new Point3(1, 2, 3), PatientFrame.LpsToRas(PatientFrame.RasToLps(new Point3(1, 2, 3))));
    }

    [Fact]
    public void Dicom_round_trip_preserves_frame()
    {
        using var dir = new TempDir();
        var vol = new Volume(8, 8, 4, new VoxelSpacing(1, 1, 2))
        {
            Frame = new PatientFrame(new Point3(-100, -50, 20), new Point3(1, 0, 0), new Point3(0, 1, 0), new Point3(0, 0, 1)),
        };
        DicomSeriesWriter.Write(new LoadedSeries(new StudyMetadata(), vol), dir.Path);
        var loaded = DicomSeriesLoader.LoadDirectory(dir.Path).Series;
        Assert.Equal(new Point3(-100, -50, 20), loaded.Volume.Frame.Origin);
        Assert.Equal(dir.Path, loaded.Metadata.SourcePath);
    }
}

public class NiftiTests
{
    private static double[] LpsAlignedAffine(VoxelSpacing s) => [-s.X, 0, 0, 0, 0, -s.Y, 0, 0, 0, 0, s.Z, 0];

    [Fact]
    public void Writes_and_reads_header_data_and_gzip()
    {
        var data = Enumerable.Range(0, 24).Select(i => (float)i).ToArray();
        var ms = new MemoryStream();
        NiftiImage.Write(ms, 2, 3, 4, data, [2, 0, 0, 10, 0, 3, 0, 20, 0, 0, 4, 30]);
        var img = NiftiImage.Read(new MemoryStream(ms.ToArray()));
        Assert.Equal((2, 3, 4), (img.Nx, img.Ny, img.Nz));
        Assert.Equal(23, img[1, 2, 3]);
        Assert.Equal(new Point3(12, 23, 34), img.VoxelToRas(1, 1, 1));
        var (i, j, k) = img.RasToVoxel(new Point3(12, 23, 34));
        Assert.Equal(1, i, 6);
        Assert.Equal(1, j, 6);
        Assert.Equal(1, k, 6);
        Assert.Equal((2.0, 3.0, 4.0), img.PixelDimensions);

        var gz = new MemoryStream();
        using (var g = new System.IO.Compression.GZipStream(gz, System.IO.Compression.CompressionLevel.Fastest, true))
            g.Write(ms.ToArray());
        Assert.Equal(23, NiftiImage.Read(new MemoryStream(gz.ToArray()))[1, 2, 3]);
    }

    [Fact]
    public void Rejects_invalid_files()
    {
        Assert.Throws<InvalidDataException>(() => NiftiImage.Read(new MemoryStream(new byte[10])));
        Assert.Throws<InvalidDataException>(() => NiftiImage.Read(new MemoryStream(new byte[400])));
    }

    [Fact]
    public void Label_map_resamples_onto_matching_volume()
    {
        var vol = new Volume(10, 8, 6, new VoxelSpacing(2, 2, 3));
        var labels = new float[10 * 8 * 6];
        labels[(3 * 8 + 4) * 10 + 5] = 7;
        var ms = new MemoryStream();
        NiftiImage.Write(ms, 10, 8, 6, labels, LpsAlignedAffine(vol.Spacing), asLabels: true);
        var img = NiftiImage.Read(new MemoryStream(ms.ToArray()));
        var resampled = img.ResampleLabelsTo(vol);
        Assert.Equal(7, resampled[vol.IndexOf(5, 4, 3)]);
        Assert.Equal(1, resampled.Count(b => b != 0));
    }

    [Fact]
    public void ToSeries_reorients_ras_data_to_lps_display_order()
    {
        // i increases to the patient's right (RAS +x), so the viewer must flip it.
        var data = new float[3 * 2 * 2];
        data[(0 * 2 + 0) * 3 + 2] = 99; // i = 2 (most rightward), j = 0, k = 0
        var ms = new MemoryStream();
        NiftiImage.Write(ms, 3, 2, 2, data, [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0]);
        var series = NiftiImage.Read(new MemoryStream(ms.ToArray())).ToSeries(new StudyMetadata());
        var v = series.Volume;
        // RAS (2,0,0) → LPS (-2,0,0): x is most negative (patient right) → output x = 0; j = 0 is most posterior? no:
        // RAS +y is anterior, so LPS y = -j; j = 0 is the most posterior → output y = last row.
        Assert.Equal(99, v[0, 1, 0]);
        Assert.Equal(new Point3(1, 0, 0), v.Frame.XDirection);
        var back = PatientFrame.LpsToRas(v.ToPatient(v.VoxelToMm(0, 1, 0)));
        Assert.Equal(new Point3(2, 0, 0), back);
    }

    [Fact]
    public void Qform_and_fallback_affines_are_supported()
    {
        var ms = new MemoryStream();
        NiftiImage.Write(ms, 2, 2, 2, new float[8], [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0]);
        var bytes = ms.ToArray();
        // Switch to qform with identity quaternion and offset (5, 6, 7).
        BitConverter.GetBytes((short)0).CopyTo(bytes, 254);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 252);
        BitConverter.GetBytes(5f).CopyTo(bytes, 268);
        BitConverter.GetBytes(6f).CopyTo(bytes, 272);
        BitConverter.GetBytes(7f).CopyTo(bytes, 276);
        Assert.Equal(new Point3(6, 7, 8), NiftiImage.Read(new MemoryStream(bytes)).VoxelToRas(1, 1, 1));

        BitConverter.GetBytes((short)0).CopyTo(bytes, 252);
        Assert.Equal(new Point3(1, 1, 1), NiftiImage.Read(new MemoryStream(bytes)).VoxelToRas(1, 1, 1));
    }
}

public class MeshTests
{
    private static SurfaceMesh Cube(float c, float h) =>
        new(
        [
            c - h, c - h, c - h, c + h, c - h, c - h, c + h, c + h, c - h, c - h, c + h, c - h,
            c - h, c - h, c + h, c + h, c - h, c + h, c + h, c + h, c + h, c - h, c + h, c + h,
        ],
        [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 2, 6, 3, 6, 7, 0, 3, 7, 0, 7, 4, 1, 2, 6, 1, 6, 5]);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mz3_round_trips(bool gzip)
    {
        var mesh = Cube(5, 1);
        var ms = new MemoryStream();
        Mz3.Write(ms, mesh, gzip);
        var read = Mz3.Read(new MemoryStream(ms.ToArray()));
        Assert.Equal(mesh.Vertices, read.Vertices);
        Assert.Equal(mesh.Indices, read.Indices);
        Assert.Equal(12, read.TriangleCount);
        Assert.Throws<InvalidDataException>(() => Mz3.Read(new MemoryStream(new byte[20])));
    }

    [Fact]
    public void Mesh_validates_and_transforms()
    {
        Assert.Throws<ArgumentException>(() => new SurfaceMesh(new float[4], []));
        Assert.Throws<InvalidDataException>(() => new SurfaceMesh(new float[3], [0, 1, 2]));
        var moved = Cube(0, 1).Transform(p => new Point3(p.X + 10, p.Y, p.Z));
        var (min, max) = moved.Bounds();
        Assert.Equal(9, min.X, 5);
        Assert.Equal(11, max.X, 5);
    }

    [Fact]
    public void Surface_extractor_wraps_a_labelled_sphere()
    {
        var v = new Volume(20, 20, 20, VoxelSpacing.Isotropic(1));
        var labels = new byte[8000];
        for (int z = 0; z < 20; z++)
        for (int y = 0; y < 20; y++)
        for (int x = 0; x < 20; x++)
            if ((x - 10) * (x - 10) + (y - 10) * (y - 10) + (z - 10) * (z - 10) <= 25) labels[v.IndexOf(x, y, z)] = 3;

        var mesh = SurfaceExtractor.Extract(v, labels, 3, step: 1)!;
        Assert.True(mesh.TriangleCount > 100);
        var (min, max) = mesh.Bounds();
        Assert.InRange((max.X - min.X), 8, 12);
        Assert.Null(SurfaceExtractor.Extract(v, labels, 9));
    }

    [Fact]
    public void Mesh_renderer_draws_structures_over_the_volume()
    {
        var v = new Volume(20, 20, 20, VoxelSpacing.Isotropic(1));
        Array.Fill(v.Voxels, (short)-1000);
        var request = new VolumeRenderRequest(30, 20, 40, WindowLevel.Bone);
        var image = VolumeRenderer.Render(v, request);
        int centre = (20 * 40 + 20) * 4;
        Assert.Equal(0, image.Bgra[centre + 2]);

        var layers = new[]
        {
            new MeshLayer(Cube(10, 6), new Rgba(0, 0, 255, 0.3)),
            new MeshLayer(Cube(10, 2), new Rgba(255, 0, 0, 1)),
        };
        MeshRenderer.Composite(v, request, layers, image.Bgra);
        Assert.True(image.Bgra[centre + 2] > 100, "inner opaque red structure should show through the translucent outer one");
        Assert.True(image.Bgra[centre] > 0, "outer translucent blue should tint the result");
        Assert.Equal(0, image.Bgra[2]);
        Assert.Throws<ArgumentException>(() => MeshRenderer.Composite(v, request, layers, new byte[4]));
    }

    [Fact]
    public void View_transform_matches_marker_projection()
    {
        var v = new Volume(20, 20, 20, VoxelSpacing.Isotropic(1));
        var request = new VolumeRenderRequest(40, -10, 64, WindowLevel.Bone);
        var p = new Point3(3, 15, 7);
        var (x, y) = VolumeRenderer.Project(v, request, p);
        var t = VolumeRenderer.GetView(v, request).Project(p.X, p.Y, p.Z);
        Assert.Equal(x, t.X, 6);
        Assert.Equal(y, t.Y, 6);
        Assert.Equal(64, VolumeRenderer.GetView(v, request).Size);
    }
}

public class SegmentationModelTests
{
    [Fact]
    public void Models_match_server_contract_ids()
    {
        Assert.Equal(["nv-segment-ct", "ts-specialist-kidney", "ts-specialist-liver", "ts-specialist-head-n-neck"],
            SegmentationModelInfo.All.Select(m => m.Id));
        Assert.Equal("Liver", SegmentationModelInfo.FromId("ts-specialist-liver").DisplayName);
        Assert.Equal("unknown", SegmentationModelInfo.FromId("unknown").DisplayName);
    }

    [Fact]
    public void Palette_makes_lungs_translucent_and_lesions_opaque()
    {
        Assert.True(LabelPalette.ColourFor(1, "Lungs").Opacity < 0.5);
        Assert.Equal(1, LabelPalette.ColourFor(4, "Lesion candidate").Opacity);
        Assert.Equal("#FFC828", LabelPalette.ColourFor(4, "nodule").Hex);
        Assert.Equal(0.8, LabelPalette.ColourFor(13, "Structure 13").Opacity);
    }

    [Fact]
    public void Describe_labels_reports_volumes_and_meshes()
    {
        var v = new Volume(10, 10, 10, VoxelSpacing.Isotropic(2));
        var labels = new byte[1000];
        for (int z = 2; z < 8; z++)
        for (int y = 2; y < 8; y++)
        for (int x = 2; x < 8; x++)
            labels[v.IndexOf(x, y, z)] = 5;
        var s = Assert.Single(SegmentationResult.DescribeLabels(v, labels, meshStep: 1));
        Assert.Equal(216, s.VoxelCount);
        Assert.Equal(1.728, s.VolumeMl, 3);
        Assert.Equal("1.7 ml", s.VolumeText);
        Assert.NotNull(s.Mesh);
        Assert.Equal("Structure 5", s.Name);
    }
}

public class DemoSegmentationTests
{
    [Fact]
    public async Task Finds_lungs_bones_vessels_and_the_phantom_nodule()
    {
        var series = PhantomGenerator.CreateChestSeries();
        var progress = new List<SegmentationStage>();
        var result = await new DemoSegmentationService().SegmentAsync(
            new SegmentationRequest("s1", series, SegmentationModelInfo.WholeBodyCt),
            new SyncProgress<SegmentationProgress>(p => progress.Add(p.Stage)));

        Assert.Equal("Offline demo", result.Source);
        Assert.Contains("not a trained model", result.Note);
        var names = result.Structures.Select(s => s.Name).ToList();
        Assert.Contains("Lungs and airways", names);
        Assert.Contains("Bones", names);
        Assert.Contains("Contrast-filled vessels", names);
        Assert.Contains("Lesion candidate", names);

        // The lesion should sit on the synthetic nodule and be roughly its size (18 mm sphere ≈ 3 ml).
        var v = series.Volume;
        var c = PhantomGenerator.NoduleCentre(v);
        var (x, y, z) = v.MmToVoxel(c);
        Assert.Equal(DemoSegmentationService.Lesion, result.Labels![v.IndexOf(x, y, z)]);
        var lesion = result.Structures.Single(s => s.Label == DemoSegmentationService.Lesion);
        Assert.InRange(lesion.VolumeMl, 1.5, 6);
        Assert.Equal(4, result.MeshLayers.Count);
        Assert.Equal(new Rgba(255, 200, 40, 1), result.ColourOf(DemoSegmentationService.Lesion));
        Assert.Contains(SegmentationStage.Done, progress);
    }

    [Fact]
    public async Task Specialist_models_explain_they_need_the_server()
    {
        var series = PhantomGenerator.CreateChestSeries(40, 32, 30);
        var result = await new DemoSegmentationService().SegmentAsync(
            new SegmentationRequest("s1", series, SegmentationModelInfo.Liver));
        Assert.Empty(result.Structures);
        Assert.Contains("HoloRepository server", result.Note);
    }

    [Fact]
    public void Morphology_erodes_and_dilates()
    {
        var v = new Volume(9, 9, 9, VoxelSpacing.Isotropic(1));
        var mask = new bool[729];
        mask[v.IndexOf(4, 4, 4)] = true;
        var dilated = DemoSegmentationService.Dilate(v, mask, 1.0);
        Assert.Equal(7, dilated.Count(b => b)); // centre plus six face neighbours
        var eroded = DemoSegmentationService.Erode(v, dilated, 1.0);
        Assert.Equal(1, eroded.Count(b => b));
        Assert.Equal(27, DemoSegmentationService.Dilate(v, mask, 1.8).Count(b => b));
    }
}

/// <summary>An in-memory stand-in for the HoloRepository FastAPI server, following its API contract.</summary>
internal sealed class FakeServer : HttpMessageHandler
{
    public readonly List<string> Calls = new();
    public string? LastModel;
    public string? LastFileType;
    public string? AuthHeader;
    public int StatusPolls;
    public bool FailJob;
    public byte[] Nifti = [];
    public byte[] Mesh = [];

    private bool _done;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath.TrimStart('/');
        Calls.Add($"{request.Method} {path}");
        if (path is not ("token" or "signup")) AuthHeader = request.Headers.Authorization?.ToString();

        switch (path)
        {
            case "signup":
                return Json(HttpStatusCode.Created, """{"id":"u1","username":"doctor"}""");
            case "token":
            {
                string form = await request.Content!.ReadAsStringAsync(cancellationToken);
                return form.Contains("password=wrong")
                    ? Json(HttpStatusCode.Unauthorized, """{"detail":"Incorrect username or password"}""")
                    : Json(HttpStatusCode.OK, """{"access_token":"abc123","token_type":"bearer"}""");
            }
            case "me":
                return Json(HttpStatusCode.OK, """{"id":"u1","username":"doctor"}""");
            case "new-job":
            {
                var multipart = (MultipartFormDataContent)request.Content!;
                foreach (var part in multipart)
                {
                    string? name = part.Headers.ContentDisposition?.Name?.Trim('"');
                    if (name == "model") LastModel = await part.ReadAsStringAsync(cancellationToken);
                    if (name == "file_type") LastFileType = await part.ReadAsStringAsync(cancellationToken);
                }
                return Json(HttpStatusCode.OK, """{"id":"job-1"}""");
            }
            case "job-status":
                StatusPolls++;
                if (FailJob) return Json(HttpStatusCode.OK, """{"status":"error","msg":"model crashed"}""");
                if (StatusPolls == 1) return Json(HttpStatusCode.OK, """{"status":"in_queue"}""");
                if (StatusPolls == 2) return Json(HttpStatusCode.OK, """{"status":"in_progress","msg":"Segmenting"}""");
                _done = true;
                return Json(HttpStatusCode.OK, """{"status":"done"}""");
            case "records":
                return Json(HttpStatusCode.OK, _done
                    ? """[{"id":"old","files":[]},{"id":"rec-1","files":[{"id":"f-dcm","file_type":"dicom"},{"id":"f-seg","file_type":"nifti_segmented"},{"id":"f-mesh","file_type":"mz3"}]}]"""
                    : """[{"id":"old","files":[]}]""");
            case "files/f-seg":
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Nifti) };
            case "files/f-mesh":
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Mesh) };
            default:
                return Json(HttpStatusCode.NotFound, """{"detail":"Not Found"}""");
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

public class ApiClientTests
{
    private static HoloRepositoryApiClient Client(FakeServer server) => new(new Uri("http://server.test/"), server);

    [Fact]
    public async Task Sign_in_stores_token_and_sends_it()
    {
        var server = new FakeServer();
        using var api = Client(server);
        Assert.False(api.IsSignedIn);
        await Assert.ThrowsAsync<HoloRepositoryApiException>(() => api.GetRecordsAsync());

        var user = await api.SignInAsync("doctor", "password1");
        Assert.Equal("doctor", user.Username);
        Assert.True(api.IsSignedIn);
        Assert.Equal("Bearer abc123", server.AuthHeader);
        Assert.Equal("doctor", (await api.SignUpAsync("doctor", "password1")).Username);
        Assert.Equal(new Uri("http://server.test/"), api.BaseAddress);

        api.SignOut();
        Assert.False(api.IsSignedIn);
    }

    [Fact]
    public async Task Errors_use_server_detail()
    {
        using var api = Client(new FakeServer());
        var ex = await Assert.ThrowsAsync<HoloRepositoryApiException>(() => api.SignInAsync("doctor", "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.Status);
        Assert.Equal("Incorrect username or password", ex.Message);

        await api.SignInAsync("doctor", "password1");
        var missing = await Assert.ThrowsAsync<HoloRepositoryApiException>(() => api.DownloadFileAsync("nope"));
        Assert.Equal("Not Found", missing.Message);
    }

    [Theory]
    [InlineData("""{"detail":[{"msg":"field required"},{"msg":"too short"}]}""", HttpStatusCode.UnprocessableEntity, "field required; too short")]
    [InlineData("not json", HttpStatusCode.Unauthorized, "Your session has expired. Please sign in again.")]
    [InlineData("", HttpStatusCode.NotFound, "The server could not find that item.")]
    [InlineData("{}", HttpStatusCode.InternalServerError, "The server returned an error (500).")]
    public void Describes_fastapi_errors(string body, HttpStatusCode status, string expected)
    {
        Assert.Equal(expected, HoloRepositoryApiClient.DescribeError(status, body));
    }

    [Fact]
    public void Enums_use_contract_wire_values()
    {
        Assert.Equal("nifti_segmented", HoloRepositoryApiClient.ToWire(ApiFileType.NiftiSegmented));
        Assert.Equal("mz3", HoloRepositoryApiClient.ToWire(ApiFileType.Mz3));
        Assert.Equal("dicom", HoloRepositoryApiClient.ToWire(ApiFileType.Dicom));
    }
}

public class RemoteSegmentationTests
{
    private static (FakeServer Server, LoadedSeries Series) Setup()
    {
        var series = PhantomGenerator.CreateChestSeries(24, 20, 16);
        var v = series.Volume;
        var labels = new float[v.Voxels.Length];
        for (int z = 4; z < 10; z++)
        for (int y = 4; y < 10; y++)
        for (int x = 4; x < 10; x++)
            labels[v.IndexOf(x, y, z)] = 2;
        var nifti = new MemoryStream();
        NiftiImage.Write(nifti, v.Width, v.Height, v.Depth, labels,
            [-v.Spacing.X, 0, 0, 0, 0, -v.Spacing.Y, 0, 0, 0, 0, v.Spacing.Z, 0], asLabels: true);

        // A small mesh in RAS millimetres around the labelled cube.
        var mesh = new SurfaceMesh([-60, -60, 40, -70, -60, 40, -60, -70, 50], [0, 1, 2]);
        var mz3 = new MemoryStream();
        Mz3.Write(mz3, mesh);

        return (new FakeServer { Nifti = nifti.ToArray(), Mesh = mz3.ToArray() }, series);
    }

    [Fact]
    public async Task Uploads_polls_and_aligns_results_with_the_series()
    {
        var (server, series) = Setup();
        using var api = new HoloRepositoryApiClient(new Uri("http://server.test/"), server);
        await api.SignInAsync("doctor", "password1");
        var service = new RemoteSegmentationService(api, (_, _) => Task.CompletedTask);
        var stages = new List<SegmentationStage>();

        var result = await service.SegmentAsync(new SegmentationRequest("s1", series, SegmentationModelInfo.Liver),
            new SyncProgress<SegmentationProgress>(p => stages.Add(p.Stage)));

        Assert.Equal("ts-specialist-liver", server.LastModel);
        Assert.Equal("dicom", server.LastFileType);
        Assert.Equal(3, server.StatusPolls);
        Assert.Equal("HoloRepository server", result.Source);
        var v = series.Volume;
        Assert.Equal(2, result.Labels![v.IndexOf(6, 6, 6)]);
        var structure = Assert.Single(result.Structures);
        Assert.Equal(216, structure.VoxelCount);
        // Single structure: the server's own mesh is used, converted from RAS to volume space.
        Assert.Equal(1, structure.Mesh!.TriangleCount);
        var p = structure.Mesh.Vertex(0);
        var expected = v.FromPatient(new Point3(60, 60, 40));
        Assert.Equal(expected.X, p.X, 3);
        Assert.Equal(expected.Y, p.Y, 3);
        Assert.Equal(expected.Z, p.Z, 3);
        Assert.Equal([SegmentationStage.Preparing, SegmentationStage.Uploading, SegmentationStage.Queued, SegmentationStage.Processing,
                      SegmentationStage.Downloading, SegmentationStage.Building, SegmentationStage.Done], stages);
    }

    [Fact]
    public async Task Reports_server_job_errors()
    {
        var (server, series) = Setup();
        server.FailJob = true;
        using var api = new HoloRepositoryApiClient(new Uri("http://server.test/"), server);
        await api.SignInAsync("doctor", "password1");
        var service = new RemoteSegmentationService(api, (_, _) => Task.CompletedTask);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SegmentAsync(new SegmentationRequest("s1", series, SegmentationModelInfo.WholeBodyCt)));
        Assert.Contains("model crashed", ex.Message);
    }

    [Fact]
    public void Builds_structures_from_mesh_only_results()
    {
        var v = new Volume(4, 4, 4, VoxelSpacing.Isotropic(1));
        var mesh = new SurfaceMesh([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 1, 2]);
        var only = Assert.Single(RemoteSegmentationService.BuildStructures(v, null, mesh));
        Assert.Same(mesh, only.Mesh);
        Assert.Empty(RemoteSegmentationService.BuildStructures(v, null, null));
    }

    [Fact]
    public void Zips_existing_dicom_folder_or_writes_one()
    {
        using var work = new TempDir();
        var series = PhantomGenerator.CreateChestSeries(8, 8, 3);
        string zip = RemoteSegmentationService.PrepareZip(series, work.Path, CancellationToken.None);
        using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
            Assert.Equal(3, archive.Entries.Count);

        using var source = new TempDir();
        File.WriteAllText(System.IO.Path.Combine(source.Path, "a.dcm"), "x");
        using var work2 = new TempDir();
        string zip2 = RemoteSegmentationService.PrepareZip(series with { Metadata = series.Metadata with { SourcePath = source.Path } },
            work2.Path, CancellationToken.None);
        using var archive2 = System.IO.Compression.ZipFile.OpenRead(zip2);
        Assert.Equal("a.dcm", Assert.Single(archive2.Entries).Name);
    }
}

internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
