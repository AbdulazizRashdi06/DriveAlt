using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DriveAlt.Api.Data;
using DriveAlt.Api.Processing;

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
    string RawJson,
    bool NeedsReview = false);

public sealed record FileContext(string CourseName, string MaterialType, string Title, int? Year, string? AssessmentLabel, int PageNumber, int PageCount);

public sealed record FileSuggestion(string? CourseSlug, string? MaterialType, int? AcademicYear, string? AssessmentLabel);
public sealed record PageReadInput(byte[] Jpeg, FileContext Context, string EmbeddedText);

public interface IMaterialAi
{
    bool Online { get; }
    bool UsesBatchExtraction => false;
    int ExtractionBatchSize => 1;
    Task<IReadOnlyList<PageExtraction>> ExtractBatchAsync(IReadOnlyList<PageReadInput> pages, IReadOnlyList<Course> courses, CancellationToken ct) =>
        throw new NotSupportedException("This provider reads individual pages.");
    Task<PageExtraction> ExtractPageAsync(byte[] jpeg, FileContext ctx, string? embeddedText, IReadOnlyList<Course> courses, CancellationToken ct);
    Task<FileSuggestion?> SuggestAsync(byte[] jpeg, string fileName, string? embeddedText, IReadOnlyList<Course> courses, CancellationToken ct);
    Task<float[]?[]> EmbedAsync(IReadOnlyList<string> texts, bool query, CancellationToken ct);
}

/// <summary>
/// Page understanding by the Codex gateway when it is configured, else DeepSeek when its key is set, else Gemini;
/// embeddings always come from Gemini. When no AI is configured it falls back to an offline mode that uses the PDF text
/// layer and simple heuristics, so the app still works end-to-end in development and tests.
/// </summary>
public sealed partial class MaterialAi(GeminiClient gemini, DeepSeekClient deepseek, CodexClient codex, ILogger<MaterialAi> log) : IMaterialAi
{
    public bool Online => gemini.Enabled || deepseek.Enabled || codex.Enabled;

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
        - regions: one box per question, each with label:
            * "question": one per question or sub-question block, covering everything that belongs to it on the paper: its number or title,
              stem, sub-parts, figures, tables, code, answer choices, and any answer, solution, working or handwriting that goes with it.
              Crops are shown to students exactly as the paper is, answers included; that is fine. You must never WRITE an answer, though.
          Be generous: span the text column and leave a small margin. A box that is a bit too big is fine; one that cuts off part of a
          question or its answer is a failure. Neighbouring boxes may touch but must not overlap. Do not create separate answer or other boxes.
          box_2d is [ymin, xmin, ymax, xmax] normalized to 0–1000. question_number like "1", "2(b)", "Q3", or null.
          text is the question's own wording only: never write out, paraphrase or hint at an answer or solution in it.
          Return an empty regions array for slides or pages without questions, including answer-only pages.
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
                  "label": { "type": "STRING", "enum": ["question"] },
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

    // ---------- Codex (GPT): pixel boxes, which GPT models place far better than Gemini's 0–1000 format ----------

    private const string NormalizedBoxLine = "box_2d is [ymin, xmin, ymax, xmax] normalized to 0–1000.";
    private const string CodexSystem = "You read pages of university course material for a search index.";

    internal static string PixelBoxInstructions(int width, int height) => ExtractInstructions
        .Replace(NormalizedBoxLine, $"bbox is [x0, y0, x1, y1] in pixels of this image, which is {width}x{height} pixels with the origin at the top-left.")
        .Replace("box_2d", "bbox");

    private static JsonObject Nullable(string type) => new() { ["type"] = new JsonArray(type, "null") };

