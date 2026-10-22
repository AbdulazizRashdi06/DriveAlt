using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DriveAlt.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace DriveAlt.Api.Ai;

/// <summary>Thin REST client for the Gemini API (generateContent + embeddings).</summary>
public sealed class GeminiClient(HttpClient http, IOptions<GeminiOptions> opts, ILogger<GeminiClient> log)
{
    private GeminiOptions O => opts.Value;

    public bool Enabled => O.Enabled;

    public async Task<JsonObject> GenerateAsync(JsonObject body, CancellationToken ct)
    {
        var url = $"{O.BaseUrl}/models/{O.Model}:generateContent";
        return await PostAsync(url, body, ct);
    }

    /// <summary>Returns the concatenated text parts of the first candidate.</summary>
    public static string CandidateText(JsonObject response)
    {
        var parts = response["candidates"]?[0]?["content"]?["parts"]?.AsArray();
        if (parts is null) return "";
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (p?["thought"]?.GetValue<bool>() == true) continue;
            if (p?["text"] is JsonValue t) sb.Append(t.GetValue<string>());
        }
        return sb.ToString();
    }

    public static JsonObject UserText(string text) => new()
    {
        ["role"] = "user",
        ["parts"] = new JsonArray(new JsonObject { ["text"] = text }),
    };

    public static JsonObject InlineImage(byte[] bytes, string mime = "image/jpeg") => new()
    {
        ["inlineData"] = new JsonObject { ["mimeType"] = mime, ["data"] = Convert.ToBase64String(bytes) },
    };

    /// <param name="query">true for search queries, false for documents being indexed.</param>
    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, bool query, CancellationToken ct)
    {
        if (texts.Count == 0) return [];
        var result = new List<float[]>(texts.Count);
        foreach (var chunk in texts.Chunk(100))
        {
            var requests = new JsonArray();
            foreach (var t in chunk)
            {
                requests.Add(new JsonObject
                {
                    ["model"] = $"models/{O.EmbeddingModel}",
                    ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = Truncate(t, 8000) }) },
                    ["taskType"] = query ? "RETRIEVAL_QUERY" : "RETRIEVAL_DOCUMENT",
                    ["outputDimensionality"] = O.EmbeddingDimensions,
                });
            }
            var url = $"{O.BaseUrl}/models/{O.EmbeddingModel}:batchEmbedContents";
            var res = await PostAsync(url, new JsonObject { ["requests"] = requests }, ct);
            foreach (var e in res["embeddings"]!.AsArray())
                result.Add(Normalize(e!["values"]!.AsArray().Select(v => v!.GetValue<float>()).ToArray()));
        }
        return result.ToArray();
    }

    private static float[] Normalize(float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        if (norm > 0) for (var i = 0; i < v.Length; i++) v[i] /= norm;
        return v;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private async Task<JsonObject> PostAsync(string url, JsonObject body, CancellationToken ct)
    {
        var payload = body.ToJsonString();
        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("x-goog-api-key", O.ApiKey);
            using var res = await http.SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode)
                return JsonNode.Parse(text)!.AsObject();

            var retryable = res.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
            if (!retryable || attempt >= 4)
                throw new GeminiException((int)res.StatusCode, text.Length > 600 ? text[..600] : text);
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            log.LogWarning("Gemini {Status}, retrying in {Delay}s", (int)res.StatusCode, delay.TotalSeconds);
            await Task.Delay(delay, ct);
        }
    }
}

public sealed class GeminiException(int status, string body) : Exception($"Gemini API error {status}: {body}")
{
    public int Status { get; } = status;
}

public static class Json
{
    public static JsonNode? Parse(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```"))
        {
            var first = s.IndexOf('\n');
            var last = s.LastIndexOf("```", StringComparison.Ordinal);
            if (first > 0 && last > first) s = s[(first + 1)..last];
        }
        try { return JsonNode.Parse(s); }
        catch (JsonException) { return null; }
    }

    public static string? Str(this JsonNode? n, string key)
    {
        var v = n?[key];
        if (v is null) return null;
        if (v is JsonValue jv && jv.TryGetValue<string>(out var s)) return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return v.ToJsonString().Trim('"');
    }

    public static int? Int(this JsonNode? n, string key)
    {
        var v = n?[key];
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<int>(out var i)) return i;
            if (jv.TryGetValue<double>(out var d)) return (int)d;
            if (jv.TryGetValue<string>(out var s) && int.TryParse(s, out var p)) return p;
        }
        return null;
    }

    public static string[] StrArray(this JsonNode? n, string key) =>
        n?[key] is JsonArray a
            ? a.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : null)
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToArray()
            : [];
}
