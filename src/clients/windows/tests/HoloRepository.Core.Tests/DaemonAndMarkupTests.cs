using System.Runtime.InteropServices;
using HoloRepository.Core.Daemon;
using HoloRepository.Core.Imaging;
using HoloRepository.Core.Markup;

namespace HoloRepository.Core.Tests;

public class HardwareProbeTests
{
    [Fact]
    public void Detects_vendor_runtimes_from_system_directory()
    {
        var present = new HashSet<string> { Path.Combine("C:\\sys", "nvcuda.dll") };
        var probe = new HardwareProbe(present.Contains, Architecture.X64, "C:\\sys");
        var caps = probe.Probe(ComputeBackend.Cpu);

        Assert.True(caps.Single(c => c.Backend == ComputeBackend.Cpu).Active);
        Assert.True(caps.Single(c => c.Backend == ComputeBackend.NvidiaCuda).Detected);
        Assert.False(caps.Single(c => c.Backend == ComputeBackend.IntelArc).Detected);
        Assert.False(caps.Single(c => c.Backend == ComputeBackend.RtxSparkArm).Detected);
    }

    [Fact]
    public void Arm64_and_intel_runtime_are_reported()
    {
        var probe = new HardwareProbe(p => p.EndsWith("ze_loader.dll"), Architecture.Arm64, "C:\\sys");
        var caps = probe.Probe(ComputeBackend.Cpu);
        Assert.True(caps.Single(c => c.Backend == ComputeBackend.IntelArc).Detected);
        Assert.True(caps.Single(c => c.Backend == ComputeBackend.RtxSparkArm).Detected);
        Assert.NotNull(new HardwareProbe().Probe(ComputeBackend.Cpu));
    }
}

public class InProcessDaemonTests
{
    [Fact]
    public async Task Loads_sample_serves_slices_and_renders()
    {
        var daemon = new InProcessDaemonClient(new HardwareProbe(_ => false, Architecture.X64, "C:\\sys"));
        var info = await daemon.LoadSampleAsync();

        Assert.Single(daemon.LoadedSeries);
        Assert.True(info.Metadata.IsSynthetic);
        Assert.Contains("×", info.Dimensions);
        Assert.Contains("mm", info.SpacingText);

        var status = await daemon.GetStatusAsync();
        Assert.True(status.Online);
        Assert.True(status.CloudEgressDisabled);
        Assert.Equal(1, status.LoadedSeriesCount);
        Assert.Equal(4, status.Backends.Count);

        var slice = await daemon.GetSliceAsync(info.Id, SlicePlane.Coronal, 10);
        Assert.Equal(info.Width, slice.Width);
        Assert.Equal(info.Depth, slice.Height);

        var img = await daemon.RenderVolumeAsync(info.Id, new VolumeRenderRequest(10, 5, 24, WindowLevel.Bone));
        Assert.Equal(24, img.Width);
        Assert.Same(daemon.GetGeometry(info.Id), daemon.GetGeometry(info.Id));
        Assert.Equal("CT", daemon.GetMetadata(info.Id).Modality);
        Assert.Equal(ComputeBackend.Cpu, daemon.PreferredBackend);
    }

    [Fact]
    public async Task Exports_and_reimports_dicom()
    {
        using var dir = new TempDir();
        var daemon = new InProcessDaemonClient();
        var sample = await daemon.LoadSampleAsync();
        await daemon.ExportSeriesAsDicomAsync(sample.Id, dir.Path);
        var loaded = await daemon.LoadDicomDirectoryAsync(dir.Path);
        Assert.Equal(sample.Depth, loaded.Depth);
        Assert.Equal(2, daemon.LoadedSeries.Count);
        Assert.Equal("DICOM folder", loaded.Metadata.Source);
    }

    [Fact]
    public async Task Unknown_series_throws()
    {
        var daemon = new InProcessDaemonClient();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => daemon.GetSliceAsync("nope", SlicePlane.Axial, 0));
    }
}

public class AnnotationStoreTests
{
    private static Annotation Pin(string series = "s1") =>
        new() { SeriesId = series, Kind = AnnotationKind.Pin, Start = new Point3(1, 2, 3), Label = "Nodule" };

