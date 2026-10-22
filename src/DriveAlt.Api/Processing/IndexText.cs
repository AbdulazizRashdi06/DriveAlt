using DriveAlt.Api.Data;

namespace DriveAlt.Api.Processing;

/// <summary>
/// Builds the text that goes into full-text search and embeddings. Answer-region text is never included,
/// so neither search nor the bot can surface it.
/// </summary>
public static class IndexText
{
    public static string TypeLabel(string type) => type switch
    {
        MaterialTypes.Slides => "slides lecture",
        MaterialTypes.PastPaper => "past paper exam midterm final",
        MaterialTypes.Quiz => "quiz",
        MaterialTypes.Exercise => "exercise tutorial sheet",
        MaterialTypes.Lab => "lab",
        MaterialTypes.Notes => "notes summary",
        _ => "",
    };

    private static string FileMeta(DocFile f) => Join(
        f.Course?.Name, string.Join(' ', f.Course?.Aliases ?? []), f.Title, f.AssessmentLabel, f.Folder,
        TypeLabel(f.MaterialType), f.AcademicYear?.ToString());

    public static string ForPage(DocFile f, Page p) => Join(
        FileMeta(f), p.Topic, string.Join(' ', p.Tags), p.Text,
        string.Join(' ', p.Regions.Where(r => r.Label != RegionLabels.Answer).Select(r => r.Text)));

    public static string ForRegion(DocFile f, Page p, Region r) => r.Label == RegionLabels.Answer
        ? ""
        : Join(FileMeta(f), r.QuestionNumber is { } q ? $"question {q} Q{q}" : null, p.Topic, string.Join(' ', p.Tags), r.Text);

    public static string EmbedPage(DocFile f, Page p) => Clip(Join(
        f.Course?.Name, f.Title, f.AssessmentLabel, TypeLabel(f.MaterialType), p.Topic, string.Join(", ", p.Tags), p.Text), 6000);

    public static string EmbedRegion(DocFile f, Page p, Region r) => Clip(Join(
        f.Course?.Name, f.AssessmentLabel, p.Topic, string.Join(", ", p.Tags), r.Text), 3000);

    private static string Join(params string?[] parts) =>
        string.Join(' ', parts.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    public static void Refresh(DocFile f, Page p)
    {
        p.SearchText = Clip(ForPage(f, p), 200_000);
        foreach (var r in p.Regions) r.SearchText = ForRegion(f, p, r);
    }
}
