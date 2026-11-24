using System.Buffers.Binary;
using System.IO.Compression;

namespace HoloRepository.Core.Imaging;

/// <summary>An RGB colour with an opacity used when drawing structures.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, double Opacity = 1)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>A triangle mesh. Vertices are x,y,z triplets; indices are triangle corner triplets.</summary>
public sealed class SurfaceMesh
{
    public SurfaceMesh(float[] vertices, int[] indices)
    {
        if (vertices.Length % 3 != 0) throw new ArgumentException("Vertex array length must be a multiple of 3.", nameof(vertices));
        if (indices.Length % 3 != 0) throw new ArgumentException("Index array length must be a multiple of 3.", nameof(indices));
        int count = vertices.Length / 3;
        foreach (int i in indices)
            if (i < 0 || i >= count) throw new InvalidDataException("Mesh index out of range.");
        Vertices = vertices;
        Indices = indices;
    }

    public float[] Vertices { get; }
    public int[] Indices { get; }
    public int VertexCount => Vertices.Length / 3;
    public int TriangleCount => Indices.Length / 3;

    public Point3 Vertex(int i) => new(Vertices[i * 3], Vertices[i * 3 + 1], Vertices[i * 3 + 2]);

    /// <summary>Returns a copy with every vertex transformed (e.g. RAS → volume space).</summary>
    public SurfaceMesh Transform(Func<Point3, Point3> map)
    {
        var v = new float[Vertices.Length];
        for (int i = 0; i < VertexCount; i++)
        {
            var p = map(Vertex(i));
            v[i * 3] = (float)p.X;
            v[i * 3 + 1] = (float)p.Y;
            v[i * 3 + 2] = (float)p.Z;
        }
        return new SurfaceMesh(v, (int[])Indices.Clone());
    }

    public (Point3 Min, Point3 Max) Bounds()
    {
        if (VertexCount == 0) return (default, default);
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < Vertices.Length; i += 3)
        {
            minX = Math.Min(minX, Vertices[i]); maxX = Math.Max(maxX, Vertices[i]);
            minY = Math.Min(minY, Vertices[i + 1]); maxY = Math.Max(maxY, Vertices[i + 1]);
            minZ = Math.Min(minZ, Vertices[i + 2]); maxZ = Math.Max(maxZ, Vertices[i + 2]);
        }
        return (new Point3(minX, minY, minZ), new Point3(maxX, maxY, maxZ));
    }
}

/// <summary>
/// Reader/writer for the MZ3 mesh format (Surfice / NiiVue) produced by the HoloRepository server.
/// Layout: optional gzip; uint16 magic 0x5A4D; uint16 attributes (1 faces, 2 vertices, 4 RGBA, 8 scalars, 16 double scalars);
/// uint32 face count; uint32 vertex count; uint32 bytes to skip; then faces (int32×3), vertices (float32×3), colours and scalars.
/// </summary>
public static class Mz3
{
    private const ushort Magic = 0x5A4D;
    private const ushort HasFaces = 1, HasVertices = 2;

    public static SurfaceMesh Read(Stream stream)
    {
        byte[] bytes = NiftiImage.ReadAll(stream);
        if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b) bytes = NiftiImage.Gunzip(bytes);
        if (bytes.Length < 16 || BinaryPrimitives.ReadUInt16LittleEndian(bytes) != Magic)
            throw new InvalidDataException("Not an MZ3 mesh.");

        ushort attr = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));
        int faces = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        int verts = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
        int skip = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)));
        if ((attr & HasFaces) == 0 || (attr & HasVertices) == 0)
            throw new InvalidDataException("MZ3 file does not contain both faces and vertices.");

        int offset = 16 + skip;
        long needed = offset + (long)faces * 12 + (long)verts * 12;
        if (needed > bytes.Length) throw new InvalidDataException("MZ3 mesh is truncated.");

        var indices = new int[faces * 3];
        for (int i = 0; i < indices.Length; i++, offset += 4)
            indices[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
        var vertices = new float[verts * 3];
        for (int i = 0; i < vertices.Length; i++, offset += 4)
            vertices[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset));
        return new SurfaceMesh(vertices, indices);
    }

    public static SurfaceMesh Load(string path)
    {
        using var s = File.OpenRead(path);
        return Read(s);
    }

    public static void Write(Stream stream, SurfaceMesh mesh, bool gzip = true)
    {
        using var ms = new MemoryStream();
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(header, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], HasFaces | HasVertices);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)mesh.TriangleCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)mesh.VertexCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], 0);
        ms.Write(header);
        Span<byte> four = stackalloc byte[4];
        foreach (int i in mesh.Indices) { BinaryPrimitives.WriteInt32LittleEndian(four, i); ms.Write(four); }
        foreach (float v in mesh.Vertices) { BinaryPrimitives.WriteSingleLittleEndian(four, v); ms.Write(four); }

        if (gzip)
        {
            using var gz = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true);
            ms.Position = 0;
            ms.CopyTo(gz);
        }
        else
        {
            ms.Position = 0;
            ms.CopyTo(stream);
        }
    }
}

