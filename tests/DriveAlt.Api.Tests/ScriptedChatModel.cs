using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DriveAlt.Api.Ai;
using DriveAlt.Api.Data;

namespace DriveAlt.Api.Tests;

/// <summary>A scripted model for API integration tests. No network or paid AI calls.</summary>
public sealed class ScriptedChatModel : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<JsonObject, JsonObject>> _responses = new();
    private int _requestCount;
    public int RequestCount => _requestCount;
    public int Pending => _responses.Count;
    public JsonObject? LastRequest { get; private set; }

    public void Enqueue(Func<JsonObject, JsonObject> response) => _responses.Enqueue(response);
    public void EnqueueText(string text) => Enqueue(_ => new JsonObject { ["role"] = "assistant", ["content"] = text });
    public void EnqueueTool(string name, JsonObject args) => Enqueue(_ => Tool(name, args));

    public static JsonObject Tool(string name, JsonObject args) => new()
    {
        ["role"] = "assistant", ["content"] = null,
        ["tool_calls"] = new JsonArray(new JsonObject
        {
            ["id"] = "test-call", ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = name, ["arguments"] = args.ToJsonString() },
        }),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
        LastRequest = body.DeepClone().AsObject();
        Interlocked.Increment(ref _requestCount);
        if (!_responses.TryDequeue(out var response)) throw new InvalidOperationException("The test did not schedule a model response.");
        var message = response(body);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject { ["message"] = message }),
        }) };
    }
}

/// <summary>Keep PDF extraction offline while chat is served by the scripted model.</summary>
public sealed class OfflineTestMaterialAi : IMaterialAi
{
    public bool Online => false;
    public Func<IReadOnlyList<PageReadInput>, CancellationToken, Task<IReadOnlyList<PageExtraction>>>? BatchHandler { get; set; }
    public bool UsesBatchExtraction => BatchHandler is not null;
    public int ExtractionBatchSize => 8;
    public Task<IReadOnlyList<PageExtraction>> ExtractBatchAsync(IReadOnlyList<PageReadInput> pages, IReadOnlyList<Course> courses, CancellationToken ct) =>
        BatchHandler!(pages, ct);
    public Task<PageExtraction> ExtractPageAsync(byte[] jpeg, FileContext ctx, string? embeddedText, IReadOnlyList<Course> courses, CancellationToken ct) =>
        Task.FromResult(MaterialAi.Offline(ctx, embeddedText));
    public Task<FileSuggestion?> SuggestAsync(byte[] jpeg, string fileName, string? embeddedText, IReadOnlyList<Course> courses, CancellationToken ct) =>
        Task.FromResult<FileSuggestion?>(null);
    public Task<float[]?[]> EmbedAsync(IReadOnlyList<string> texts, bool query, CancellationToken ct) => Task.FromResult(new float[]?[texts.Count]);
}
