using System.Text.RegularExpressions;

namespace HoloRepository.Core.Notes;

public enum ConceptCategory
{
    Finding,
    Disorder,
    Procedure,
    History,
}

public sealed record SnomedConcept(string ConceptId, string PreferredTerm, ConceptCategory Category, string MatchedText)
{
    public string BrowserUrl => $"https://termbrowser.nhs.uk/?perspective=full&conceptId1={ConceptId}";
}

public sealed record GuidelineReference(string Code, string Title, string Rationale)
{
    public string Url => $"https://www.nice.org.uk/guidance/{Code.ToLowerInvariant()}";
}

/// <summary>
/// Offline SNOMED CT grounding and NICE guideline linking (FR-09).
/// The concept table is a small illustrative subset for the prototype; codes must be verified against the
/// NHS SNOMED CT browser, and live NICE verification belongs to the clinical agent hub.
/// </summary>
public static class ClinicalTerminology
{
    private sealed record Entry(string ConceptId, string Term, ConceptCategory Category, string[] Synonyms, string? Guideline);

    private static readonly Entry[] Entries =
    [
        new("427359005", "Solitary nodule of lung", ConceptCategory.Finding, ["lung nodule", "pulmonary nodule", "nodule"], "NG122"),
        new("363358000", "Malignant tumour of lung", ConceptCategory.Disorder, ["lung cancer", "malignant tumour of lung", "lung malignancy"], "NG122"),
        new("30746006", "Lymphadenopathy", ConceptCategory.Finding, ["lymphadenopathy", "lymph node enlargement"], "NG122"),
        new("60046008", "Pleural effusion", ConceptCategory.Finding, ["pleural effusion"], null),
        new("59282003", "Pulmonary embolism", ConceptCategory.Disorder, ["pulmonary embolism", "pulmonary embolus"], "NG158"),
        new("233604007", "Pneumonia", ConceptCategory.Disorder, ["pneumonia"], "CG191"),
        new("13645005", "Chronic obstructive lung disease", ConceptCategory.History, ["copd", "chronic obstructive"], "NG115"),
        new("38341003", "Hypertensive disorder", ConceptCategory.History, ["hypertension", "high blood pressure"], "NG136"),
        new("44054006", "Diabetes mellitus type 2", ConceptCategory.History, ["type 2 diabetes", "t2dm"], "NG28"),
        new("8517006", "Ex-smoker", ConceptCategory.History, ["ex-smoker", "former smoker", "stopped smoking"], "NG209"),
        new("77477000", "Computed tomography", ConceptCategory.Procedure, ["ct thorax", "ct scan", "ct guided"], null),
        new("82918005", "Positron emission tomography", ConceptCategory.Procedure, ["pet-ct", "pet ct", "pet scan"], "NG122"),
        new("86273004", "Biopsy", ConceptCategory.Procedure, ["biopsy"], "NG122"),
        new("173171007", "Lobectomy of lung", ConceptCategory.Procedure, ["lobectomy"], "NG122"),
    ];

    private static readonly Dictionary<string, (string Title, string Rationale)> Guidelines = new()
    {
        ["NG122"] = ("Lung cancer: diagnosis and management", "Investigation, staging and treatment of suspected lung cancer and nodules."),
        ["NG136"] = ("Hypertension in adults: diagnosis and management", "Comorbidity relevant to fitness for surgery."),
        ["NG28"] = ("Type 2 diabetes in adults: management", "Comorbidity relevant to peri-operative planning."),
        ["NG115"] = ("Chronic obstructive pulmonary disease in over 16s: diagnosis and management", "Lung function affects surgical options."),
        ["NG158"] = ("Venous thromboembolic diseases: diagnosis, management and thrombophilia testing", "Management of suspected pulmonary embolism."),
        ["CG191"] = ("Pneumonia in adults: diagnosis and management", "Management of community-acquired pneumonia."),
        ["NG209"] = ("Tobacco: preventing uptake, promoting quitting and treating dependence", "Smoking history and cessation support."),
    };

    public static int ConceptCount => Entries.Length;

    /// <summary>Finds concepts mentioned in free text. Each concept is returned once, longest synonym wins.</summary>
    public static IReadOnlyList<SnomedConcept> Ground(string text)
    {
        var found = new List<SnomedConcept>();
        if (string.IsNullOrWhiteSpace(text)) return found;

        foreach (var e in Entries)
        {
            foreach (var syn in e.Synonyms.OrderByDescending(s => s.Length))
            {
                var m = Regex.Match(text, $@"(?<![\w-]){Regex.Escape(syn)}(?![\w-])", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    found.Add(new SnomedConcept(e.ConceptId, e.Term, e.Category, m.Value));
                    break;
                }
            }
        }
        return found;
    }

    public static IReadOnlyList<GuidelineReference> GuidelinesFor(IEnumerable<SnomedConcept> concepts)
    {
        var codes = concepts
            .Select(c => Entries.First(e => e.ConceptId == c.ConceptId).Guideline)
            .Where(g => g is not null)
            .Distinct();
        return codes.Select(c => new GuidelineReference(c!, Guidelines[c!].Title, Guidelines[c!].Rationale)).ToList();
    }
}
