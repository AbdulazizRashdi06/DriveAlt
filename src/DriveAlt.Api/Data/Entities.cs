using NpgsqlTypes;
using Pgvector;

namespace DriveAlt.Api.Data;

public static class Roles
{
    public const string Student = "student";
    public const string Uploader = "uploader";
}

public static class MaterialTypes
{
    public const string Slides = "slides";
    public const string PastPaper = "past_paper";
    public const string Quiz = "quiz";
    public const string Exercise = "exercise";
    public const string Lab = "lab";
    public const string Notes = "notes";
    public const string Other = "other";

    public static readonly string[] All = [Slides, PastPaper, Quiz, Exercise, Lab, Notes, Other];

    /// <summary>Assessment material: answers may exist on the page and must never be repeated by the bot.</summary>
    public static bool IsAssessment(string type) => type is PastPaper or Quiz or Exercise or Lab;

    public static string Normalize(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        return v switch
        {
            "slide" or "slides" or "lecture" => Slides,
            "past_paper" or "past_papers" or "pastpaper" or "exam" or "midterm" or "final" => PastPaper,
            "quiz" or "quizzes" or "test" => Quiz,
            "exercise" or "exercises" or "tutorial" or "assignment" or "worksheet" => Exercise,
            "lab" or "labs" => Lab,
            "notes" or "note" or "summary" or "book" or "cheat_sheet" => Notes,
            _ => All.Contains(v) ? v : Other,
        };
    }
}

public static class FileStatuses
{
    /// <summary>Uploaded; waiting for the uploader to confirm course + type.</summary>
    public const string Uploaded = "uploaded";
    public const string Queued = "queued";
    public const string Splitting = "splitting";
    public const string Reading = "reading";
    public const string Tagging = "tagging";
    public const string Cropping = "cropping";
    public const string Ready = "ready";
    public const string Failed = "failed";

    public static bool InProgress(string s) => s is Queued or Splitting or Reading or Tagging or Cropping;

    /// <summary>
    /// Statuses students can find. A file being (re)read keeps its earlier pages and question boxes searchable until the new
    /// ones replace them; pages and files without content never match anyway. Unconfirmed uploads, splitting and failed files stay hidden.
    /// </summary>
    public static readonly string[] Visible = [Ready, Queued, Reading, Tagging, Cropping];

    /// <summary>1-based step for the 5-segment pipeline bar in the UI.</summary>
    public static int Step(string s) => s switch
    {
        Splitting => 1,
        Reading => 2,
        Tagging => 3,
        Cropping => 4,
        Ready => 5,
        _ => 0,
    };
}

public static class SourceKinds
{
    public const string Pdf = "pdf";
    public const string Office = "office";
    public const string Images = "images";
}

public static class RegionLabels
{
    public const string Question = "question";
    public const string Answer = "answer";
    public const string Other = "other";

    public static string Normalize(string? v) => (v ?? "").Trim().ToLowerInvariant() switch
    {
        "question" or "q" => Question,
        "answer" or "solution" or "answer/solution" => Answer,
        _ => Other,
    };
}

public static class PageKinds
{
    public const string Slide = "slide";
    public const string Assessment = "assessment";
    public const string Notes = "notes";
    public const string Other = "other";

    public static string Normalize(string? v) => (v ?? "").Trim().ToLowerInvariant() switch
    {
        "slide" => Slide,
        "assessment" or "exam" or "quiz" or "exercise" => Assessment,
        "notes" => Notes,
        _ => Other,
    };
}

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UniId { get; set; }
    public required string Email { get; set; }
    public string Role { get; set; } = Roles.Student;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

public sealed class OtpCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UniId { get; set; }
    public required string CodeHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public sealed class Course
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public int Semester { get; set; }

    /// <summary>Drive-style grouping inside a semester: "AI", "ALL", "CS & CYS" or "" (no grouping).</summary>
    public string Group { get; set; } = "";
    public string[] Aliases { get; set; } = [];
    public string Color { get; set; } = "#FF6B2C";
    public int Sort { get; set; }
}