/// <summary>
/// Builds a smooth surface around one label of a label volume: boundary faces between inside and outside
/// blocks, followed by Taubin smoothing (the same smoothing the server's meshing step uses).
/// Vertices are in volume millimetres.
/// </summary>
public static class SurfaceExtractor
{
    public static SurfaceMesh? Extract(Volume geometry, byte[] labels, byte label, int step = 2, int smoothingIterations = 12)
    {
        step = Math.Max(1, step);
        int bw = (geometry.Width + step - 1) / step, bh = (geometry.Height + step - 1) / step, bd = (geometry.Depth + step - 1) / step;
        var inside = new bool[bw * bh * bd];
        bool any = false;
        for (int bz = 0; bz < bd; bz++)
        for (int by = 0; by < bh; by++)
        for (int bx = 0; bx < bw; bx++)
        {
            int count = 0, total = 0;
            for (int z = bz * step; z < Math.Min(geometry.Depth, (bz + 1) * step); z++)
            for (int y = by * step; y < Math.Min(geometry.Height, (by + 1) * step); y++)
            for (int x = bx * step; x < Math.Min(geometry.Width, (bx + 1) * step); x++)
            {
                total++;
                if (labels[geometry.IndexOf(x, y, z)] == label) count++;
            }
            if (count * 2 >= total && count > 0)
            {
                inside[(bz * bh + by) * bw + bx] = true;
                any = true;
            }
        }
        if (!any) return null;

        bool In(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < bw && y < bh && z < bd && inside[(z * bh + y) * bw + x];

        var vertexIndex = new Dictionary<long, int>();
        var verts = new List<float>();
        var tris = new List<int>();
        double sx = geometry.Spacing.X * step, sy = geometry.Spacing.Y * step, sz = geometry.Spacing.Z * step;

        int Corner(int x, int y, int z)
        {
            long key = ((long)z * (bh + 1) + y) * (bw + 1) + x;
            if (vertexIndex.TryGetValue(key, out int idx)) return idx;
            idx = verts.Count / 3;
            verts.Add((float)(x * sx));
            verts.Add((float)(y * sy));
            verts.Add((float)(z * sz));
            vertexIndex[key] = idx;
            return idx;
        }

        void Quad(int a, int b, int c, int d)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(a); tris.Add(c); tris.Add(d);
        }

        for (int z = 0; z < bd; z++)
        for (int y = 0; y < bh; y++)
        for (int x = 0; x < bw; x++)
        {
            if (!In(x, y, z)) continue;
            if (!In(x - 1, y, z)) Quad(Corner(x, y, z), Corner(x, y, z + 1), Corner(x, y + 1, z + 1), Corner(x, y + 1, z));
            if (!In(x + 1, y, z)) Quad(Corner(x + 1, y, z), Corner(x + 1, y + 1, z), Corner(x + 1, y + 1, z + 1), Corner(x + 1, y, z + 1));
            if (!In(x, y - 1, z)) Quad(Corner(x, y, z), Corner(x + 1, y, z), Corner(x + 1, y, z + 1), Corner(x, y, z + 1));
            if (!In(x, y + 1, z)) Quad(Corner(x, y + 1, z), Corner(x, y + 1, z + 1), Corner(x + 1, y + 1, z + 1), Corner(x + 1, y + 1, z));
            if (!In(x, y, z - 1)) Quad(Corner(x, y, z), Corner(x, y + 1, z), Corner(x + 1, y + 1, z), Corner(x + 1, y, z));
            if (!In(x, y, z + 1)) Quad(Corner(x, y, z + 1), Corner(x + 1, y, z + 1), Corner(x + 1, y + 1, z + 1), Corner(x, y + 1, z + 1));
        }

        var vertices = verts.ToArray();
        var indices = tris.ToArray();
        TaubinSmooth(vertices, indices, smoothingIterations);
        return new SurfaceMesh(vertices, indices);
    }

    /// <summary>Taubin λ/μ smoothing: shrink-free smoothing that removes the staircase look of voxel surfaces.</summary>
    public static void TaubinSmooth(float[] vertices, int[] indices, int iterations, double lambda = 0.5, double mu = -0.53)
    {
        int n = vertices.Length / 3;
        var neighbours = new HashSet<int>[n];
        for (int i = 0; i < n; i++) neighbours[i] = new HashSet<int>();
        for (int t = 0; t < indices.Length; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            neighbours[a].Add(b); neighbours[a].Add(c);
            neighbours[b].Add(a); neighbours[b].Add(c);
            neighbours[c].Add(a); neighbours[c].Add(b);
        }
        var adjacency = neighbours.Select(s => s.ToArray()).ToArray();
        var next = new float[vertices.Length];

        for (int it = 0; it < iterations * 2; it++)
        {
            double factor = it % 2 == 0 ? lambda : mu;
            for (int i = 0; i < n; i++)
            {
                var adj = adjacency[i];
                if (adj.Length == 0)
                {
                    next[i * 3] = vertices[i * 3]; next[i * 3 + 1] = vertices[i * 3 + 1]; next[i * 3 + 2] = vertices[i * 3 + 2];
                    continue;
                }
                double ax = 0, ay = 0, az = 0;
                foreach (int j in adj) { ax += vertices[j * 3]; ay += vertices[j * 3 + 1]; az += vertices[j * 3 + 2]; }
                ax /= adj.Length; ay /= adj.Length; az /= adj.Length;
                next[i * 3] = (float)(vertices[i * 3] + factor * (ax - vertices[i * 3]));
                next[i * 3 + 1] = (float)(vertices[i * 3 + 1] + factor * (ay - vertices[i * 3 + 1]));
                next[i * 3 + 2] = (float)(vertices[i * 3 + 2] + factor * (az - vertices[i * 3 + 2]));
            }
            Array.Copy(next, vertices, vertices.Length);
        }
    }
}
