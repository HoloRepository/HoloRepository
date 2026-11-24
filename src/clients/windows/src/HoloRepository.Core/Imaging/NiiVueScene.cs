using System.Buffers.Binary;
using System.Text.Json;
using HoloRepository.Core.Segmentation;

namespace HoloRepository.Core.Imaging;

/// <summary>A mesh entry in a NiiVue scene: file name (relative to the scene folder), name and colour.</summary>
public sealed record NiiVueMesh(string Url, string Name, int[] Rgba255, byte Label);

/// <summary>Label colour table in NiiVue's ColorMap shape (parallel arrays indexed by label value).</summary>
public sealed record NiiVueLabelColours(int[] R, int[] G, int[] B, int[] A, int[] I, string[] Labels);

/// <summary>
/// Everything NiiVue needs to show a series: the volume, an optional label overlay and organ meshes.
/// File URLs are relative to the folder the scene was written to.
/// </summary>
public sealed record NiiVueSceneDescription(
    string VolumeUrl,
    double CalMin,
    double CalMax,
    string? LabelsUrl,
    NiiVueLabelColours? LabelColours,
    IReadOnlyList<NiiVueMesh> Meshes)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

/// <summary>
/// Writes a series (and its segmentation) in the formats NiiVue reads: NIfTI-1 for the volume and labels,
/// MZ3 for surfaces. Everything is written in RAS millimetres, NiiVue's world space, so volume, labels and
/// meshes line up exactly.
/// </summary>
public static class NiiVueSceneWriter
{
    public static NiiVueSceneDescription Write(string folder, Volume volume, WindowLevel window,
        SegmentationResult? segmentation = null, ISet<byte>? hiddenLabels = null)
    {
        Directory.CreateDirectory(folder);
        var affine = RasAffine(volume);

        using (var s = File.Create(Path.Combine(folder, "volume.nii")))
            WriteInt16(s, volume, affine);

        string? labelsUrl = null;
        NiiVueLabelColours? colours = null;
        var meshes = new List<NiiVueMesh>();

        if (segmentation is not null)
        {
            var visible = segmentation.Structures.Where(st => hiddenLabels is null || !hiddenLabels.Contains(st.Label)).ToList();

            if (segmentation.Labels is { } labels && visible.Count > 0)
            {
                var shown = visible.Select(st => st.Label).ToHashSet();
                var data = new float[labels.Length];
                for (int i = 0; i < labels.Length; i++) data[i] = shown.Contains(labels[i]) ? labels[i] : 0;
                using (var s = File.Create(Path.Combine(folder, "labels.nii")))
                    NiftiImage.Write(s, volume.Width, volume.Height, volume.Depth, data, affine, asLabels: true);
                labelsUrl = "labels.nii";
                colours = LabelColours(segmentation.Structures);
            }

            foreach (var st in visible.Where(st => st.Mesh is not null))
            {
                string name = $"mesh-{st.Label}.mz3";
                var ras = st.Mesh!.Transform(p => PatientFrame.LpsToRas(volume.ToPatient(p)));
                using (var s = File.Create(Path.Combine(folder, name)))
                    Mz3.Write(s, ras, gzip: false);
                var c = st.Colour;
                meshes.Add(new NiiVueMesh(name, st.Name, [c.R, c.G, c.B, (int)Math.Round(c.Opacity * 255)], st.Label));
            }
        }

        return new NiiVueSceneDescription("volume.nii", window.Lower, window.Upper, labelsUrl, colours, meshes);
    }

    /// <summary>Row-major 3×4 voxel → RAS affine for a volume, from its patient (LPS) frame.</summary>
    public static double[] RasAffine(Volume v)
    {
        var f = v.Frame;
        var s = v.Spacing;
        var o = PatientFrame.LpsToRas(f.Origin);
        Point3 Col(Point3 dir, double spacing) => PatientFrame.LpsToRas(new Point3(dir.X * spacing, dir.Y * spacing, dir.Z * spacing));
        var x = Col(f.XDirection, s.X);
        var y = Col(f.YDirection, s.Y);
        var z = Col(f.ZDirection, s.Z);
        return [x.X, y.X, z.X, o.X, x.Y, y.Y, z.Y, o.Y, x.Z, y.Z, z.Z, o.Z];
    }

    /// <summary>Label colours with label 0 transparent. Unused entries are filled so the arrays stay aligned.</summary>
    public static NiiVueLabelColours LabelColours(IReadOnlyList<SegmentedStructure> structures)
    {
        int max = structures.Count == 0 ? 0 : structures.Max(s => s.Label);
        int n = max + 1;
        var r = new int[n]; var g = new int[n]; var b = new int[n]; var a = new int[n]; var idx = new int[n];
        var names = new string[n];
        for (int i = 0; i < n; i++)
        {
            idx[i] = i;
            names[i] = i == 0 ? "background" : string.Empty;
        }
        foreach (var st in structures)
        {
            r[st.Label] = st.Colour.R;
            g[st.Label] = st.Colour.G;
            b[st.Label] = st.Colour.B;
            a[st.Label] = 255;
            names[st.Label] = st.Name;
        }
        return new NiiVueLabelColours(r, g, b, a, idx, names);
    }

    /// <summary>Uncompressed NIfTI-1 with int16 voxels (datatype 4) and an sform.</summary>
    internal static void WriteInt16(Stream stream, Volume v, double[] affine)
    {
        var header = new byte[352];
        var h = header.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(h, 348);
        BinaryPrimitives.WriteInt16LittleEndian(h[40..], 3);
        BinaryPrimitives.WriteInt16LittleEndian(h[42..], (short)v.Width);
        BinaryPrimitives.WriteInt16LittleEndian(h[44..], (short)v.Height);
        BinaryPrimitives.WriteInt16LittleEndian(h[46..], (short)v.Depth);
        BinaryPrimitives.WriteInt16LittleEndian(h[48..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(h[70..], 4);
        BinaryPrimitives.WriteInt16LittleEndian(h[72..], 16);
        BinaryPrimitives.WriteSingleLittleEndian(h[76..], 1);
        BinaryPrimitives.WriteSingleLittleEndian(h[80..], (float)v.Spacing.X);
        BinaryPrimitives.WriteSingleLittleEndian(h[84..], (float)v.Spacing.Y);
        BinaryPrimitives.WriteSingleLittleEndian(h[88..], (float)v.Spacing.Z);
        BinaryPrimitives.WriteSingleLittleEndian(h[108..], 352);
        BinaryPrimitives.WriteSingleLittleEndian(h[112..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(h[254..], 2);
        for (int i = 0; i < 12; i++) BinaryPrimitives.WriteSingleLittleEndian(h[(280 + i * 4)..], (float)affine[i]);
        "n+1\0"u8.CopyTo(h[344..]);
        stream.Write(header);

        var buffer = new byte[v.Voxels.Length * 2];
        for (int i = 0; i < v.Voxels.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(i * 2), v.Voxels[i]);
        stream.Write(buffer);
    }
}
