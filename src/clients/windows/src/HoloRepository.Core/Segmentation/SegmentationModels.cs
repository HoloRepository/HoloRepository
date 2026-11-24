using HoloRepository.Core.Imaging;

namespace HoloRepository.Core.Segmentation;

/// <summary>
/// A segmentation model offered by the HoloRepository server (`SegmentationModel` in the API contract),
/// with wording suitable for clinicians.
/// </summary>
public sealed record SegmentationModelInfo(string Id, string DisplayName, string Description)
{
    public static SegmentationModelInfo WholeBodyCt { get; } =
        new("nv-segment-ct", "Organs and structures (CT)", "Outlines the major organs, bones and vessels on a CT scan.");

    public static SegmentationModelInfo Kidney { get; } =
        new("ts-specialist-kidney", "Kidneys", "Detailed outline of the kidneys.");

    public static SegmentationModelInfo Liver { get; } =
        new("ts-specialist-liver", "Liver", "Detailed outline of the liver.");

    public static SegmentationModelInfo HeadAndNeck { get; } =
        new("ts-specialist-head-n-neck", "Head and neck", "Structures of the head and neck.");

    public static IReadOnlyList<SegmentationModelInfo> All { get; } = [WholeBodyCt, Kidney, Liver, HeadAndNeck];

    public static SegmentationModelInfo FromId(string id) =>
        All.FirstOrDefault(m => m.Id == id) ?? new SegmentationModelInfo(id, id, string.Empty);
}

/// <summary>One labelled structure in a segmentation, with its surface for 3D display.</summary>
public sealed record SegmentedStructure(byte Label, string Name, Rgba Colour, int VoxelCount, double VolumeMl, SurfaceMesh? Mesh)
{
    public string VolumeText => VolumeMl >= 10 ? $"{VolumeMl:0} ml" : $"{VolumeMl:0.0} ml";
}

/// <summary>Segmentation of one series, aligned voxel-for-voxel with that series' volume.</summary>
public sealed class SegmentationResult
{
    public required string SeriesId { get; init; }
    public required SegmentationModelInfo Model { get; init; }

    /// <summary>Label per voxel of the series volume (0 = background), or null when only a mesh was returned.</summary>
    public byte[]? Labels { get; init; }

    public IReadOnlyList<SegmentedStructure> Structures { get; init; } = Array.Empty<SegmentedStructure>();

    /// <summary>Where the result came from, e.g. "HoloRepository server" or "Offline demo".</summary>
    public required string Source { get; init; }

    public string? Note { get; init; }

    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.Now;

    public IReadOnlyList<MeshLayer> MeshLayers =>
        Structures.Where(s => s.Mesh is not null).Select(s => new MeshLayer(s.Mesh!, s.Colour)).ToList();

    public Rgba? ColourOf(byte label) => Structures.FirstOrDefault(s => s.Label == label)?.Colour;

    /// <summary>
    /// Builds structures (counts, volumes, colours and surfaces) for every label present in <paramref name="labels"/>.
    /// </summary>
    public static IReadOnlyList<SegmentedStructure> DescribeLabels(Volume volume, byte[] labels,
        Func<byte, string>? nameOf = null, Func<byte, string, Rgba>? colourOf = null, int meshStep = 2)
    {
        var counts = new int[256];
        foreach (byte l in labels) counts[l]++;
        double voxelMl = volume.Spacing.X * volume.Spacing.Y * volume.Spacing.Z / 1000.0;
        var structures = new List<SegmentedStructure>();
        for (int l = 1; l < 256; l++)
        {
            if (counts[l] == 0) continue;
            byte label = (byte)l;
            string name = nameOf?.Invoke(label) ?? $"Structure {l}";
            var colour = colourOf?.Invoke(label, name) ?? LabelPalette.ColourFor(label, name);
            // Small structures (e.g. lesions) get full-resolution surfaces; large organs are meshed at half resolution.
            var mesh = SurfaceExtractor.Extract(volume, labels, label, counts[l] < 20000 ? 1 : meshStep);
            structures.Add(new SegmentedStructure(label, name, colour, counts[l], counts[l] * voxelMl, mesh));
        }
        return structures;
    }
}

/// <summary>Distinct colours for structures; soft or air-filled organs are drawn translucent so inner structures stay visible.</summary>
public static class LabelPalette
{
    private static readonly Rgba[] Colours =
    [
        new(230, 85, 70), new(80, 170, 230), new(240, 200, 80), new(120, 200, 120), new(200, 120, 220),
        new(250, 140, 60), new(90, 210, 200), new(220, 110, 150), new(160, 160, 240), new(180, 220, 90),
        new(240, 160, 160), new(110, 140, 200),
    ];

    public static Rgba ColourFor(byte label, string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("lung") || n.Contains("airway")) return new Rgba(120, 190, 240, 0.3);
        if (n.Contains("bone") || n.Contains("skeleton") || n.Contains("rib") || n.Contains("vertebra")) return new Rgba(235, 225, 200, 0.95);
        if (n.Contains("lesion") || n.Contains("nodule") || n.Contains("tumour") || n.Contains("tumor")) return new Rgba(255, 200, 40, 1);
        if (n.Contains("vessel") || n.Contains("aorta") || n.Contains("arter")) return new Rgba(220, 60, 60, 0.95);
        var c = Colours[(label - 1) % Colours.Length];
        return c with { Opacity = 0.8 };
    }
}

public enum SegmentationStage
{
    Preparing,
    Uploading,
    Queued,
    Processing,
    Downloading,
    Building,
    Done,
}

public sealed record SegmentationProgress(SegmentationStage Stage, string Message);

public sealed record SegmentationRequest(string SeriesId, LoadedSeries Series, SegmentationModelInfo Model);

/// <summary>Runs a segmentation model on a series. Implemented by the server client and the offline demo.</summary>
public interface ISegmentationService
{
    /// <summary>Short name shown in results, e.g. "HoloRepository server".</summary>
    string Name { get; }

    Task<SegmentationResult> SegmentAsync(SegmentationRequest request, IProgress<SegmentationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
