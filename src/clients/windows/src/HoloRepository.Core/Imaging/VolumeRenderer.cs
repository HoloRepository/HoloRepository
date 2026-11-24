namespace HoloRepository.Core.Imaging;

public enum RenderMode
{
    /// <summary>Maximum intensity projection.</summary>
    MaximumIntensity,

    /// <summary>Front-to-back alpha compositing with a simple tissue/bone colour transfer function.</summary>
    Composite,
}

public sealed record VolumeRenderRequest(
    double YawDegrees,
    double PitchDegrees,
    int Size,
    WindowLevel Window,
    RenderMode Mode = RenderMode.MaximumIntensity,
    double Zoom = 1.0);

/// <summary>An opaque BGRA image produced by a renderer.</summary>
public sealed record RenderedImage(int Width, int Height, byte[] Bgra, TimeSpan Elapsed);

/// <summary>
/// Orthographic CPU ray caster. This is the reference implementation that a GPU (CUDA / OpenVINO)
/// backend in the compute daemon is expected to reproduce at higher resolution and frame rate.
/// The default camera looks at the patient from the front (anterior view) with the head at the top.
/// </summary>
public static class VolumeRenderer
{
    private readonly record struct Vec(double X, double Y, double Z)
    {
        public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec operator *(Vec a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public double Dot(Vec o) => X * o.X + Y * o.Y + Z * o.Z;
    }

    private readonly record struct Camera(Vec Right, Vec Up, Vec Forward, Vec Centre, double HalfExtent, double HalfDepth);

    /// <summary>The camera of a render request, for projecting meshes and markers into the rendered image.</summary>
    public readonly struct ViewTransform
    {
        private readonly double _rx, _ry, _rz, _ux, _uy, _uz, _fx, _fy, _fz, _cx, _cy, _cz, _half;
        private readonly int _size;

        internal ViewTransform(Volume volume, VolumeRenderRequest request)
        {
            var cam = BuildCamera(volume, request);
            (_rx, _ry, _rz) = (cam.Right.X, cam.Right.Y, cam.Right.Z);
            (_ux, _uy, _uz) = (cam.Up.X, cam.Up.Y, cam.Up.Z);
            (_fx, _fy, _fz) = (cam.Forward.X, cam.Forward.Y, cam.Forward.Z);
            (_cx, _cy, _cz) = (cam.Centre.X, cam.Centre.Y, cam.Centre.Z);
            _half = cam.HalfExtent;
            _size = request.Size;
        }

        public int Size => _size;

        /// <summary>View direction in volume space (for lighting).</summary>
        public (double X, double Y, double Z) Forward => (_fx, _fy, _fz);

        /// <summary>Pixel position and depth (larger = further from the viewer) of a volume-space point.</summary>
        public (double X, double Y, double Depth) Project(double x, double y, double z)
        {
            double dx = x - _cx, dy = y - _cy, dz = z - _cz;
            double u = (dx * _rx + dy * _ry + dz * _rz) / _half;
            double v = (dx * _ux + dy * _uy + dz * _uz) / _half;
            return ((u + 1) / 2 * _size, (1 - v) / 2 * _size, dx * _fx + dy * _fy + dz * _fz);
        }
    }

    public static ViewTransform GetView(Volume volume, VolumeRenderRequest request) => new(volume, request);

    public static RenderedImage Render(Volume volume, VolumeRenderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Size <= 0) throw new ArgumentOutOfRangeException(nameof(request), "Size must be positive.");

        var started = DateTime.UtcNow;
        int size = request.Size;
        var bgra = new byte[size * size * 4];
        var cam = BuildCamera(volume, request);
        double step = volume.Spacing.Min;
        var window = request.Window;
        double pw = volume.PhysicalWidth, ph = volume.PhysicalHeight, pd = volume.PhysicalDepth;

        Parallel.For(0, size, new ParallelOptions { CancellationToken = cancellationToken }, j =>
        {
            double v = 1 - (j + 0.5) / size * 2;
            for (int i = 0; i < size; i++)
            {
                double u = (i + 0.5) / size * 2 - 1;
                Vec origin = cam.Centre + cam.Right * (u * cam.HalfExtent) + cam.Up * (v * cam.HalfExtent) - cam.Forward * cam.HalfDepth;

                int o = (j * size + i) * 4;
                if (!IntersectBox(origin, cam.Forward, pw, ph, pd, out double t0, out double t1))
                {
                    WriteBackground(bgra, o, request.Mode);
                    continue;
                }

                if (request.Mode == RenderMode.MaximumIntensity)
                {
                    short max = short.MinValue;
                    for (double t = t0; t <= t1; t += step)
                    {
                        Vec p = origin + cam.Forward * t;
                        short s = volume.SampleMm(p.X, p.Y, p.Z);
                        if (s > max) max = s;
                    }
                    byte g = window.Apply(max);
                    bgra[o] = g; bgra[o + 1] = g; bgra[o + 2] = g; bgra[o + 3] = 255;
                }
                else
                {
                    Composite(volume, origin, cam.Forward, t0, t1, step, window, bgra, o);
                }
            }
        });

        return new RenderedImage(size, size, bgra, DateTime.UtcNow - started);
    }

