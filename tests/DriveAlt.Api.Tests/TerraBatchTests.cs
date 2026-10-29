using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DriveAlt.Api.Ai;
using DriveAlt.Api.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DriveAlt.Api.Tests;

public class TerraBatchTests
{
    private static JsonObject Page(int number = 1, int width = 1000) => JsonNode.Parse($$"""
        {"page":{{number}},"width":{{width}},"height":1400,"page_kind":"assessment","content_status":"content",
         "text":"","topic":"queues","tags":["queues"],"academic_year":null,"assessment_label":null,
         "needs_review":false,"regions":[{"question_number":"1","text":"Describe a queue.","bbox":[100,200,900,700]}]}
        """)!.AsObject();

    private static IReadOnlyList<PageExtraction> Parse(params JsonObject[] pages) => MaterialAi.ValidateBatch(
        new JsonObject { ["pages"] = new JsonArray(pages.Cast<JsonNode>().ToArray()) }.ToJsonString(),
        Enumerable.Range(1, pages.Length).Select(n => (n, 1000, 1400)).ToList(), "gpt-6.1-terra", "low");

    [Fact]
    public void Batch_boxes_use_the_actual_image_size_and_question_text_is_composed_once()
    {
        var result = Assert.Single(Parse(Page()));
        Assert.Equal("Describe a queue.", result.Text);
        Assert.Equal(.085f, Assert.Single(result.Regions).X, 3);
        Assert.False(result.NeedsReview);
        Assert.Contains("terra-batch-v1", result.RawJson);
        Assert.Contains("gpt-6.1-terra", result.RawJson);
    }

    [Fact]
    public void Slides_without_regions_keep_text_and_empty_content_is_held_for_review()
    {
        var page = Page(); page["page_kind"] = "slide"; page["regions"] = new JsonArray(); page["text"] = "Introduction to linked lists";
        Assert.Equal("Introduction to linked lists", Assert.Single(Parse(page)).Text);
        page = Page(); page["page_kind"] = "slide"; page["regions"] = new JsonArray();
        Assert.True(Assert.Single(Parse(page)).NeedsReview);
        page = Page(); page["regions"] = new JsonArray(); page["content_status"] = "answer_only";
        Assert.False(Assert.Single(Parse(page)).NeedsReview);
    }

    [Fact]
    public void Missing_duplicate_or_shifted_pages_and_wrong_dimensions_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Page(2)));
        Assert.Throws<InvalidDataException>(() => Parse(Page(1), Page(1)));
        Assert.Throws<InvalidDataException>(() => Parse(Page(1, 500)));
        Assert.Throws<InvalidDataException>(() => MaterialAi.ValidateBatch("{\"pages\":[]}", [(1,1000,1400)], "terra", "low"));
    }

    [Theory]
    [InlineData("[-1,20,900,700]")]
    [InlineData("[100,200,1001,700]")]
    [InlineData("[100,200,900,1401]")]
    [InlineData("[900,200,100,700]")]
    [InlineData("[100,200,100,700]")]
    [InlineData("[1,2,3]")]
    public void Bad_boxes_are_rejected_before_any_page_is_applied(string bbox)
    {
        var page = Page(); page["regions"]![0]!["bbox"] = JsonNode.Parse(bbox);
        Assert.Throws<InvalidDataException>(() => Parse(page));
    }

    [Fact]
    public void Producer_review_flags_and_unreadable_question_wording_are_preserved()
    {
        var flagged = Page(); flagged["needs_review"] = true;
        Assert.True(Assert.Single(Parse(flagged)).NeedsReview);
        var empty = Page(); empty["regions"]![0]!["text"] = "";
        Assert.True(Assert.Single(Parse(empty)).NeedsReview);
    }

    [Fact]
    public async Task Batch_request_uses_terra_low_and_all_eight_images()
    {
        JsonObject? captured = null;
        var handler = new Handler(async request =>
        {
            captured = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}") };
        });
        var client = new CodexClient(new HttpClient(handler), Options.Create(new CodexOptions { BaseUrl = "http://test.invalid/v1" }), NullLogger<CodexClient>.Instance);
        await client.BatchJsonAsync("system", "worklist", Enumerable.Range(0,8).Select(_ => new byte[] {1,2,3}).ToList(), new JsonObject { ["type"] = "object" }, default);
        Assert.Equal("gpt-6.1-terra", captured!["model"]!.GetValue<string>());
        Assert.Equal("low", captured["reasoning_effort"]!.GetValue<string>());
        Assert.Equal(9, captured["messages"]![1]!["content"]!.AsArray().Count);
    }

    [Fact]
    public async Task Batch_quota_limits_still_pause_instead_of_falling_back()
    {
        var client = new CodexClient(new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        { Content = new StringContent("quota") }))), Options.Create(new CodexOptions { BaseUrl = "http://test.invalid/v1" }), NullLogger<CodexClient>.Instance);
        await Assert.ThrowsAsync<AiUnavailableException>(() => client.BatchJsonAsync("system", "worklist", [new byte[] {1}], new JsonObject { ["type"] = "object" }, default));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request);
    }
}
