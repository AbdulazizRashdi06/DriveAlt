namespace DriveAlt.Api.Files;

/// <summary>The shape of material requested, independently of course/type/year filters.</summary>
public static class MaterialWants
{
    public const string Questions = "questions";
    public const string Slides = "slides";
    public const string SlidePages = "slide_pages";
    public const string Files = "files";
    public const string NotesPages = "notes_pages";
    public const string None = "none";
    public static bool IsSearch(string? value) => value is Questions or Slides or SlidePages or Files or NotesPages;

    public static string Effective(string text, string declared) => Heuristics.AskedKind(text) ?? declared;
}
