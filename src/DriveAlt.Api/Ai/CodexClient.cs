using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DriveAlt.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace DriveAlt.Api.Ai;

/// <summary>
/// The AI can't take work right now (ChatGPT plan usage limit, or the gateway lost its login). Processing pauses the file
/// and retries later instead of falling back to offline reading, so no page silently loses its boxes.
/// </summary>
public sealed class AiUnavailableException(string message) : Exception(message)
{
    public static bool In(Exception e) => e switch
    {
        AiUnavailableException => true,
        AggregateException a => a.InnerExceptions.Any(In),
        _ => e.InnerException is { } inner && In(inner),
    };
}

/// <summary>
/// Client for the Zarqa stack's private Codex gateway: one system and one user message (text plus up to four inline
/// images for legacy calls, up to eight for Terra batches) and a strict JSON schema in, the JSON answer out.
/// </summary>
public sealed class CodexClient(HttpClient http, IOptions<CodexOptions> opts, ILogger<CodexClient> log)
{
    private CodexOptions O => opts.Value;

    public bool Enabled => O.Enabled;
    public int BatchSize => Math.Clamp(O.BatchSize, 1, 8);
    public string BatchModel => O.BatchModel;
    public string BatchReasoningEffort => O.BatchReasoningEffort;

    /// <summary>Returns the answer JSON as text (the gateway has already checked it against the schema).</summary>
    public Task<string> JsonAsync(string system, string text, byte[] jpeg, JsonObject schema, CancellationToken ct) =>
        RequestAsync(system, text, [jpeg], schema, O.Model, null, ct);

    public Task<string> BatchJsonAsync(string system, string text, IReadOnlyList<byte[]> images, JsonObject schema, CancellationToken ct) =>
        RequestAsync(system, text, images, schema, O.BatchModel, O.BatchReasoningEffort, ct);

    private async Task<string> RequestAsync(string system, string text, IReadOnlyList<byte[]> images, JsonObject schema,
        string model, string? effort, CancellationToken ct)
    {
        if (images.Count is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(images));
        var content = new JsonArray(DeepSeekClient.TextPart(text));
        foreach (var jpeg in images) content.Add(DeepSeekClient.ImagePart(jpeg));
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = content,
                }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "page", ["schema"] = schema.DeepClone() },
            },
        };
        if (effort is not null) body["reasoning_effort"] = effort;
        var payload = body.ToJsonString();
        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{O.BaseUrl.TrimEnd('/')}/chat/completions")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            using var res = await http.SendAsync(req, ct);
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode)
                return JsonNode.Parse(raw)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("Codex gateway returned no content");

            // 429: the plan's usage limit (or the gateway queue is full). 503: the gateway needs a fresh ChatGPT login.
            // 400/404: the gateway rejects the request itself (e.g. it doesn't allow this model yet) - a setup problem, not the page's.
            if (res.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
                throw new AiUnavailableException($"Codex unavailable ({(int)res.StatusCode}): {Short(raw)}");
            if (res.StatusCode < HttpStatusCode.InternalServerError || attempt >= 3)
                throw new InvalidOperationException($"Codex gateway error {(int)res.StatusCode}: {Short(raw)}");
            log.LogWarning("Codex gateway {Status}, retrying (attempt {Attempt})", (int)res.StatusCode, attempt);
            await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
        }
    }

    private static string Short(string s) => s.Length > 300 ? s[..300] : s;
}
