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

    public static string? MaterialType(string lower)
    {
        if (Has(lower, "slide", "lecture", "lec ", "lec_", "seance", "chapter")) return MaterialTypes.Slides;
        if (Has(lower, "past paper", "past_paper", "midterm", "final", "exam", "mid & final", "mid&final")) return MaterialTypes.PastPaper;
        if (Has(lower, "quiz", "quizzes", "test")) return MaterialTypes.Quiz;
        if (Has(lower, "lab")) return MaterialTypes.Lab;
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
}
