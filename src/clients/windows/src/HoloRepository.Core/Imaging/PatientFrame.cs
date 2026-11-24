namespace HoloRepository.Core.Imaging;

/// <summary>
/// Places a volume in patient space using the DICOM LPS convention (x → patient left, y → posterior, z → superior).
/// <see cref="Origin"/> is the centre of voxel (0,0,0); the three directions are unit vectors along the volume's
/// x (column), y (row) and z (slice) axes.
/// </summary>
public sealed record PatientFrame(Point3 Origin, Point3 XDirection, Point3 YDirection, Point3 ZDirection)
{
    public static PatientFrame Identity { get; } =
        new(new Point3(0, 0, 0), new Point3(1, 0, 0), new Point3(0, 1, 0), new Point3(0, 0, 1));

    /// <summary>Volume mm (voxel centres at (i + 0.5) × spacing) → patient LPS mm.</summary>
    public Point3 ToPatient(Point3 v, VoxelSpacing s)
    {
        double a = v.X - 0.5 * s.X, b = v.Y - 0.5 * s.Y, c = v.Z - 0.5 * s.Z;
        return new Point3(
            Origin.X + XDirection.X * a + YDirection.X * b + ZDirection.X * c,
            Origin.Y + XDirection.Y * a + YDirection.Y * b + ZDirection.Y * c,
            Origin.Z + XDirection.Z * a + YDirection.Z * b + ZDirection.Z * c);
    }

    /// <summary>Patient LPS mm → volume mm. Assumes orthonormal directions, as in DICOM and canonicalised NIfTI.</summary>
    public Point3 FromPatient(Point3 p, VoxelSpacing s)
    {
        double dx = p.X - Origin.X, dy = p.Y - Origin.Y, dz = p.Z - Origin.Z;
        return new Point3(
            Dot(dx, dy, dz, XDirection) + 0.5 * s.X,
            Dot(dx, dy, dz, YDirection) + 0.5 * s.Y,
            Dot(dx, dy, dz, ZDirection) + 0.5 * s.Z);
    }

    /// <summary>NIfTI and the HoloRepository server use RAS; DICOM uses LPS. The conversion flips x and y.</summary>
    public static Point3 RasToLps(Point3 ras) => new(-ras.X, -ras.Y, ras.Z);

    public static Point3 LpsToRas(Point3 lps) => new(-lps.X, -lps.Y, lps.Z);

    private static double Dot(double x, double y, double z, Point3 d) => x * d.X + y * d.Y + z * d.Z;
}
