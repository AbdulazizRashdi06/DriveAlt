using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DriveAlt.Api.Data;

namespace DriveAlt.Api.Ai;

public sealed record ExtractedRegion(string Label, float X, float Y, float W, float H, string? QuestionNumber, string Text);

public sealed record PageExtraction(
    string Kind,
    string Text,
    string Summary,
    string? Topic,
    string[] Tags,
    string? MaterialType,
    int? AcademicYear,
    string? AssessmentLabel,
    string? CourseGuess,
    List<ExtractedRegion> Regions,
    string RawJson);

public sealed record FileContext(string CourseName, string MaterialType, string Title, int? Year, string? AssessmentLabel, int PageNumber, int PageCount);

public sealed record FileSuggestion(string? CourseSlug, string? MaterialType, int? AcademicYear, string? AssessmentLabel);

public interface IMaterialAi
{
    bool Online { get; }
    Task<PageExtraction> ExtractPageAsync(byte[] jpeg, FileContext ctx, string? embeddedText, IReadOnlyList<Course> courses, CancellationToken ct);
    Task<FileSuggestion?> SuggestAsync(byte[] jpeg, string fileName, string? embeddedText, IReadOnlyList<Course> courses, CancellationToken ct);
    Task<float[]?[]> EmbedAsync(IReadOnlyList<string> texts, bool query, CancellationToken ct);
}

/// <summary>
/// Gemini-backed page understanding. When no API key is configured it falls back to an offline mode that
/// uses the PDF text layer and simple heuristics, so the app still works end-to-end in development and tests.
/// </summary>
public sealed partial class MaterialAi(GeminiClient gemini, ILogger<MaterialAi> log) : IMaterialAi
{
    public bool Online => gemini.Enabled;

    private const string ExtractInstructions = """
        You read one page of university course material (lecture slides, past exam papers, quizzes, exercise sheets, labs, notes)
        for a search index. The page may be a clean PDF render or a phone photo (possibly rotated, skewed or with handwriting).

        Return JSON only, following the schema:
        - page_kind: "slide" for lecture slides, "assessment" for exam/quiz/exercise/lab pages with questions, "notes" for notes/books/summaries, else "other".
        - text: all readable text on the page EXCEPT answers, solutions, worked solutions, filled-in blanks, marking/ticks and handwritten answers. Keep question numbering. Use plain text, LaTeX-free where possible.
        - summary: 1–3 sentences describing what this page is and covers (e.g. "Midterm 2024 page 3: questions 2–3 on building a max-heap and heap sort; a worked solution follows Q2."). Mention if answers/solutions are present, but never state what they are.
        - topic: the main topic in a few words.
        - tags: 3–8 short lowercase topic tags a student would search for (e.g. "queues", "circular queue", "rear pointer").
        - material_type: one of slides, past_paper, quiz, exercise, lab, notes, other.
        - academic_year: the 4-digit year the material is from if visible (exam date, "Fall 2024", copyright line), else null.
        - assessment_label: e.g. "Midterm 1", "Final", "Quiz 3", "Exercise 6", "Lab 2" if visible, else null.
        - course_guess: the slug of the matching course from the list below, or null.
        - regions: boxes on the page, each with label:
            * "question": one per question or sub-question block, including its stem, sub-parts, figures, tables, code and answer choices. Do NOT include the answer or solution area.
            * "answer": any answer, solution, worked example solution, filled-in/handwritten response, circled/ticked choice or marking scheme. Keep them tight: they are cropped and shown on their own when a student asks for the answer.
            * "other": headers, instructions, logos, page furniture, slide titles/bodies on non-assessment pages.
          box_2d is [ymin, xmin, ymax, xmax] normalized to 0–1000. question_number like "1", "2(b)", "Q3", or null. text is the region's text.
          Slides normally have no question/answer regions unless they contain an exercise.
        """;

