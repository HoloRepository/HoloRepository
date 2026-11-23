namespace HoloRepository.Core.Imaging;

/// <summary>Descriptive, non-pixel information about a loaded series.</summary>
public sealed record StudyMetadata
{
    public string PatientName { get; init; } = "Anonymous";
    public string PatientId { get; init; } = "UNKNOWN";
    public string Modality { get; init; } = "CT";
    public string StudyDescription { get; init; } = string.Empty;
    public string SeriesDescription { get; init; } = string.Empty;
    public string StudyInstanceUid { get; init; } = string.Empty;
    public string SeriesInstanceUid { get; init; } = string.Empty;
    public DateTime? StudyDate { get; init; }
    public string Source { get; init; } = string.Empty;
    public bool IsSynthetic { get; init; }
}

/// <summary>A volume together with its metadata.</summary>
public sealed record LoadedSeries(StudyMetadata Metadata, Volume Volume);
