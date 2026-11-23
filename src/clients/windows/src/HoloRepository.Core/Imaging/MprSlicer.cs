namespace HoloRepository.Core.Imaging;

public enum SlicePlane
{
    Axial,
    Coronal,
    Sagittal,
}

/// <summary>A single 2D reformatted slice. Pixels are row-major, top row first.</summary>
public sealed record SliceImage(
    SlicePlane Plane,
    int Index,
    int Width,
    int Height,
    short[] Pixels,
    double PixelWidthMm,
    double PixelHeightMm)
{
    public double PhysicalWidth => Width * PixelWidthMm;
    public double PhysicalHeight => Height * PixelHeightMm;
}

/// <summary>
/// Multi-planar reconstruction (MPR). Coronal and sagittal slices are displayed with the head at the top.
/// Also converts between slice pixel coordinates and patient-space millimetres.
/// </summary>
public static class MprSlicer
{
    public static int SliceCount(Volume v, SlicePlane plane) => plane switch
    {
        SlicePlane.Axial => v.Depth,
        SlicePlane.Coronal => v.Height,
        SlicePlane.Sagittal => v.Width,
        _ => throw new ArgumentOutOfRangeException(nameof(plane)),
    };

    public static int MiddleSlice(Volume v, SlicePlane plane) => SliceCount(v, plane) / 2;

    public static SliceImage Extract(Volume v, SlicePlane plane, int index)
    {
        index = Math.Clamp(index, 0, SliceCount(v, plane) - 1);
        switch (plane)
        {
            case SlicePlane.Axial:
            {
                var px = new short[v.Width * v.Height];
                Array.Copy(v.Voxels, v.IndexOf(0, 0, index), px, 0, px.Length);
                return new SliceImage(plane, index, v.Width, v.Height, px, v.Spacing.X, v.Spacing.Y);
            }
            case SlicePlane.Coronal:
            {
                var px = new short[v.Width * v.Depth];
                for (int r = 0; r < v.Depth; r++)
                {
                    int z = v.Depth - 1 - r;
                    Array.Copy(v.Voxels, v.IndexOf(0, index, z), px, r * v.Width, v.Width);
                }
                return new SliceImage(plane, index, v.Width, v.Depth, px, v.Spacing.X, v.Spacing.Z);
            }
            case SlicePlane.Sagittal:
            {
                var px = new short[v.Height * v.Depth];
                for (int r = 0; r < v.Depth; r++)
                {
                    int z = v.Depth - 1 - r;
                    for (int c = 0; c < v.Height; c++)
                        px[r * v.Height + c] = v[index, c, z];
                }
                return new SliceImage(plane, index, v.Height, v.Depth, px, v.Spacing.Y, v.Spacing.Z);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(plane));
        }
    }

    /// <summary>Converts a continuous pixel position on a slice to patient-space millimetres.</summary>
    public static Point3 SliceToPoint(Volume v, SlicePlane plane, int index, double col, double row)
    {
        var s = v.Spacing;
        return plane switch
        {
            SlicePlane.Axial => new Point3(col * s.X, row * s.Y, (index + 0.5) * s.Z),
            SlicePlane.Coronal => new Point3(col * s.X, (index + 0.5) * s.Y, v.PhysicalDepth - row * s.Z),
            SlicePlane.Sagittal => new Point3((index + 0.5) * s.X, col * s.Y, v.PhysicalDepth - row * s.Z),
            _ => throw new ArgumentOutOfRangeException(nameof(plane)),
        };
    }

    /// <summary>Projects a patient-space point onto a plane, returning pixel coordinates and the slice containing it.</summary>
    public static (double Col, double Row, int Index) PointToSlice(Volume v, SlicePlane plane, Point3 p)
    {
        var s = v.Spacing;
        return plane switch
        {
            SlicePlane.Axial => (p.X / s.X, p.Y / s.Y, ClampIndex((int)Math.Floor(p.Z / s.Z), v.Depth)),
            SlicePlane.Coronal => (p.X / s.X, (v.PhysicalDepth - p.Z) / s.Z, ClampIndex((int)Math.Floor(p.Y / s.Y), v.Height)),
            SlicePlane.Sagittal => (p.Y / s.Y, (v.PhysicalDepth - p.Z) / s.Z, ClampIndex((int)Math.Floor(p.X / s.X), v.Width)),
            _ => throw new ArgumentOutOfRangeException(nameof(plane)),
        };
    }

    /// <summary>
    /// Given a click on one plane, returns the slice indices the other two planes should jump to (crosshair sync).
    /// </summary>
    public static IReadOnlyDictionary<SlicePlane, int> CrosshairIndices(Volume v, Point3 p) => new Dictionary<SlicePlane, int>
    {
        [SlicePlane.Axial] = PointToSlice(v, SlicePlane.Axial, p).Index,
        [SlicePlane.Coronal] = PointToSlice(v, SlicePlane.Coronal, p).Index,
        [SlicePlane.Sagittal] = PointToSlice(v, SlicePlane.Sagittal, p).Index,
    };

    private static int ClampIndex(int i, int count) => Math.Clamp(i, 0, count - 1);
}
