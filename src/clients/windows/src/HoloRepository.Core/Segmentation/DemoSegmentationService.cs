using HoloRepository.Core.Imaging;

namespace HoloRepository.Core.Segmentation;

/// <summary>
/// Offline stand-in used until the HoloRepository server is available. It applies simple, deterministic
/// CT threshold rules (not a trained model) so the segmentation workflow can be demonstrated end to end.
/// Results are clearly marked as a demo.
/// </summary>
public sealed class DemoSegmentationService : ISegmentationService
{
    public const byte Lungs = 1, Bones = 2, Vessels = 3, Lesion = 4;

    private static readonly Dictionary<byte, string> Names = new()
    {
        [Lungs] = "Lungs and airways",
        [Bones] = "Bones",
        [Vessels] = "Contrast-filled vessels",
        [Lesion] = "Lesion candidate",
    };

    public string Name => "Offline demo";

    public Task<SegmentationResult> SegmentAsync(SegmentationRequest request, IProgress<SegmentationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Segment(request, progress, cancellationToken), cancellationToken);

    private SegmentationResult Segment(SegmentationRequest request, IProgress<SegmentationProgress>? progress, CancellationToken ct)
    {
        var volume = request.Series.Volume;
        if (request.Model.Id != SegmentationModelInfo.WholeBodyCt.Id)
        {
            return new SegmentationResult
            {
                SeriesId = request.SeriesId,
                Model = request.Model,
                Labels = new byte[volume.Voxels.Length],
                Source = Name,
                Note = $"The offline demo only supports \"{SegmentationModelInfo.WholeBodyCt.DisplayName}\". " +
                       $"Connect to the HoloRepository server to use the {request.Model.DisplayName.ToLowerInvariant()} model.",
            };
        }

        progress?.Report(new SegmentationProgress(SegmentationStage.Processing, "Finding lungs, bones and vessels…"));
        var labels = ClassifyTissues(volume, ct);

        progress?.Report(new SegmentationProgress(SegmentationStage.Processing, "Looking for lesions inside the lungs…"));
        MarkLesions(volume, labels, ct);

        progress?.Report(new SegmentationProgress(SegmentationStage.Building, "Building 3D surfaces…"));
        var structures = SegmentationResult.DescribeLabels(volume, labels, l => Names.GetValueOrDefault(l, $"Structure {l}"));
        progress?.Report(new SegmentationProgress(SegmentationStage.Done, "Done"));

        return new SegmentationResult
        {
            SeriesId = request.SeriesId,
            Model = request.Model,
            Labels = labels,
            Structures = structures,
            Source = Name,
            Note = "Demo result from simple CT thresholds, not a trained model. Do not use for clinical decisions.",
        };
    }

