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

        // Reversed corners still parse: FromJson takes min/max of each axis.
        var x = MaterialAi.FromJson(MaterialAi.PixelBoxesToNormalized(JsonNode.Parse("""
            { "page_kind": "assessment", "regions": [ { "label": "question", "bbox": [400, 300, 300, 200], "text": "" } ] }
            """)!, 452, 678), "{}");
        var a = Assert.Single(x.Regions);
        Assert.Equal(300 / 452f, a.X, 2);
        Assert.Equal(200 / 678f, a.Y, 2);
        Assert.Equal(100 / 452f, a.W, 2);
        Assert.Equal(100 / 678f, a.H, 2);
    }

    [Fact]
    public void Codex_prompt_asks_for_pixel_boxes()
    {
        var p = MaterialAi.PixelBoxInstructions(586, 760);
        Assert.Contains("bbox is [x0, y0, x1, y1] in pixels of this image, which is 586x760 pixels", p);
        Assert.DoesNotContain("box_2d", p);
        Assert.DoesNotContain("normalized to 0–1000", p);
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
