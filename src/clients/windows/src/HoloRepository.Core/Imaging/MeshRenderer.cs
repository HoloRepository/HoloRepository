namespace HoloRepository.Core.Imaging;

/// <summary>A mesh to draw, in volume millimetres, with its colour and opacity.</summary>
public sealed record MeshLayer(SurfaceMesh Mesh, Rgba Colour);

/// <summary>
/// Software rasteriser that draws segmented structures over a volume render using the same camera.
/// Each structure keeps its nearest surface per pixel; up to <see cref="MaxLayers"/> structures per pixel are
/// blended back to front, so translucent organs (e.g. lungs) still show what is inside them.
/// </summary>
public static class MeshRenderer
{
    public const int MaxLayers = 4;

    public static void Composite(Volume volume, VolumeRenderRequest request, IReadOnlyList<MeshLayer> layers, byte[] bgra,
        CancellationToken cancellationToken = default)
    {
        int size = request.Size;
        if (bgra.Length < size * size * 4) throw new ArgumentException("Image buffer is too small.", nameof(bgra));
        if (layers.Count == 0) return;

        var view = VolumeRenderer.GetView(volume, request);
        int pixels = size * size;

        // Per-pixel fragment lists: nearest surface of up to MaxLayers structures.
        var fragDepth = new float[pixels * MaxLayers];
        var fragColour = new uint[pixels * MaxLayers];
        var fragCount = new byte[pixels];

        var depth = new float[pixels];
        var shade = new float[pixels];
        var (fx, fy, fz) = view.Forward;

        foreach (var layer in layers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Array.Fill(depth, float.PositiveInfinity);
            var mesh = layer.Mesh;
            var projected = new (float X, float Y, float D)[mesh.VertexCount];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var p = view.Project(mesh.Vertices[i * 3], mesh.Vertices[i * 3 + 1], mesh.Vertices[i * 3 + 2]);
                projected[i] = ((float)p.X, (float)p.Y, (float)p.Depth);
            }

            var idx = mesh.Indices;
            var v = mesh.Vertices;
            for (int t = 0; t < idx.Length; t += 3)
            {
                int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                var pa = projected[a]; var pb = projected[b]; var pc = projected[c];

                // Lambert shading from the face normal, lit from the viewer (two-sided).
                double e1x = v[b * 3] - v[a * 3], e1y = v[b * 3 + 1] - v[a * 3 + 1], e1z = v[b * 3 + 2] - v[a * 3 + 2];
                double e2x = v[c * 3] - v[a * 3], e2y = v[c * 3 + 1] - v[a * 3 + 1], e2z = v[c * 3 + 2] - v[a * 3 + 2];
                double nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-12) continue;
                float light = (float)(0.3 + 0.7 * Math.Abs((nx * fx + ny * fy + nz * fz) / len));

                RasteriseTriangle(pa, pb, pc, light, size, depth, shade);
            }

            byte r = layer.Colour.R, g = layer.Colour.G, bl = layer.Colour.B;
            byte alpha = (byte)Math.Clamp(layer.Colour.Opacity * 255, 0, 255);
            for (int p = 0; p < pixels; p++)
            {
                if (float.IsPositiveInfinity(depth[p])) continue;
                float s = shade[p];
                uint colour = (uint)(alpha << 24 | (byte)(r * s) << 16 | (byte)(g * s) << 8 | (byte)(bl * s));
                InsertFragment(p, depth[p], colour, fragDepth, fragColour, fragCount);
            }
        }

        // Blend fragments back to front over the volume render.
        for (int p = 0; p < pixels; p++)
        {
            int n = fragCount[p];
            if (n == 0) continue;
            int o = p * 4;
            double cb = bgra[o], cg = bgra[o + 1], cr = bgra[o + 2];
            for (int k = n - 1; k >= 0; k--)
            {
                uint col = fragColour[p * MaxLayers + k];
                double al = (col >> 24) / 255.0;
                cr = cr * (1 - al) + ((col >> 16) & 0xFF) * al;
                cg = cg * (1 - al) + ((col >> 8) & 0xFF) * al;
                cb = cb * (1 - al) + (col & 0xFF) * al;
            }
            bgra[o] = (byte)cb;
            bgra[o + 1] = (byte)cg;
            bgra[o + 2] = (byte)cr;
            bgra[o + 3] = 255;
        }
    }

    /// <summary>Keeps fragments sorted nearest-first; drops the furthest when full.</summary>
    private static void InsertFragment(int p, float d, uint colour, float[] depths, uint[] colours, byte[] counts)
    {
        int baseIndex = p * MaxLayers;
        int n = counts[p];
        int pos = n;
        while (pos > 0 && depths[baseIndex + pos - 1] > d) pos--;
        if (pos >= MaxLayers) return;
        int last = Math.Min(n, MaxLayers - 1);
        for (int k = last; k > pos; k--)
        {
            depths[baseIndex + k] = depths[baseIndex + k - 1];
            colours[baseIndex + k] = colours[baseIndex + k - 1];
        }
        depths[baseIndex + pos] = d;
        colours[baseIndex + pos] = colour;
        if (n < MaxLayers) counts[p] = (byte)(n + 1);
    }

    private static void RasteriseTriangle((float X, float Y, float D) a, (float X, float Y, float D) b, (float X, float Y, float D) c,
        float light, int size, float[] depth, float[] shade)
    {
        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        int maxX = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        int maxY = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        if (minX > maxX || minY > maxY) return;

        float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (MathF.Abs(area) < 1e-6f) return;
        float inv = 1 / area;

        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float w0 = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) * inv;
                float w1 = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) * inv;
                float w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float d = w0 * a.D + w1 * b.D + w2 * c.D;
                int p = y * size + x;
                if (d < depth[p])
                {
                    depth[p] = d;
                    shade[p] = light;
                }
            }
        }
    }
}
