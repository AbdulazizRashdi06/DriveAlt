using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DriveAlt.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace DriveAlt.Api.Ai;

/// <summary>Thin REST client for DeepSeek's OpenAI-style chat completions (text, images, tool calls, JSON output).</summary>
public sealed class DeepSeekClient(HttpClient http, IOptions<DeepSeekOptions> opts, ILogger<DeepSeekClient> log)
{
    private DeepSeekOptions O => opts.Value;

    public bool Enabled => O.Enabled;

    /// <summary>Posts a chat completion; <c>model</c> is filled in from config. Returns the first choice's message.</summary>
    public async Task<JsonObject> ChatAsync(JsonObject body, CancellationToken ct)
    {
        body["model"] = O.Model;
        var res = await PostAsync($"{O.BaseUrl.TrimEnd('/')}/chat/completions", body, ct);
        return res["choices"]?[0]?["message"] as JsonObject ?? throw new InvalidOperationException("DeepSeek returned no message");
    }

    public static JsonObject Message(string role, string text) => new() { ["role"] = role, ["content"] = text };

    public static JsonObject ImagePart(byte[] bytes, string mime = "image/jpeg") => new()
    {
        ["type"] = "image_url",
        ["image_url"] = new JsonObject { ["url"] = $"data:{mime};base64,{Convert.ToBase64String(bytes)}" },
    };

    public static JsonObject TextPart(string text) => new() { ["type"] = "text", ["text"] = text };

    /// <summary>Turns Gemini-style function declarations (OBJECT/STRING types) into OpenAI-style tools.</summary>
    public static JsonArray ToolsFromGemini(JsonArray geminiTools)
    {
        var tools = new JsonArray();
        foreach (var group in geminiTools)
            foreach (var fn in group!["functionDeclarations"]!.AsArray())
            {
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = fn!["name"]!.DeepClone(),
                        ["description"] = fn["description"]!.DeepClone(),
                        ["parameters"] = LowerTypes(fn["parameters"]!.DeepClone()),
                    },
                });
            }
        return tools;
    }

    private static JsonNode LowerTypes(JsonNode node)
    {
        if (node is JsonObject o)
        {
            foreach (var (k, v) in o.ToList())
            {
                if (k == "type" && v is JsonValue t) o[k] = t.GetValue<string>().ToLowerInvariant();
                else if (v is not null) LowerTypes(v);
            }
        }
        else if (node is JsonArray a)
        {
            foreach (var v in a) if (v is not null) LowerTypes(v);
        }
        return node;
    }

    private async Task<JsonObject> PostAsync(string url, JsonObject body, CancellationToken ct)
    {
        var payload = body.ToJsonString();
        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("Authorization", $"Bearer {O.ApiKey}");
            using var res = await http.SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode)
                return JsonNode.Parse(text)!.AsObject();

            var retryable = res.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
            if (!retryable || attempt >= 4)
                throw new InvalidOperationException($"DeepSeek API error {(int)res.StatusCode}: {(text.Length > 600 ? text[..600] : text)}");
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            log.LogWarning("DeepSeek {Status}, retrying in {Delay}s", (int)res.StatusCode, delay.TotalSeconds);
            await Task.Delay(delay, ct);
        }
    }
}
