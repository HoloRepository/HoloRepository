namespace HoloRepository.Core.Imaging;

/// <summary>
/// Builds a deterministic synthetic chest CT so the app can be demonstrated and tested
/// without any real patient data. Values are approximate Hounsfield units.
/// </summary>
public static class PhantomGenerator
{
    public const short Air = -1000;
    public const short Lung = -850;
    public const short Fat = -100;
    public const short SoftTissue = 40;
    public const short Blood = 45;
    public const short ContrastVessel = 220;
    public const short Cortical = 750;
    public const short Cancellous = 300;
    public const short Nodule = 35;

    public const double NoduleRadiusMm = 9;

    /// <summary>Patient-space centre of the synthetic right upper lobe nodule for a given volume.</summary>
    public static Point3 NoduleCentre(Volume v) =>
        new(v.PhysicalWidth * 0.30, v.PhysicalHeight * 0.45, v.PhysicalDepth * 0.66);

    public static LoadedSeries CreateChestSeries(int width = 160, int height = 128, int depth = 120)
    {
        var volume = CreateChest(width, height, depth);
        var metadata = new StudyMetadata
        {
            PatientName = "PHANTOM^CHEST",
            PatientId = "SYNTH-0001",
            Modality = "CT",
            StudyDescription = "CT Thorax with contrast (synthetic)",
            SeriesDescription = "Sample chest CT",
            StudyInstanceUid = "2.25.100000000000000000000000000000001",
            SeriesInstanceUid = "2.25.100000000000000000000000000000002",
            StudyDate = new DateTime(2026, 11, 1),
            Source = "Built-in sample (not a real patient)",
            IsSynthetic = true,
        };
        return new LoadedSeries(metadata, volume);
    }

    public static Volume CreateChest(int width = 160, int height = 128, int depth = 120)
    {
        // Physical field of view of roughly 350 x 280 x 300 mm.
        var spacing = new VoxelSpacing(350.0 / width, 280.0 / height, 300.0 / depth);
        var v = new Volume(width, height, depth, spacing);

        double pw = v.PhysicalWidth, ph = v.PhysicalHeight, pd = v.PhysicalDepth;
        double cx = pw / 2, cy = ph / 2;
        double bodyA = pw * 0.45, bodyB = ph * 0.42;
        var nodule = NoduleCentre(v);

        Parallel.For(0, depth, z =>
        {
            double zm = (z + 0.5) * spacing.Z;
            bool discLevel = (zm % 30) < 5;

            for (int y = 0; y < height; y++)
            {
                double ym = (y + 0.5) * spacing.Y;
                for (int x = 0; x < width; x++)
                {
                    double xm = (x + 0.5) * spacing.X;
                    short value = Air;

                    double bx = (xm - cx) / bodyA, by = (ym - cy) / bodyB;
                    double body = bx * bx + by * by;
                    if (body <= 1)
                    {
                        value = body > 0.82 ? Fat : SoftTissue;

                        // Ribs: an elliptical shell just inside the fat layer, sloping down towards the front
                        // and stopping short of the sternum (costal cartilage gap).
                        double ribZ = zm + 0.35 * (ym - cy);
                        bool ribLevel = (ribZ % 26 + 26) % 26 < 8 && zm > pd * 0.12 && zm < pd * 0.88;
                        bool nearSternum = Math.Abs(xm - cx) < pw * 0.1 && ym < cy;
                        if (ribLevel && !nearSternum && body > 0.66 && body < 0.76)
                            value = Cortical;

                        // Sternum.
                        if (Math.Abs(xm - cx) < 12 && ym > cy - bodyB * 0.92 && ym < cy - bodyB * 0.78 && zm > pd * 0.3 && zm < pd * 0.8)
                            value = Cancellous + 150;

                        // Lungs: two tapered ellipsoids.
                        if (InLung(xm, ym, zm, cx - pw * 0.19, cy - ph * 0.01, pd * 0.55, pw, ph, pd) ||
                            InLung(xm, ym, zm, cx + pw * 0.19, cy - ph * 0.01, pd * 0.55, pw, ph, pd))
                            value = Lung;

                        // Heart sits anterior and slightly to the patient's left.
                        if (Ellipsoid(xm, ym, zm, cx + pw * 0.06, cy - ph * 0.10, pd * 0.38, pw * 0.15, ph * 0.20, pd * 0.16))
                            value = Blood;

                        // Descending aorta (contrast enhanced).
                        if (Circle(xm, ym, cx + pw * 0.06, cy + ph * 0.18, 11) && zm < pd * 0.72 && zm > pd * 0.1)
                            value = ContrastVessel;

                        // Trachea.
                        if (Circle(xm, ym, cx, cy - ph * 0.02, 8) && zm > pd * 0.62)
                            value = Air;

                        // Vertebral column with intervertebral discs.
                        double sx = xm - cx, sy = ym - (cy + ph * 0.33);
                        double sr = Math.Sqrt(sx * sx + sy * sy);
                        if (sr < 20)
                            value = discLevel ? (short)80 : sr > 16 ? Cortical : Cancellous;

                        // Spinous process.
                        if (!discLevel && Math.Abs(sx) < 4 && ym > cy + ph * 0.33 + 20 && ym < cy + ph * 0.33 + 40)
                            value = Cortical;

                        if (value == Lung && nodule.DistanceTo(new Point3(xm, ym, zm)) < NoduleRadiusMm + Spiculation(xm, ym, zm))
                            value = Nodule;
                    }

                    v[x, y, z] = (short)Math.Clamp(value + Noise(x, y, z), short.MinValue, short.MaxValue);
                }
            }
        });

        AddPulmonaryVessels(v, cx, cy, pw, ph, pd);
        return v;
    }

