namespace HoloRepository.Core.Imaging;

/// <summary>
/// A display window (centre/width) that maps Hounsfield units to 8-bit grey levels.
/// </summary>
public readonly record struct WindowLevel(double Centre, double Width)
{
    public const double MinimumWidth = 1;

    public static WindowLevel Lung { get; } = new(-600, 1500);
    public static WindowLevel Mediastinum { get; } = new(40, 400);
    public static WindowLevel Bone { get; } = new(400, 1800);
    public static WindowLevel Brain { get; } = new(40, 80);
    public static WindowLevel Abdomen { get; } = new(50, 350);

    public static IReadOnlyList<(string Name, WindowLevel Value)> Presets { get; } =
    [
        ("Lung", Lung),
        ("Mediastinum", Mediastinum),
        ("Bone", Bone),
        ("Abdomen", Abdomen),
        ("Brain", Brain),
    ];

    public double Lower => Centre - Width / 2;
    public double Upper => Centre + Width / 2;

    /// <summary>Maps a value to a grey level 0–255.</summary>
    public byte Apply(double value)
    {
        double w = Math.Max(Width, MinimumWidth);
        double t = (value - (Centre - w / 2)) / w;
        if (t <= 0) return 0;
        if (t >= 1) return 255;
        return (byte)(t * 255 + 0.5);
    }

    /// <summary>Returns a new window after a mouse-style drag: horizontal changes width, vertical changes centre.</summary>
    public WindowLevel Adjust(double deltaWidth, double deltaCentre) =>
        new(Centre + deltaCentre, Math.Max(MinimumWidth, Width + deltaWidth));

    /// <summary>Writes grey-scale BGRA pixels (premultiplied, opaque) for the given values.</summary>
    public void ApplyToBgra(ReadOnlySpan<short> values, Span<byte> bgra)
    {
        if (bgra.Length < values.Length * 4)
            throw new ArgumentException("Destination buffer is too small.", nameof(bgra));

        for (int i = 0; i < values.Length; i++)
        {
            byte g = Apply(values[i]);
            int o = i * 4;
            bgra[o] = g;
            bgra[o + 1] = g;
            bgra[o + 2] = g;
            bgra[o + 3] = 255;
        }
    }

    public string Describe() => $"W {Width:0} / L {Centre:0}";
}
