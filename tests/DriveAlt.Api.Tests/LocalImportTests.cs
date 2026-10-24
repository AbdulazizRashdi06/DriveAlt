using DriveAlt.Api.Ai;
using DriveAlt.Api.Data;
using DriveAlt.Api.Import;

namespace DriveAlt.Api.Tests;

public class LocalImportTests
{
    [Fact]
    public void Render_pixel_boxes_become_padded_page_fractions()
    {
        // 1QXl page 1 as reviewed: render 2472x3197.
        var page = new LocalImporter.CropPage("assessment",
            [new("Exercise 1", [180, 336, 2295, 1620], "Exercise 1: Indicate whether ...")], 2472, 3197);

        var r = Assert.Single(LocalImporter.ToRegions(page));
        var pad = (float)MaterialAi.BoxPadding;
        Assert.Equal(RegionLabels.Question, r.Label);
        Assert.Equal(180f / 2472 - pad, r.X, 3);
        Assert.Equal(336f / 3197 - pad, r.Y, 3);
        Assert.Equal((2295f - 180) / 2472 + 2 * pad, r.W, 3);
        Assert.Equal((1620f - 336) / 3197 + 2 * pad, r.H, 3);
        Assert.Equal("Exercise 1", r.QuestionNumber);
    }

    [Fact]
    public void Boxes_past_the_render_are_clamped_and_tiny_ones_dropped()
    {
        // 1vyz page 4: the reviewed box runs past its 765x991 render.
        var page = new LocalImporter.CropPage("assessment",
            [new("3", [130, 190, 800, 1000], "Question 3"), new("x", [10, 10, 12, 12], "dot")], 765, 991);

        var r = Assert.Single(LocalImporter.ToRegions(page));
        Assert.Equal(1f, r.X + r.W, 3);
        Assert.Equal(1f, r.Y + r.H, 3);
    }

    [Fact]
    public void Control_characters_from_math_fonts_are_dropped()
    {
        Assert.Equal("P 1 A and B 2  0\nnext",LocalImporter.Clean("P 1 A and B 2 \u0002 0\u0007\nnext"));
    }
}
