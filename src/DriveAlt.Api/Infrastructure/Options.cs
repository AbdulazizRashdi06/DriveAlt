namespace DriveAlt.Api.Infrastructure;

public sealed class AppOptions
{
    public string Name { get; set; } = "DriveAlt";
    public string PublicUrl { get; set; } = "http://localhost:5257";
}

public sealed class AuthOptions
{
    /// <summary>Email built from the uni ID, e.g. "{id}@student.gutech.edu.om".</summary>
    public string EmailTemplate { get; set; } = "{id}@student.gutech.edu.om";

    /// <summary>Uni IDs like 24-0161 that get the uploader role on sign-in.</summary>
    public List<string> Uploaders { get; set; } = [];

    /// <summary>Optional allow-list of uni ID prefixes (e.g. "24-"). Empty = any valid uni ID.</summary>
    public List<string> AllowedPrefixes { get; set; } = [];

    /// <summary>Secret mixed into OTP hashes. Must be set in production.</summary>
    public string OtpSecret { get; set; } = "dev-only-otp-secret";

    public int OtpMinutes { get; set; } = 10;
    public int OtpMaxAttempts { get; set; } = 5;
}

public sealed class ResendOptions
{
    public string ApiKey { get; set; } = "";
    public string From { get; set; } = "DriveAlt <noreply@tryzarqa.com>";
}

public sealed class GeminiOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public string Model { get; set; } = "gemini-3.8-flash";
    public string EmbeddingModel { get; set; } = "gemini-embedding-001";

    /// <summary>Must match the vector column size in the database.</summary>
    public int EmbeddingDimensions { get; set; } = 768;

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>When set, DeepSeek runs the chat bot and reads pages; Gemini then only does embeddings.</summary>
public sealed class DeepSeekOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    /// <summary>Non-thinking mode: forced tool calls and JSON output only work there.</summary>
    public string Model { get; set; } = "deepseek-chat";

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// When set, pages are read through the Zarqa stack's private Codex gateway (ChatGPT login, OpenAI-style API) instead of
/// DeepSeek or Gemini. Chat is unaffected.
/// </summary>
public sealed class CodexOptions
{
    /// <summary>e.g. http://codex:8090/v1 on the zarqa_default network.</summary>
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "gpt-6.1-sol";

    /// <summary>How long a file waits before retrying after the plan's usage limit (or a lost login) stopped it.</summary>
    public int PauseMinutes { get; set; } = 20;

    public bool Enabled => !string.IsNullOrWhiteSpace(BaseUrl);
}

public sealed class StorageOptions
{
    public string Root { get; set; } = "storage";
    public long MaxUploadBytes { get; set; } = 200L * 1024 * 1024;
    public int MaxPagesPerFile { get; set; } = 400;
}

public sealed class ProcessingOptions
{
    public int Workers { get; set; } = 2;
    public int PageConcurrency { get; set; } = 3;
    public int RenderDpi { get; set; } = 150;
    public string SofficePath { get; set; } = "soffice";
    public string PdftoppmPath { get; set; } = "pdftoppm";
    public string PdftotextPath { get; set; } = "pdftotext";
    public string HeifConvertPath { get; set; } = "heif-convert";
}

public sealed class GoogleOptions
{
    /// <summary>Drive API key used by the import-drive command. Optional: public folders can be scraped without one.</summary>
    public string ApiKey { get; set; } = "";
}