    /// <summary>Projects a patient-space point to pixel coordinates in an image rendered with the same request.</summary>
    public static (double X, double Y) Project(Volume volume, VolumeRenderRequest request, Point3 point)
    {
        var cam = BuildCamera(volume, request);
        var d = new Vec(point.X, point.Y, point.Z) - cam.Centre;
        double u = d.Dot(cam.Right) / cam.HalfExtent;
        double v = d.Dot(cam.Up) / cam.HalfExtent;
        return ((u + 1) / 2 * request.Size, (1 - v) / 2 * request.Size);
    }

    private static Camera BuildCamera(Volume volume, VolumeRenderRequest request)
    {
        double yaw = request.YawDegrees * Math.PI / 180;
        double pitch = request.PitchDegrees * Math.PI / 180;

        // Start from an anterior view: screen-right = +x, screen-up = +z (head), looking along +y.
        var right = new Vec(Math.Cos(yaw), Math.Sin(yaw), 0);
        var forward = new Vec(-Math.Sin(yaw), Math.Cos(yaw), 0);
        var up = new Vec(0, 0, 1);

        var forwardP = forward * Math.Cos(pitch) + up * Math.Sin(pitch);
        var upP = up * Math.Cos(pitch) - forward * Math.Sin(pitch);

        var c = volume.Centre;
        double diag = Math.Sqrt(volume.PhysicalWidth * volume.PhysicalWidth +
                                volume.PhysicalHeight * volume.PhysicalHeight +
                                volume.PhysicalDepth * volume.PhysicalDepth);
        double zoom = request.Zoom <= 0 ? 1 : request.Zoom;
        return new Camera(right, upP, forwardP, new Vec(c.X, c.Y, c.Z), diag / 2 / zoom, diag / 2);
    }

    private static void Composite(Volume volume, Vec origin, Vec dir, double t0, double t1, double step, WindowLevel window,
        byte[] bgra, int o)
    {
        double r = 0, g = 0, b = 0, a = 0;
        double lower = window.Lower, width = Math.Max(window.Width, 1);
        for (double t = t0; t <= t1 && a < 0.97; t += step)
        {
            Vec p = origin + dir * t;
            short s = volume.SampleMm(p.X, p.Y, p.Z);
            double x = (s - lower) / width;
            if (x <= 0.05) continue;
            if (x > 1) x = 1;

            double alpha = x * x * 0.09;
            // Soft tissue is rendered red, dense structures shift towards ivory.
            double cr = 0.75 + 0.2 * x, cg = 0.22 + 0.68 * x * x, cb = 0.2 + 0.62 * x * x;
            double w = (1 - a) * alpha;
            r += w * cr; g += w * cg; b += w * cb;
            a += w;
        }

        // Blend over a dark clinical background.
        double bg = 1 - a;
        bgra[o] = ToByte(b + bg * 0.09);
        bgra[o + 1] = ToByte(g + bg * 0.06);
        bgra[o + 2] = ToByte(r + bg * 0.04);
        bgra[o + 3] = 255;
    }

    private static void WriteBackground(byte[] bgra, int o, RenderMode mode)
    {
        if (mode == RenderMode.Composite)
        {
            bgra[o] = ToByte(0.09); bgra[o + 1] = ToByte(0.06); bgra[o + 2] = ToByte(0.04);
        }
        else
        {
            bgra[o] = 0; bgra[o + 1] = 0; bgra[o + 2] = 0;
        }
        bgra[o + 3] = 255;
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(v * 255 + 0.5, 0, 255);

    private static bool IntersectBox(Vec o, Vec d, double w, double h, double depth, out double tMin, out double tMax)
    {
        tMin = double.NegativeInfinity;
        tMax = double.PositiveInfinity;
        if (!Slab(o.X, d.X, w, ref tMin, ref tMax)) return false;
        if (!Slab(o.Y, d.Y, h, ref tMin, ref tMax)) return false;
        if (!Slab(o.Z, d.Z, depth, ref tMin, ref tMax)) return false;
        tMin = Math.Max(tMin, 0);
        return tMax > tMin;
    }

    private static bool Slab(double o, double d, double max, ref double tMin, ref double tMax)
    {
        if (Math.Abs(d) < 1e-12) return o >= 0 && o <= max;
        double a = (0 - o) / d, b = (max - o) / d;
        if (a > b) (a, b) = (b, a);
        tMin = Math.Max(tMin, a);
        tMax = Math.Min(tMax, b);
        return tMax >= tMin;
    }
}