    private static JsonObject Strict(JsonObject properties) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray(properties.Select(p => (JsonNode)p.Key).ToArray()),
        ["properties"] = properties,
    };

    private static JsonObject Enum(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()) };

    private static readonly JsonObject CodexExtractSchema = Strict(new JsonObject
    {
        ["page_kind"] = Enum("slide", "assessment", "notes", "other"),
        ["text"] = new JsonObject { ["type"] = "string" },
        ["summary"] = new JsonObject { ["type"] = "string" },
        ["topic"] = new JsonObject { ["type"] = "string" },
        ["tags"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        ["material_type"] = Enum("slides", "past_paper", "quiz", "exercise", "lab", "notes", "other"),
        ["academic_year"] = Nullable("integer"),
        ["assessment_label"] = Nullable("string"),
        ["course_guess"] = Nullable("string"),
        ["regions"] = new JsonObject
        {
            ["type"] = "array",
            ["items"] = Strict(new JsonObject
            {
                ["label"] = Enum("question"),
                ["bbox"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" } },
                ["question_number"] = Nullable("string"),
                ["text"] = new JsonObject { ["type"] = "string" },
            }),
        },
    });

    private static readonly JsonObject CodexSuggestSchema = Strict(new JsonObject
    {
        ["course_guess"] = Nullable("string"),
        ["material_type"] = Enum("slides", "past_paper", "quiz", "exercise", "lab", "notes", "other"),
        ["academic_year"] = Nullable("integer"),
        ["assessment_label"] = Nullable("string"),
    });

    /// <summary>Rewrites each region's pixel <c>bbox</c> as the app's <c>box_2d</c> ([ymin, xmin, ymax, xmax], 0–1000).</summary>
    internal static JsonNode PixelBoxesToNormalized(JsonNode j, int width, int height)
    {
        if (j["regions"] is not JsonArray regions) return j;
        foreach (var r in regions.OfType<JsonObject>())
        {
            if (r["bbox"] is not JsonArray b || b.Count != 4) continue;
            var v = b.Select(x => x?.GetValue<double>() ?? 0).ToArray();
            int Y(double y) => (int)Math.Round(y / height * 1000);
            int X(double x) => (int)Math.Round(x / width * 1000);
            // Parsed (not constructed) numbers, so FromJson's GetValue<double>() reads them like any model's output.
            r["box_2d"] = JsonNode.Parse($"[{Y(v[1])},{X(v[0])},{Y(v[3])},{X(v[2])}]");
            r.Remove("bbox");
        }
        return j;
    }

    private static string CourseList(IReadOnlyList<Course> courses) =>
        string.Join("\n", courses.Select(c => $"- {c.Slug}: {c.Name} (aka {string.Join(", ", c.Aliases)})"));

    /// <summary>DeepSeek has no response schema: it gets the schema in the prompt and JSON mode.</summary>
    private async Task<string> DeepSeekJsonAsync(byte[] jpeg, string prompt, JsonObject schema, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(
                    DeepSeekClient.ImagePart(jpeg),
                    DeepSeekClient.TextPart($"{prompt}\n\nReply with one JSON object matching this schema:\n{schema.ToJsonString()}")),
            }),
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
        };
        var msg = await deepseek.ChatAsync(body, ct);
        return msg["content"]?.GetValue<string>() ?? "";
    }

    public async Task<PageExtraction> ExtractPageAsync(byte[] jpeg, FileContext ctx, string? embeddedText, IReadOnlyList<Course> courses, CancellationToken ct)
    {
        if (!Online) return Offline(ctx, embeddedText);

        var prompt = $"""
            {ExtractInstructions}

            Courses:
            {CourseList(courses)}

            The uploader filed this as: course "{ctx.CourseName}", type "{ctx.MaterialType}", title "{ctx.Title}"{(ctx.Year is { } y ? $", year {y}" : "")}{(ctx.AssessmentLabel is { } l ? $", label \"{l}\"" : "")}. This is page {ctx.PageNumber} of {ctx.PageCount}.
            {(string.IsNullOrWhiteSpace(embeddedText) ? "" : $"The PDF text layer for this page (may be incomplete or out of order):\n\"\"\"\n{Truncate(embeddedText, 6000)}\n\"\"\"")}
            """;
        if (codex.Enabled)
        {
            var (w, h) = ImageOps.Identify(jpeg);
            var codexPrompt = prompt.Replace(ExtractInstructions, PixelBoxInstructions(w, h));
            var answer = await codex.JsonAsync(CodexSystem, codexPrompt, jpeg, CodexExtractSchema, ct);
            var node = Json.Parse(answer) ?? throw new InvalidOperationException("Codex returned non-JSON extraction: " + Truncate(answer, 200));
            return FromJson(PixelBoxesToNormalized(node, w, h), answer);
        }
        if (deepseek.Enabled)
        {
            var text = await DeepSeekJsonAsync(jpeg, prompt, ExtractSchema, ct);
            var parsed = Json.Parse(text) ?? throw new InvalidOperationException("DeepSeek returned non-JSON extraction: " + Truncate(text, 200));
            return FromJson(parsed, text);
        }
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

    /// <summary>Added on every side of a model's box (fraction of the page), so a tight box never clips a question or its answer.</summary>
    internal const double BoxPadding = 0.015;

    internal static PageExtraction FromJson(JsonNode j, string raw)
    {
        var regions = new List<ExtractedRegion>();
        if (j["regions"] is JsonArray arr)
        {
            foreach (var r in arr)
            {
                if (r.Str("label") != RegionLabels.Question) continue;
                if (r?["box_2d"] is not JsonArray box || box.Count != 4) continue;
                var b = box.Select(v => Math.Clamp(v?.GetValue<double>() ?? 0, 0, 1000) / 1000.0).ToArray();
                var (y0, x0, y1, x1) = (Math.Min(b[0], b[2]), Math.Min(b[1], b[3]), Math.Max(b[0], b[2]), Math.Max(b[1], b[3]));
                if (y1 - y0 < 0.01 || x1 - x0 < 0.01) continue;
                (y0, x0, y1, x1) = (Math.Max(0, y0 - BoxPadding), Math.Max(0, x0 - BoxPadding), Math.Min(1, y1 + BoxPadding), Math.Min(1, x1 + BoxPadding));
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
        if (!Online) return null;
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
            var j = Json.Parse(codex.Enabled ? await codex.BatchJsonAsync(CodexSystem, prompt, [jpeg], CodexSuggestSchema, ct)
                : deepseek.Enabled ? await DeepSeekJsonAsync(jpeg, prompt, SuggestSchema, ct)
                : GeminiClient.CandidateText(await gemini.GenerateAsync(body, ct)));
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