    private static readonly JsonObject ExtractSchema = (JsonObject)JsonNode.Parse("""
        {
          "type": "OBJECT",
          "properties": {
            "page_kind": { "type": "STRING", "enum": ["slide", "assessment", "notes", "other"] },
            "text": { "type": "STRING" },
            "summary": { "type": "STRING" },
            "topic": { "type": "STRING" },
            "tags": { "type": "ARRAY", "items": { "type": "STRING" } },
            "material_type": { "type": "STRING", "enum": ["slides", "past_paper", "quiz", "exercise", "lab", "notes", "other"] },
            "academic_year": { "type": "INTEGER", "nullable": true },
            "assessment_label": { "type": "STRING", "nullable": true },
            "course_guess": { "type": "STRING", "nullable": true },
            "regions": {
              "type": "ARRAY",
              "items": {
                "type": "OBJECT",
                "properties": {
                  "label": { "type": "STRING", "enum": ["question", "answer", "other"] },
                  "box_2d": { "type": "ARRAY", "items": { "type": "INTEGER" } },
                  "question_number": { "type": "STRING", "nullable": true },
                  "text": { "type": "STRING" }
                },
                "required": ["label", "box_2d", "text"]
              }
            }
          },
          "required": ["page_kind", "text", "summary", "topic", "tags", "material_type", "regions"]
        }
        """)!;

    private static readonly JsonObject SuggestSchema = (JsonObject)JsonNode.Parse("""
        {
          "type": "OBJECT",
          "properties": {
            "course_guess": { "type": "STRING", "nullable": true },
            "material_type": { "type": "STRING", "enum": ["slides", "past_paper", "quiz", "exercise", "lab", "notes", "other"] },
            "academic_year": { "type": "INTEGER", "nullable": true },
            "assessment_label": { "type": "STRING", "nullable": true }
          },
          "required": ["material_type"]
        }
        """)!;

    private static string CourseList(IReadOnlyList<Course> courses) =>
        string.Join("\n", courses.Select(c => $"- {c.Slug}: {c.Name} (aka {string.Join(", ", c.Aliases)})"));

