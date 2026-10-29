using System.Text.RegularExpressions;
using DriveAlt.Api.Data;

namespace DriveAlt.Api.Files;

/// <summary>Guesses course / type / year / label from file names and Drive folder paths.</summary>
public static partial class Heuristics
{
    public sealed record Guess(Course? Course, string? MaterialType, int? Year, string? Label);

    [GeneratedRegex(@"(?<!\d)(20[12]\d)(?!\d)")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"\b(?:[FSW](?:all|pring|inter)?)['’ ]?(\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex ShortYearPattern();

    [GeneratedRegex(@"\b(midterm|mid|final|quiz|test|exercise|exercises|lab|tutorial|assignment|sheet)[ _\-]*(\d{1,2})?\b", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPattern();

    public static Guess FromPath(string path, IReadOnlyList<Course> courses)
    {
        var p = path.Replace('\\', '/');
        var lower = p.ToLowerInvariant();
        return new Guess(MatchCourse(lower, courses), MaterialType(lower), Year(p), Label(p));
    }

    public static Course? MatchCourse(string lower, IReadOnlyList<Course> courses)
    {
        Course? best = null;
        var bestLen = 0;
        foreach (var c in courses)
        {
            foreach (var name in c.Aliases.Append(c.Name).Append(c.Slug.Replace('-', ' ')))
            {
                var n = name.ToLowerInvariant();
                if (n.Length < 2) continue;
                // short aliases (DS, DB, ML) must be whole tokens
                var hit = n.Length <= 3
                    ? Regex.IsMatch(lower, $@"(?<![a-z0-9]){Regex.Escape(n)}(?![a-z0-9])")
                    : lower.Contains(n);
                if (hit && n.Length > bestLen) { best = c; bestLen = n.Length; }
            }
        }
        return best;
    }

    [GeneratedRegex(@"\b(midterms?|finals?|exams?|mid ?& ?finals?|past ?papers?)\b")]
    private static partial Regex PastPaperWords();

    [GeneratedRegex(@"\b(quiz\w*|tests?)\b|quiz")]
    private static partial Regex QuizWords();

    [GeneratedRegex(@"\blabs?\b|\blab ?\d")]
    private static partial Regex LabWords();

    public static string? MaterialType(string lower)
    {
        lower = lower.Replace('_', ' ').Replace('-', ' ').Replace('.', ' ');
        if (Has(lower, "slide", "lecture", "lec ", "lec_", "seance", "chapter")) return MaterialTypes.Slides;
        if (PastPaperWords().IsMatch(lower)) return MaterialTypes.PastPaper;
        if (QuizWords().IsMatch(lower)) return MaterialTypes.Quiz;
        if (LabWords().IsMatch(lower)) return MaterialTypes.Lab;
        if (Has(lower, "exercise", "excercise", "excercice", "tutorial", "problems", "practice", "assignment", "worksheet", "sheet")) return MaterialTypes.Exercise;
        if (Has(lower, "notes", "summary", "cheat sheet", "cheat-sheet", "formulas", "book", "handbook")) return MaterialTypes.Notes;
        return null;
    }

    public static int? Year(string s)
    {
        var m = YearPattern().Matches(s);
        if (m.Count > 0) return int.Parse(m[^1].Groups[1].Value);
        var sm = ShortYearPattern().Match(s);
        return sm.Success ? 2000 + int.Parse(sm.Groups[1].Value) : null;
    }

    public static string? Label(string s)
    {
        var m = LabelPattern().Matches(Path.GetFileNameWithoutExtension(s) + " " + s);
        foreach (Match x in m)
        {
            if (!x.Groups[2].Success) continue;
            var word = x.Groups[1].Value.ToLowerInvariant() switch
            {
                "mid" or "midterm" => "Midterm",
                "final" => "Final",
                "quiz" or "test" => "Quiz",
                "lab" => "Lab",
                "tutorial" => "Tutorial",
                "assignment" => "Assignment",
                _ => "Exercise",
            };
            return $"{word} {int.Parse(x.Groups[2].Value)}";
        }
        return null;
    }

    private static bool Has(string s, params string[] needles) => needles.Any(s.Contains);

    /// <summary>Phone/chat photos ("WhatsApp Image…", "IMG_1234") — almost always photos of assessments.</summary>
    public static bool LooksLikePhoto(string name) =>
        name.StartsWith("whatsapp", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("img", StringComparison.OrdinalIgnoreCase) ||
        Guid.TryParse(Path.GetFileNameWithoutExtension(name), out _);

    public static string TitleFromName(string name)
    {
        var t = Path.GetFileNameWithoutExtension(name).Replace('_', ' ').Trim();
        if (t.StartsWith("Copy of ", StringComparison.OrdinalIgnoreCase)) t = t[8..];
        return string.IsNullOrWhiteSpace(t) ? name : t;
    }

    [GeneratedRegex(@"\.(?:pdf|pptx?|docx?|jpe?g|png|webp|gif|heic|heif)$", RegexOptions.IgnoreCase)]
    private static partial Regex KnownExtensionPattern();

    [GeneratedRegex(@"(?:\s*[-–—]?\s*copy(?:\s*\(\d+\))?)+$", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitCopySuffixPattern();

    [GeneratedRegex(@"^(?<stem>.*?)\s*\(\d+\)$")]
    private static partial Regex TrailingParenNumberPattern();

    [GeneratedRegex(@"\b(?:midterm|mid|final|quiz|test|exercise|exercises|lab|tutorial|assignment|sheet|part|ch|chapter|lec|lecture|week|section|sec|hw|homework|paper)$", RegexOptions.IgnoreCase)]
    private static partial Regex AssessmentOrPartTailPattern();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlnumPattern();

    /// <summary>Normalized title key for fuzzy duplicate detection (ignores copy suffixes, punctuation and case).</summary>
    public static string CanonicalTitle(string nameOrTitle)
    {
        var leaf = nameOrTitle.Replace('\\', '/').Split('/')[^1].Trim();
        if (LooksLikePhoto(leaf)) return "";
        var baseName = KnownExtensionPattern().Replace(leaf, "");
        var t = baseName.Replace('_', ' ').Trim();
        while (t.StartsWith("Copy of ", StringComparison.OrdinalIgnoreCase)) t = t[8..].TrimStart();
        t = ExplicitCopySuffixPattern().Replace(t, "").Trim();
        if (TrailingParenNumberPattern().Match(t) is { Success: true } m)
        {
            var stem = m.Groups["stem"].Value.Trim();
            if (stem.Length > 0 && !AssessmentOrPartTailPattern().IsMatch(stem))
                t = stem;
        }
        t = ExplicitCopySuffixPattern().Replace(t, "").Trim();
        var norm = NonAlnumPattern().Replace(t.ToLowerInvariant(), " ").Trim();
        return norm.Length < 4 ? "" : norm;
    }
}