    [Fact]
    public void Add_remove_clear_raise_events_and_deduplicate()
    {
        var store = new AnnotationStore();
        var events = new List<AnnotationChange>();
        store.Changed += (_, e) => events.Add(e.Change);

        var a = Pin();
        Assert.True(store.Add(a));
        Assert.False(store.Add(a));
        store.Add(Pin("s2"));
        Assert.Single(store.ForSeries("s1"));
        Assert.Equal(2, store.NextNumber(AnnotationKind.Pin) - 1);
        Assert.True(store.Remove(a.Id));
        Assert.False(store.Remove(a.Id));
        store.Clear();
        Assert.Empty(store.All);
        Assert.Equal([AnnotationChange.Added, AnnotationChange.Added, AnnotationChange.Removed, AnnotationChange.Cleared], events);
    }

    [Fact]
    public void Caliper_reports_length_and_summary()
    {
        var c = new Annotation
        {
            SeriesId = "s", Kind = AnnotationKind.Caliper, Start = new Point3(0, 0, 0), End = new Point3(6, 8, 0), Label = "Lesion",
        };
        Assert.Equal(10, c.LengthMm);
        Assert.Equal("Lesion: 10.0 mm", c.Summary);
        Assert.Null(Pin().LengthMm);
        Assert.Equal("Nodule", Pin().Summary);
        Assert.Equal("Callout", new Annotation { SeriesId = "s", Kind = AnnotationKind.Callout, Start = default }.DisplayLabel);
    }
}

public class CollaborationHubTests
{
    private static LoopbackCollaborationHub Hub() =>
        new(TimeSpan.FromMilliseconds(20), (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Join_adds_local_and_simulated_participants()
    {
        var hub = Hub();
        int changes = 0;
        hub.ParticipantsChanged += (_, _) => changes++;
        await hub.JoinAsync("MDT-1", "Dr Test User");

        Assert.True(hub.IsConnected);
        Assert.Equal("MDT-1", hub.SessionId);
        Assert.Equal(4, hub.Participants.Count);
        Assert.Equal("DT", hub.Participants[0].Initials);

        await hub.LeaveAsync();
        Assert.False(hub.IsConnected);
        Assert.Empty(hub.Participants);
        Assert.Equal(2, changes);
        await Assert.ThrowsAsync<ArgumentException>(() => hub.JoinAsync(" ", "x"));
    }

    [Fact]
    public async Task Publishing_requires_connection_and_records_latency()
    {
        var hub = Hub();
        var pin = new Annotation { SeriesId = "s", Kind = AnnotationKind.Pin, Start = default, Label = "A" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.PublishAnnotationAsync(pin));

        var activity = new List<SyncEvent>();
        hub.Activity += (_, e) => activity.Add(e);
        await hub.JoinAsync("MDT-2", "");
        await hub.PublishAnnotationAsync(pin);
        await hub.PublishViewportAsync(new ViewportState("s", SlicePlane.Axial, 4, WindowLevel.Lung, 0, 0, "You"));

        Assert.NotNull(hub.LastRoundTripMs);
        Assert.True(hub.LastRoundTripMs < LoopbackCollaborationHub.LatencyBudgetMs);
        Assert.Equal(3, activity.Count);
        Assert.Contains("slice 5", activity[2].Description);
        Assert.Equal("You", hub.Participants[0].DisplayName);
    }

    [Fact]
    public async Task Remote_events_are_raised_and_marked_remote()
    {
        var hub = Hub();
        await hub.JoinAsync("MDT-3", "Host");
        Annotation? received = null;
        ViewportState? view = null;
        hub.AnnotationReceived += (_, a) => received = a;
        hub.ViewportReceived += (_, v) => view = v;

        await hub.SimulateRemoteAnnotationAsync(new Annotation { SeriesId = "s", Kind = AnnotationKind.Pin, Start = default, Author = "Surgeon" });
        await hub.SimulateRemoteViewportAsync(new ViewportState("s", SlicePlane.Coronal, 2, WindowLevel.Bone, 0, 0, "Surgeon"));

        Assert.True(received!.IsRemote);
        Assert.Equal(SlicePlane.Coronal, view!.Plane);
    }

    [Fact]
    public async Task Default_delay_measures_real_round_trip()
    {
        var hub = new LoopbackCollaborationHub(TimeSpan.FromMilliseconds(5));
        await hub.JoinAsync("MDT-4", "Host");
        await hub.PublishViewportAsync(new ViewportState("s", SlicePlane.Axial, 0, WindowLevel.Lung, 0, 0, "Host"));
        Assert.True(hub.LastRoundTripMs >= 1);
    }
}
