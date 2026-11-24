using System.Buffers.Binary;
using System.IO.Compression;

namespace HoloRepository.Core.Imaging;

/// <summary>
/// A NIfTI-1 image (.nii or .nii.gz), as returned by the HoloRepository server for segmentations.
/// <see cref="Affine"/> maps voxel indices (i, j, k) to RAS millimetres, row-major 3×4.
/// </summary>
public sealed class NiftiImage
{
    private NiftiImage(int nx, int ny, int nz, float[] data, double[] affine, (double X, double Y, double Z) pixdim)
    {
        Nx = nx;
        Ny = ny;
        Nz = nz;
        Data = data;
        Affine = affine;
        PixelDimensions = pixdim;
    }

    public int Nx { get; }
    public int Ny { get; }
    public int Nz { get; }

    /// <summary>Scaled voxel values, i fastest, then j, then k.</summary>
    public float[] Data { get; }

    /// <summary>Row-major 3×4 voxel → RAS matrix.</summary>
    public double[] Affine { get; }

    public (double X, double Y, double Z) PixelDimensions { get; }

    public float this[int i, int j, int k] => Data[(k * Ny + j) * Nx + i];

    public Point3 VoxelToRas(double i, double j, double k)
    {
        var a = Affine;
        return new Point3(
            a[0] * i + a[1] * j + a[2] * k + a[3],
            a[4] * i + a[5] * j + a[6] * k + a[7],
            a[8] * i + a[9] * j + a[10] * k + a[11]);
    }