    /// <summary>Air connected to the edge of each axial slice is outside the body; the rest of the air is lung or airway.</summary>
    internal static byte[] ClassifyTissues(Volume v, CancellationToken ct)
    {
        var labels = new byte[v.Voxels.Length];
        Parallel.For(0, v.Depth, new ParallelOptions { CancellationToken = ct }, z =>
        {
            int w = v.Width, h = v.Height;
            var outside = new bool[w * h];
            var stack = new Stack<int>();
            bool IsAir(int x, int y) => v[x, y, z] < -400;

            for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
            for (int y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                int x = p % w, y = p / w;
                if (x > 0) Seed(x - 1, y);
                if (x < w - 1) Seed(x + 1, y);
                if (y > 0) Seed(x, y - 1);
                if (y < h - 1) Seed(x, y + 1);
            }

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                short hu = v[x, y, z];
                byte label = 0;
                if (hu < -400 && !outside[y * w + x]) label = Lungs;
                else if (hu >= 260) label = Bones;
                else if (hu >= 170) label = Vessels;
                labels[v.IndexOf(x, y, z)] = label;
            }

            void Seed(int x, int y)
            {
                int p = y * w + x;
                if (outside[p] || !IsAir(x, y)) return;
                outside[p] = true;
                stack.Push(p);
            }
        });
        return labels;
    }

    /// <summary>
    /// Soft tissue that has lung on all six sides is a lesion candidate. A morphological opening then removes
    /// thin vessels, leaving rounded nodules.
    /// </summary>
    internal static void MarkLesions(Volume v, byte[] labels, CancellationToken ct, int reach = 12, double openingRadiusMm = 4.5)
    {
        var candidate = new bool[labels.Length];
        Parallel.For(0, v.Depth, new ParallelOptions { CancellationToken = ct }, z =>
        {
            for (int y = 0; y < v.Height; y++)
            for (int x = 0; x < v.Width; x++)
            {
                int i = v.IndexOf(x, y, z);
                short hu = v.Voxels[i];
                if (labels[i] != 0 || hu < -300 || hu >= 170) continue;
                candidate[i] = HitsLung(x, y, z, 1, 0, 0) && HitsLung(x, y, z, -1, 0, 0) &&
                               HitsLung(x, y, z, 0, 1, 0) && HitsLung(x, y, z, 0, -1, 0) &&
                               HitsLung(x, y, z, 0, 0, 1) && HitsLung(x, y, z, 0, 0, -1);
            }
        });

        var opened = Dilate(v, Erode(v, candidate, openingRadiusMm), openingRadiusMm);
        for (int i = 0; i < labels.Length; i++)
            if (opened[i] && candidate[i]) labels[i] = Lesion;

        bool HitsLung(int x, int y, int z, int dx, int dy, int dz)
        {
            for (int s = 1; s <= reach; s++)
            {
                int xx = x + dx * s, yy = y + dy * s, zz = z + dz * s;
                if (!v.Contains(xx, yy, zz)) return false;
                if (labels[v.IndexOf(xx, yy, zz)] == Lungs) return true;
            }
            return false;
        }
    }

    /// <summary>Keeps voxels whose whole ball of radius <paramref name="radiusMm"/> lies inside the mask.</summary>
    internal static bool[] Erode(Volume v, bool[] mask, double radiusMm)
    {
        var offsets = Ball(v, radiusMm);
        var result = new bool[mask.Length];
        Parallel.For(0, v.Depth, z =>
        {
            for (int y = 0; y < v.Height; y++)
            for (int x = 0; x < v.Width; x++)
            {
                if (!mask[v.IndexOf(x, y, z)]) continue;
                bool keep = true;
                foreach (var (dx, dy, dz) in offsets)
                {
                    int xx = x + dx, yy = y + dy, zz = z + dz;
                    if (!v.Contains(xx, yy, zz) || !mask[v.IndexOf(xx, yy, zz)]) { keep = false; break; }
                }
                result[v.IndexOf(x, y, z)] = keep;
            }
        });
        return result;
    }

    /// <summary>Grows every set voxel by a ball of radius <paramref name="radiusMm"/>.</summary>
    internal static bool[] Dilate(Volume v, bool[] mask, double radiusMm)
    {
        var offsets = Ball(v, radiusMm);
        var result = new bool[mask.Length];
        for (int z = 0; z < v.Depth; z++)
        for (int y = 0; y < v.Height; y++)
        for (int x = 0; x < v.Width; x++)
        {
            if (!mask[v.IndexOf(x, y, z)]) continue;
            foreach (var (dx, dy, dz) in offsets)
            {
                int xx = x + dx, yy = y + dy, zz = z + dz;
                if (v.Contains(xx, yy, zz)) result[v.IndexOf(xx, yy, zz)] = true;
            }
        }
        return result;
    }

    /// <summary>Voxel offsets within a physical radius, respecting anisotropic spacing.</summary>
    private static List<(int, int, int)> Ball(Volume v, double radiusMm)
    {
        var s = v.Spacing;
        int rx = (int)(radiusMm / s.X), ry = (int)(radiusMm / s.Y), rz = (int)(radiusMm / s.Z);
        var offsets = new List<(int, int, int)>();
        for (int dz = -rz; dz <= rz; dz++)
        for (int dy = -ry; dy <= ry; dy++)
        for (int dx = -rx; dx <= rx; dx++)
        {
            double d = Math.Sqrt(dx * s.X * dx * s.X + dy * s.Y * dy * s.Y + dz * s.Z * dz * s.Z);
            if (d <= radiusMm + 1e-9) offsets.Add((dx, dy, dz));
        }
        return offsets;
    }
}
