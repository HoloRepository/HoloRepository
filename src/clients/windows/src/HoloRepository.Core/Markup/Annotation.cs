using HoloRepository.Core.Imaging;

namespace HoloRepository.Core.Markup;

public enum AnnotationKind
{
    Pin,
    Caliper,
    Callout,
}

/// <summary>
/// A spatial markup item in patient space (millimetres), so it can be shown on any plane, in 3D and on
/// any connected client regardless of its viewport (FR-06).
/// </summary>
public sealed record Annotation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string SeriesId { get; init; }
    public required AnnotationKind Kind { get; init; }
    public required Point3 Start { get; init; }
    public Point3? End { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Author { get; init; } = "Local user";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool IsRemote { get; init; }

    public double? LengthMm => Kind == AnnotationKind.Caliper && End is Point3 e ? Start.DistanceTo(e) : null;

    public string Summary => Kind switch
    {
        AnnotationKind.Caliper when LengthMm is double l => $"{DisplayLabel}: {l:0.0} mm",
        _ => DisplayLabel,
    };

    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Kind.ToString() : Label;
}