    public async Task<PageExtraction> ExtractPageAsync(byte[] jpeg, FileContext ctx, string? embeddedText, IReadOnlyList<Course> courses, CancellationToken ct)
    {
        if (!gemini.Enabled) return Offline(ctx, embeddedText);

        var prompt = $"""
            {ExtractInstructions}

            Courses:
            {CourseList(courses)}

            The uploader filed this as: course "{ctx.CourseName}", type "{ctx.MaterialType}", title "{ctx.Title}"{(ctx.Year is { } y ? $", year {y}" : "")}{(ctx.AssessmentLabel is { } l ? $", label \"{l}\"" : "")}. This is page {ctx.PageNumber} of {ctx.PageCount}.
            {(string.IsNullOrWhiteSpace(embeddedText) ? "" : $"The PDF text layer for this page (may be incomplete or out of order):\n\"\"\"\n{Truncate(embeddedText, 6000)}\n\"\"\"")}
            """;
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(GeminiClient.InlineImage(jpeg), new JsonObject { ["text"] = prompt }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = ExtractSchema.DeepClone(),
            },
        };
        var res = await gemini.GenerateAsync(body, ct);
        var raw = GeminiClient.CandidateText(res);
        var j = Json.Parse(raw) ?? throw new InvalidOperationException("Gemini returned non-JSON extraction: " + Truncate(raw, 200));
        return FromJson(j, raw);
    }

    internal static PageExtraction FromJson(JsonNode j, string raw)
    {
        var regions = new List<ExtractedRegion>();
        if (j["regions"] is JsonArray arr)
        {
            foreach (var r in arr)
            {
                if (r?["box_2d"] is not JsonArray box || box.Count != 4) continue;
                var b = box.Select(v => Math.Clamp(v?.GetValue<double>() ?? 0, 0, 1000) / 1000.0).ToArray();
                var (y0, x0, y1, x1) = (Math.Min(b[0], b[2]), Math.Min(b[1], b[3]), Math.Max(b[0], b[2]), Math.Max(b[1], b[3]));
                if (y1 - y0 < 0.01 || x1 - x0 < 0.01) continue;
                regions.Add(new ExtractedRegion(
                    RegionLabels.Normalize(r.Str("label")),
                    (float)x0, (float)y0, (float)(x1 - x0), (float)(y1 - y0),
                    r.Str("question_number"),
                    r.Str("text") ?? ""));
            }
        }
        var year = j.Int("academic_year");
        if (year is < 2000 or > 2100) year = null;
        return new PageExtraction(
            PageKinds.Normalize(j.Str("page_kind")),
            j.Str("text") ?? "",
            j.Str("summary") ?? "",
            j.Str("topic"),
            j.StrArray("tags").Select(t => t.ToLowerInvariant()).Distinct().Take(10).ToArray(),
            j.Str("material_type") is { } mt ? MaterialTypes.Normalize(mt) : null,
            year,
            j.Str("assessment_label"),
            j.Str("course_guess"),
            regions,
            raw);
    }

    public async Task<FileSuggestion?> SuggestAsync(byte[] jpeg, string fileName, string? embeddedText, IReadOnlyList<Course> courses, CancellationToken ct)
    {
        if (!gemini.Enabled) return null;
        var prompt = $"""
            This is the first page of a file named "{fileName}" uploaded to a university batch's course material drive (Year 2, CS/CYS/AI).
            Guess which course it belongs to (slug from the list, or null), the material type, the year it is from and an assessment label such as "Quiz 3" or "Midterm 1" if any.

            Courses:
            {CourseList(courses)}
            {(string.IsNullOrWhiteSpace(embeddedText) ? "" : $"Text layer:\n\"\"\"\n{Truncate(embeddedText, 3000)}\n\"\"\"")}
            """;
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(GeminiClient.InlineImage(jpeg), new JsonObject { ["text"] = prompt }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = SuggestSchema.DeepClone(),
            },
        };
        try
        {
            var res = await gemini.GenerateAsync(body, ct);
            var j = Json.Parse(GeminiClient.CandidateText(res));
            if (j is null) return null;
            var year = j.Int("academic_year");
            return new FileSuggestion(j.Str("course_guess"), j.Str("material_type") is { } t ? MaterialTypes.Normalize(t) : null,
                year is >= 2000 and <= 2100 ? year : null, j.Str("assessment_label"));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Suggestion failed for {File}", fileName);
            return null;
        }
    }

    public async Task<float[]?[]> EmbedAsync(IReadOnlyList<string> texts, bool query, CancellationToken ct)
    {
        if (!gemini.Enabled || texts.Count == 0) return new float[]?[texts.Count];
        var vectors = await gemini.EmbedAsync(texts, query, ct);
        return vectors.Select(v => (float[]?)v).ToArray();
    }

    // ---------- offline fallback ----------

    [GeneratedRegex(@"[a-zA-Z][a-zA-Z\-]{3,}")]
    private static partial Regex WordPattern();

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "this", "that", "with", "from", "have", "your", "what", "which", "will", "there", "their", "they", "them", "then",
        "than", "into", "each", "when", "where", "were", "been", "being", "also", "only", "some", "such", "other", "these",
        "those", "page", "following", "given", "show", "answer", "question", "questions", "marks", "points", "write",
        "explain", "state", "using", "used", "about", "would", "could", "should", "must", "after", "before", "does",
    };

    internal static PageExtraction Offline(FileContext ctx, string? embeddedText)
    {
        var text = (embeddedText ?? "").Trim();
        var tags = WordPattern().Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => !Stop.Contains(w))
            .GroupBy(w => w).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Take(5).Select(g => g.Key).ToArray();
        var assessment = MaterialTypes.IsAssessment(ctx.MaterialType);
        var regions = new List<ExtractedRegion>();
        if (assessment)
            regions.Add(new ExtractedRegion(RegionLabels.Question, 0.02f, 0.02f, 0.96f, 0.96f, null, text));
        var kind = ctx.MaterialType switch
        {
            MaterialTypes.Slides => PageKinds.Slide,
            MaterialTypes.Notes => PageKinds.Notes,
            _ when assessment => PageKinds.Assessment,
            _ => PageKinds.Other,
        };
        return new PageExtraction(kind, text,
            $"Page {ctx.PageNumber} of {ctx.Title} (offline extraction: no AI key configured, text comes from the PDF text layer).",
            tags.FirstOrDefault(), tags, null, null, null, null, regions, "{\"offline\":true}");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
