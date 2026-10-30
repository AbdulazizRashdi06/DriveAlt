using System.Text.Json.Nodes;
using DriveAlt.Api.Ai;

namespace DriveAlt.Api.Tests;

public class CodexTests
{
    [Fact]
    public void Pixel_boxes_become_normalized_box_2d()
    {
        var j = JsonNode.Parse("""
            { "regions": [
              { "label": "question", "bbox": [45, 68, 407, 170], "question_number": "1", "text": "Q1" },
              { "label": "answer", "bbox": [400, 300, 300, 200], "question_number": null, "text": "" }
            ] }
            """)!;

        var regions = MaterialAi.PixelBoxesToNormalized(j, 452, 678)["regions"]!.AsArray();

        var q = regions[0]!.AsObject();
        Assert.False(q.ContainsKey("bbox"));
        Assert.Equal([100, 100, 251, 900], q["box_2d"]!.AsArray().Select(v => v!.GetValue<int>()));

        // Reversed corners still parse: FromJson takes min/max of each axis, then pads every side.
        var x = MaterialAi.FromJson(MaterialAi.PixelBoxesToNormalized(JsonNode.Parse("""
            { "page_kind": "assessment", "regions": [ { "label": "question", "bbox": [400, 300, 300, 200], "text": "" } ] }
            """)!, 452, 678), "{}");
        var a = Assert.Single(x.Regions);
        const float pad = (float)MaterialAi.BoxPadding;
        Assert.Equal(300 / 452f - pad, a.X, 2);
        Assert.Equal(200 / 678f - pad, a.Y, 2);
        Assert.Equal(100 / 452f + 2 * pad, a.W, 2);
        Assert.Equal(100 / 678f + 2 * pad, a.H, 2);
    }

    [Fact]
    public void Padding_never_leaves_the_page()
    {
        var x = MaterialAi.FromJson(JsonNode.Parse("""
            { "page_kind": "assessment", "regions": [ { "label": "question", "box_2d": [0, 0, 1000, 1000], "text": "" } ] }
            """)!, "{}");
        var a = Assert.Single(x.Regions);
        Assert.Equal((0f, 0f, 1f, 1f), (a.X, a.Y, a.W, a.H));
    }

    [Fact]
    public void Extraction_prompt_lets_crops_show_answers_but_never_write_them()
    {
        var p = MaterialAi.ExtractInstructions;
        Assert.Contains("answers included", p);
        Assert.DoesNotContain("Do NOT include the answer", p);
        Assert.Contains("never write out, paraphrase or hint at an answer", p);
        Assert.Contains("Answers may be SHOWN in the crop, never WRITTEN", MaterialAi.BatchInstructions);
    }

    [Fact]
    public void Usage_limits_are_found_even_when_wrapped()
    {
        var limit = new AiUnavailableException("Codex unavailable (429)");
        Assert.True(AiUnavailableException.In(limit));
        Assert.True(AiUnavailableException.In(new AggregateException(new TaskCanceledException(), limit)));
        Assert.True(AiUnavailableException.In(new InvalidOperationException("outer", limit)));
        Assert.False(AiUnavailableException.In(new InvalidOperationException("Codex gateway error 502")));
    }
}