public sealed class DocFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Null only while an upload waits for the uploader to pick a course.</summary>
    public int? CourseId { get; set; }
    public Course? Course { get; set; }
    public string MaterialType { get; set; } = MaterialTypes.Other;
    public int? AcademicYear { get; set; }
    public string? AssessmentLabel { get; set; }

    /// <summary>Browse subfolder below the course + type, e.g. "midterm/midterm 1" or "2024".</summary>
    public string? Folder { get; set; }
    public required string Title { get; set; }
    public required string OriginalName { get; set; }
    public string SourceKind { get; set; } = SourceKinds.Pdf;

    /// <summary>Stored original file names under originals/{id}/, in page order for image sets.</summary>
    public string[] Parts { get; set; } = [];
    public string Mime { get; set; } = "application/octet-stream";
    public required string Sha256 { get; set; }

    /// <summary>Where an imported file came from, e.g. "gdrive:{fileId}". Null for direct uploads.</summary>
    public string? SourceRef { get; set; }
    public long SizeBytes { get; set; }
    public int PageCount { get; set; }
    public string Status { get; set; } = FileStatuses.Uploaded;
    public int Progress { get; set; }
    public string? Error { get; set; }

    /// <summary>Which classification fields were guessed (not confirmed by a person): "course", "type", ...</summary>
    public string[] Guessed { get; set; } = [];
    public Guid UploadedById { get; set; }
    public User? UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<Page> Pages { get; set; } = [];
}

public sealed class Page
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FileId { get; set; }
    public DocFile? File { get; set; }
    public int Number { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Kind { get; set; } = PageKinds.Other;

    /// <summary>Readable text on the page, excluding answers/solutions. Used for search only.</summary>
    public string Text { get; set; } = "";

    /// <summary>The AI's understanding of the page; shown to uploaders in the comparison view.</summary>
    public string AiSummary { get; set; } = "";
    public string? Topic { get; set; }
    public string[] Tags { get; set; } = [];
    public int? AcademicYear { get; set; }
    public string? RawExtraction { get; set; }
    public string SearchText { get; set; } = "";
    public NpgsqlTsVector? Search { get; set; }
    public Vector? Embedding { get; set; }
    public bool Extracted { get; set; }
    public string? Error { get; set; }
    public DateTime? EditedAt { get; set; }
    public Guid? EditedById { get; set; }
    public List<Region> Regions { get; set; } = [];
}

public sealed class Region
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PageId { get; set; }
    public Page? Page { get; set; }
    public string Label { get; set; } = RegionLabels.Other;

    // Normalized 0..1 box relative to the page image.
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public string? QuestionNumber { get; set; }

    /// <summary>Region text. For answer regions this is stored but never indexed, embedded or sent to the bot.</summary>
    public string Text { get; set; } = "";
    public int Order { get; set; }
    public string SearchText { get; set; } = "";
    public NpgsqlTsVector? Search { get; set; }
    public Vector? Embedding { get; set; }
}

public static class JobKinds
{
    public const string Suggest = "suggest";
    public const string Process = "process";
    public const string Reembed = "reembed";
}

public sealed class Job
{
    public long Id { get; set; }
    public Guid FileId { get; set; }
    public Guid? PageId { get; set; }
    public required string Kind { get; set; }
    public string State { get; set; } = "queued";
    public int Attempts { get; set; }
    public DateTime RunAfter { get; set; } = DateTime.UtcNow;
    public DateTime? LockedAt { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = "New chat";
    public int RefusalCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<Message> Messages { get; set; } = [];
}

public sealed class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public required string Role { get; set; } // "user" | "bot"
    public string Text { get; set; } = "";

    /// <summary>Result refs shown with a bot message, e.g. ["r:{regionId}", "p:{pageId}"].</summary>
    public string[] Results { get; set; } = [];
    /// <summary>Further result refs held back so a reply isn't overwhelming; "show more" pages through them.</summary>
    public string[] More { get; set; } = [];
    /// <summary>Model-written follow-up lines prepared alongside held-back result batches.</summary>
    public string[] MoreCommentary { get; set; } = [];
    /// <summary>JSON list of <c>ChatOption</c>: the choices of a clarifying question, each searched while the student answers.</summary>
    public string? Options { get; set; }
    public bool Refused { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Bookmark
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid PageId { get; set; }
    public Page? Page { get; set; }
    public Guid? RegionId { get; set; }
    public Region? Region { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class MaterialReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid PageId { get; set; }
    public Page? Page { get; set; }
    public Guid? RegionId { get; set; }
    public Region? Region { get; set; }
    public required string TargetRef { get; set; }
    public required string Reason { get; set; }
    public string Details { get; set; } = "";
    public string Status { get; set; } = "open";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedById { get; set; }
}