    public (double I, double J, double K) RasToVoxel(Point3 ras)
    {
        // Invert the 3×3 part (general inverse, so sheared or oblique affines still work).
        var a = Affine;
        double x = ras.X - a[3], y = ras.Y - a[7], z = ras.Z - a[11];
        double m00 = a[0], m01 = a[1], m02 = a[2], m10 = a[4], m11 = a[5], m12 = a[6], m20 = a[8], m21 = a[9], m22 = a[10];
        double det = m00 * (m11 * m22 - m12 * m21) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20);
        if (Math.Abs(det) < 1e-12) throw new InvalidDataException("NIfTI affine is singular.");
        double i = ((m11 * m22 - m12 * m21) * x - (m01 * m22 - m02 * m21) * y + (m01 * m12 - m02 * m11) * z) / det;
        double j = (-(m10 * m22 - m12 * m20) * x + (m00 * m22 - m02 * m20) * y - (m00 * m12 - m02 * m10) * z) / det;
        double k = ((m10 * m21 - m11 * m20) * x - (m00 * m21 - m01 * m20) * y + (m00 * m11 - m01 * m10) * z) / det;
        return (i, j, k);
    }

    public static NiftiImage Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads a NIfTI-1 single-file image, gzip-compressed or not.</summary>
    public static NiftiImage Read(Stream stream)
    {
        byte[] bytes = ReadAll(stream);
        if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b) bytes = Gunzip(bytes);
        if (bytes.Length < 352) throw new InvalidDataException("File is too small to be a NIfTI image.");

        bool little = BinaryPrimitives.ReadInt32LittleEndian(bytes) == 348;
        if (!little && BinaryPrimitives.ReadInt32BigEndian(bytes) != 348)
            throw new InvalidDataException("Not a NIfTI-1 file (sizeof_hdr is not 348).");

        short I16(int o) => little ? BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(o)) : BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(o));
        float F32(int o) => little ? BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(o)) : BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(o));

        int dims = I16(40);
        int nx = I16(42), ny = dims >= 2 ? I16(44) : 1, nz = dims >= 3 ? I16(46) : 1;
        if (nx <= 0 || ny <= 0 || nz <= 0) throw new InvalidDataException("NIfTI image has invalid dimensions.");
        short datatype = I16(70);
        float qfac = F32(76) < 0 ? -1 : 1;
        double px = F32(80), py = F32(84), pz = F32(88);
        int offset = (int)F32(108);
        float slope = F32(112), inter = F32(116);
        if (slope == 0 || float.IsNaN(slope)) { slope = 1; inter = 0; }
        if (float.IsNaN(inter)) inter = 0;
        short qformCode = I16(252), sformCode = I16(254);

        double[] affine;
        if (sformCode > 0)
        {
            affine = new double[12];
            for (int r = 0; r < 3; r++)
            for (int c = 0; c < 4; c++)
                affine[r * 4 + c] = F32(280 + r * 16 + c * 4);
        }
        else if (qformCode > 0)
        {
            affine = QuaternionAffine(F32(256), F32(260), F32(264), F32(268), F32(272), F32(276), px, py, pz, qfac);
        }
        else
        {
            affine = [px == 0 ? 1 : px, 0, 0, 0, 0, py == 0 ? 1 : py, 0, 0, 0, 0, pz == 0 ? 1 : pz, 0];
        }

        long count = (long)nx * ny * nz;
        int size = datatype switch
        {
            2 or 256 => 1,
            4 or 512 => 2,
            8 or 16 or 768 => 4,
            64 => 8,
            _ => throw new NotSupportedException($"NIfTI datatype {datatype} is not supported."),
        };
        if (offset < 348 || offset + count * size > bytes.Length)
            throw new InvalidDataException("NIfTI voxel data is truncated.");

        var data = new float[count];
        var span = bytes.AsSpan(offset);
        for (long n = 0; n < count; n++)
        {
            int o = (int)(n * size);
            double raw = datatype switch
            {
                2 => span[o],
                256 => (sbyte)span[o],
                4 => little ? BinaryPrimitives.ReadInt16LittleEndian(span[o..]) : BinaryPrimitives.ReadInt16BigEndian(span[o..]),
                512 => little ? BinaryPrimitives.ReadUInt16LittleEndian(span[o..]) : BinaryPrimitives.ReadUInt16BigEndian(span[o..]),
                8 => little ? BinaryPrimitives.ReadInt32LittleEndian(span[o..]) : BinaryPrimitives.ReadInt32BigEndian(span[o..]),
                768 => little ? BinaryPrimitives.ReadUInt32LittleEndian(span[o..]) : BinaryPrimitives.ReadUInt32BigEndian(span[o..]),
                16 => little ? BinaryPrimitives.ReadSingleLittleEndian(span[o..]) : BinaryPrimitives.ReadSingleBigEndian(span[o..]),
                _ => little ? BinaryPrimitives.ReadDoubleLittleEndian(span[o..]) : BinaryPrimitives.ReadDoubleBigEndian(span[o..]),
            };
            data[n] = (float)(raw * slope + inter);
        }

        return new NiftiImage(nx, ny, nz, data, affine, (Math.Abs(px), Math.Abs(py), Math.Abs(pz)));
    }

    /// <summary>Writes an uncompressed NIfTI-1 float32 or uint8 image with an sform. Used for tests and exports.</summary>
    public static void Write(Stream stream, int nx, int ny, int nz, float[] data, double[] affine, bool asLabels = false)
    {
        var header = new byte[352];
        var h = header.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(h, 348);
        BinaryPrimitives.WriteInt16LittleEndian(h[40..], 3);
        BinaryPrimitives.WriteInt16LittleEndian(h[42..], (short)nx);
        BinaryPrimitives.WriteInt16LittleEndian(h[44..], (short)ny);
        BinaryPrimitives.WriteInt16LittleEndian(h[46..], (short)nz);
        BinaryPrimitives.WriteInt16LittleEndian(h[48..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(h[70..], (short)(asLabels ? 2 : 16));
        BinaryPrimitives.WriteInt16LittleEndian(h[72..], (short)(asLabels ? 8 : 32));
        BinaryPrimitives.WriteSingleLittleEndian(h[76..], 1);
        for (int c = 0; c < 3; c++)
        {
            double len = Math.Sqrt(affine[c] * affine[c] + affine[4 + c] * affine[4 + c] + affine[8 + c] * affine[8 + c]);
            BinaryPrimitives.WriteSingleLittleEndian(h[(80 + c * 4)..], (float)len);
        }
        BinaryPrimitives.WriteSingleLittleEndian(h[108..], 352);
        BinaryPrimitives.WriteSingleLittleEndian(h[112..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(h[254..], 2);
        for (int i = 0; i < 12; i++) BinaryPrimitives.WriteSingleLittleEndian(h[(280 + i * 4)..], (float)affine[i]);
        "n+1\0"u8.CopyTo(h[344..]);
        stream.Write(header);

        var buffer = new byte[data.Length * (asLabels ? 1 : 4)];
        for (int n = 0; n < data.Length; n++)
        {
            if (asLabels) buffer[n] = (byte)Math.Clamp(data[n], 0, 255);
            else BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(n * 4), data[n]);
        }
        stream.Write(buffer);
    }

    /// <summary>
    /// Converts to a viewable volume, re-ordering axes so x runs to patient left, y to posterior and z to superior
    /// (the same orientation as DICOM axial series), which keeps anatomy the right way up in the viewer.
    /// </summary>
    public LoadedSeries ToSeries(StudyMetadata metadata)
    {
        // Direction of each voxel axis in LPS.
        var dirs = new Point3[3];
        var lengths = new double[3];
        for (int c = 0; c < 3; c++)
        {
            var ras = new Point3(Affine[c], Affine[4 + c], Affine[8 + c]);
            var lps = PatientFrame.RasToLps(ras);
            lengths[c] = Math.Sqrt(lps.X * lps.X + lps.Y * lps.Y + lps.Z * lps.Z);
            dirs[c] = lengths[c] == 0 ? new Point3(c == 0 ? 1 : 0, c == 1 ? 1 : 0, c == 2 ? 1 : 0)
                                      : new Point3(lps.X / lengths[c], lps.Y / lengths[c], lps.Z / lengths[c]);
        }

        // For each output axis (L, P, S) pick the voxel axis that points most along it, and whether to flip it.
        var used = new bool[3];
        var source = new int[3];
        var flip = new bool[3];
        for (int outAxis = 0; outAxis < 3; outAxis++)
        {
            int best = -1;
            double bestDot = -1;
            for (int c = 0; c < 3; c++)
            {
                if (used[c]) continue;
                double dot = Math.Abs(Component(dirs[c], outAxis));
                if (dot > bestDot) { bestDot = dot; best = c; }
            }
            used[best] = true;
            source[outAxis] = best;
            flip[outAxis] = Component(dirs[best], outAxis) < 0;
        }

        int[] n = [Nx, Ny, Nz];
        int w = n[source[0]], h = n[source[1]], d = n[source[2]];
        var spacing = new VoxelSpacing(Len(source[0]), Len(source[1]), Len(source[2]));
        var volume = new short[(long)w * h * d];
        var idx = new int[3];
        for (int z = 0; z < d; z++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            idx[source[0]] = flip[0] ? w - 1 - x : x;
            idx[source[1]] = flip[1] ? h - 1 - y : y;
            idx[source[2]] = flip[2] ? d - 1 - z : z;
            float v = this[idx[0], idx[1], idx[2]];
            volume[((long)z * h + y) * w + x] = (short)Math.Clamp(MathF.Round(v), short.MinValue, short.MaxValue);
        }

        // Origin = patient position of output voxel (0,0,0).
        var originIdx = new double[3];
        for (int a = 0; a < 3; a++) originIdx[source[a]] = flip[a] ? n[source[a]] - 1 : 0;
        var origin = PatientFrame.RasToLps(VoxelToRas(originIdx[0], originIdx[1], originIdx[2]));
        Point3 Axis(int a) => flip[a] ? Neg(dirs[source[a]]) : dirs[source[a]];

        var vol = new Volume(w, h, d, spacing, volume)
        {
            Frame = new PatientFrame(origin, Axis(0), Axis(1), Axis(2)),
        };
        return new LoadedSeries(metadata, vol);

        double Len(int c) => lengths[c] > 0 ? lengths[c] : 1;
    }

    /// <summary>
    /// Samples this image (typically a label map) at every voxel of <paramref name="target"/> using patient coordinates.
    /// Values outside the image become 0.
    /// </summary>
    public byte[] ResampleLabelsTo(Volume target)
    {
        var labels = new byte[(long)target.Width * target.Height * target.Depth];
        Parallel.For(0, target.Depth, z =>
        {
            for (int y = 0; y < target.Height; y++)
            for (int x = 0; x < target.Width; x++)
            {
                var ras = PatientFrame.LpsToRas(target.ToPatient(target.VoxelToMm(x, y, z)));
                var (i, j, k) = RasToVoxel(ras);
                int ii = (int)Math.Round(i), jj = (int)Math.Round(j), kk = (int)Math.Round(k);
                if (ii < 0 || jj < 0 || kk < 0 || ii >= Nx || jj >= Ny || kk >= Nz) continue;
                float v = this[ii, jj, kk];
                if (v > 0) labels[target.IndexOf(x, y, z)] = (byte)Math.Clamp(MathF.Round(v), 0, 255);
            }
        });
        return labels;
    }

    private static double Component(Point3 p, int axis) => axis switch { 0 => p.X, 1 => p.Y, _ => p.Z };

    private static Point3 Neg(Point3 p) => new(-p.X, -p.Y, -p.Z);

    private static double[] QuaternionAffine(double b, double c, double d, double qx, double qy, double qz,
        double px, double py, double pz, double qfac)
    {
        double a = 1 - (b * b + c * c + d * d);
        a = a < 1e-7 ? 0 : Math.Sqrt(a);
        double[,] r =
        {
            { a * a + b * b - c * c - d * d, 2 * (b * c - a * d), 2 * (b * d + a * c) },
            { 2 * (b * c + a * d), a * a + c * c - b * b - d * d, 2 * (c * d - a * b) },
            { 2 * (b * d - a * c), 2 * (c * d + a * b), a * a + d * d - c * c - b * b },
        };
        px = px == 0 ? 1 : px;
        py = py == 0 ? 1 : py;
        pz = (pz == 0 ? 1 : pz) * qfac;
        return
        [
            r[0, 0] * px, r[0, 1] * py, r[0, 2] * pz, qx,
            r[1, 0] * px, r[1, 1] * py, r[1, 2] * pz, qy,
            r[2, 0] * px, r[2, 1] * py, r[2, 2] * pz, qz,
        ];
    }

    internal static byte[] ReadAll(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    internal static byte[] Gunzip(byte[] bytes)
    {
        using var input = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        return ReadAll(input);
    }
}