    private static bool InLung(double x, double y, double z, double lx, double ly, double lz, double pw, double ph, double pd)
    {
        // Narrow towards the apex for a more anatomical silhouette.
        double rz = (z - lz) / (pd * 0.36);
        if (rz > 1 || rz < -1) return false;
        double taper = rz > 0 ? 1 - 0.55 * rz * rz : 1 - 0.1 * rz * rz;
        double rx = (x - lx) / (pw * 0.15 * taper);
        double ry = (y - ly) / (ph * 0.30 * taper);
        return rx * rx + ry * ry + Math.Pow(rz, 4) <= 1;
    }

    private static bool Ellipsoid(double x, double y, double z, double cx, double cy, double cz, double a, double b, double c)
    {
        double dx = (x - cx) / a, dy = (y - cy) / b, dz = (z - cz) / c;
        return dx * dx + dy * dy + dz * dz <= 1;
    }

    private static bool Circle(double x, double y, double cx, double cy, double r)
    {
        double dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= r * r;
    }

    private static double Spiculation(double x, double y, double z) =>
        1.5 * Math.Sin(x * 0.9) * Math.Cos(y * 0.8) * Math.Sin(z * 0.7);

    private static int Noise(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (int)(h % 31) - 15;
        }
    }

    /// <summary>Stamps a seeded branching vessel tree into each lung from the hilum outwards.</summary>
    private static void AddPulmonaryVessels(Volume v, double cx, double cy, double pw, double ph, double pd)
    {
        var rng = new Random(2027);
        foreach (int side in new[] { -1, 1 })
        {
            var hilum = new Point3(cx + side * pw * 0.11, cy - ph * 0.02, pd * 0.55);
            for (int i = 0; i < 4; i++)
            {
                double up = (i - 1.5) * 0.55;
                var dir = Normalise(side * 1.0, rng.NextDouble() * 0.6 - 0.3, up);
                Branch(v, hilum, dir, 38, 3.6, 0, rng);
            }
        }
    }

    private static void Branch(Volume v, Point3 start, (double X, double Y, double Z) dir, double length, double radius, int depth, Random rng)
    {
        if (depth > 4 || radius < 0.9) return;
        var end = new Point3(start.X + dir.X * length, start.Y + dir.Y * length, start.Z + dir.Z * length);
        StampSegment(v, start, end, radius);

        int children = 2;
        for (int c = 0; c < children; c++)
        {
            var jitter = Normalise(
                dir.X + (rng.NextDouble() - 0.5) * 0.9,
                dir.Y + (rng.NextDouble() - 0.5) * 0.9,
                dir.Z + (rng.NextDouble() - 0.5) * 0.9);
            Branch(v, end, jitter, length * 0.72, radius * 0.7, depth + 1, rng);
        }
    }

    private static void StampSegment(Volume v, Point3 a, Point3 b, double radius)
    {
        double len = a.DistanceTo(b);
        int steps = Math.Max(1, (int)(len / Math.Max(0.5, radius * 0.5)));
        for (int s = 0; s <= steps; s++)
        {
            double t = (double)s / steps;
            var p = new Point3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
            StampSphere(v, p, radius);
        }
    }

    private static void StampSphere(Volume v, Point3 c, double r)
    {
        var sp = v.Spacing;
        int x0 = (int)((c.X - r) / sp.X), x1 = (int)((c.X + r) / sp.X);
        int y0 = (int)((c.Y - r) / sp.Y), y1 = (int)((c.Y + r) / sp.Y);
        int z0 = (int)((c.Z - r) / sp.Z), z1 = (int)((c.Z + r) / sp.Z);
        for (int z = z0; z <= z1; z++)
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            if (!v.Contains(x, y, z)) continue;
            var p = new Point3((x + 0.5) * sp.X, (y + 0.5) * sp.Y, (z + 0.5) * sp.Z);
            // Vessels only exist inside aerated lung.
            if (p.DistanceTo(c) <= r && v[x, y, z] < -500)
                v[x, y, z] = Blood;
        }
    }

    private static (double X, double Y, double Z) Normalise(double x, double y, double z)
    {
        double l = Math.Sqrt(x * x + y * y + z * z);
        return l == 0 ? (1, 0, 0) : (x / l, y / l, z / l);
    }
}
