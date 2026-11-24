namespace HoloRepository.Core.Imaging;

/// <summary>Physical size of a voxel in millimetres along each volume axis.</summary>
public readonly record struct VoxelSpacing(double X, double Y, double Z)
{
    public static VoxelSpacing Isotropic(double mm) => new(mm, mm, mm);

    public double Min => Math.Min(X, Math.Min(Y, Z));
}

/// <summary>A point in patient space, expressed in millimetres from the volume origin.</summary>
public readonly record struct Point3(double X, double Y, double Z)
{
    public double DistanceTo(Point3 other)
    {
        double dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public override string ToString() => $"({X:0.0}, {Y:0.0}, {Z:0.0}) mm";
}

/// <summary>
/// A scalar 3D volume (typically CT Hounsfield units) stored x-fastest, then y, then z.
/// </summary>
public sealed class Volume
{
    public Volume(int width, int height, int depth, VoxelSpacing spacing, short[]? voxels = null)
    {
        if (width <= 0 || height <= 0 || depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Volume dimensions must be positive.");
        if (spacing.X <= 0 || spacing.Y <= 0 || spacing.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(spacing), "Voxel spacing must be positive.");

        long count = (long)width * height * depth;
        if (voxels is not null && voxels.LongLength != count)
            throw new ArgumentException($"Expected {count} voxels but got {voxels.LongLength}.", nameof(voxels));

        Width = width;
        Height = height;
        Depth = depth;
        Spacing = spacing;
        Voxels = voxels ?? new short[count];
    }

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public VoxelSpacing Spacing { get; }
    public short[] Voxels { get; }

    public double PhysicalWidth => Width * Spacing.X;
    public double PhysicalHeight => Height * Spacing.Y;
    public double PhysicalDepth => Depth * Spacing.Z;

    public Point3 Centre => new(PhysicalWidth / 2, PhysicalHeight / 2, PhysicalDepth / 2);

    public int IndexOf(int x, int y, int z) => (z * Height + y) * Width + x;

    public bool Contains(int x, int y, int z) =>
        x >= 0 && y >= 0 && z >= 0 && x < Width && y < Height && z < Depth;

    public short this[int x, int y, int z]
    {
        get => Voxels[IndexOf(x, y, z)];
        set => Voxels[IndexOf(x, y, z)] = value;
    }

    /// <summary>Nearest-neighbour sample at a patient-space position; returns <paramref name="outside"/> beyond the bounds.</summary>
    public short SampleMm(double xMm, double yMm, double zMm, short outside = -1024)
    {
        int x = (int)(xMm / Spacing.X);
        int y = (int)(yMm / Spacing.Y);
        int z = (int)(zMm / Spacing.Z);
        if (xMm < 0 || yMm < 0 || zMm < 0 || !Contains(x, y, z)) return outside;
        return Voxels[IndexOf(x, y, z)];
    }

    public Point3 VoxelToMm(double x, double y, double z) =>
        new((x + 0.5) * Spacing.X, (y + 0.5) * Spacing.Y, (z + 0.5) * Spacing.Z);

    public (int X, int Y, int Z) MmToVoxel(Point3 p) =>
        (Clamp((int)(p.X / Spacing.X), Width), Clamp((int)(p.Y / Spacing.Y), Height), Clamp((int)(p.Z / Spacing.Z), Depth));

    public (short Min, short Max) Range()
    {
        short min = short.MaxValue, max = short.MinValue;
        foreach (short v in Voxels)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }
        return (min, max);
    }

    private static int Clamp(int v, int size) => Math.Clamp(v, 0, size - 1);
}
