using System.Text.RegularExpressions;

namespace HoloRepository.Core.Notes;

public sealed record ReadabilityResult(int Sentences, int Words, int Syllables, double FleschKincaidGrade, double ReadingAge)
{
    public bool MeetsTarget(double targetAge) => ReadingAge <= targetAge;
}

/// <summary>
/// Estimates reading age with the Flesch–Kincaid grade level (reading age ≈ US grade + 5).
/// Used to check patient summaries against the reading-age-9 requirement in FR-07.
/// </summary>
public static partial class ReadabilityScorer
{
    public const double PatientTargetReadingAge = 9;

    [GeneratedRegex(@"[A-Za-z0-9']+")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"[.!?]+")]
    private static partial Regex SentenceRegex();

    [GeneratedRegex("[aeiouy]+")]
    private static partial Regex VowelGroupRegex();

    public static ReadabilityResult Score(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new ReadabilityResult(0, 0, 0, 0, 5);

        var words = WordRegex().Matches(text).Select(m => m.Value).ToList();
        int sentences = Math.Max(1, SentenceRegex().Matches(text.Trim()).Count);
        int syllables = words.Sum(CountSyllables);
        int wordCount = Math.Max(1, words.Count);

        double grade = 0.39 * ((double)wordCount / sentences) + 11.8 * ((double)syllables / wordCount) - 15.59;
        double age = Math.Max(5, grade + 5);
        return new ReadabilityResult(sentences, words.Count, syllables, Math.Round(grade, 1), Math.Round(age, 1));
    }

    public static int CountSyllables(string word)
    {
        string w = word.ToLowerInvariant().Trim('\'');
        if (w.Length == 0) return 0;
        if (w.All(char.IsDigit)) return 1;
        if (w.Length <= 3) return 1;

        if (w.EndsWith("es") || w.EndsWith("ed")) w = w[..^2];
        else if (w.EndsWith('e') && !w.EndsWith("le")) w = w[..^1];

        int count = VowelGroupRegex().Matches(w).Count;
        return Math.Max(1, count);
    }
}
