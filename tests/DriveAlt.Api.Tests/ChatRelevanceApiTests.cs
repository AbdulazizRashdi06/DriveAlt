using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DriveAlt.Api.Data;
using DriveAlt.Api.Processing;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Tests;

public partial class ApiTests
{
    /// <summary>
    /// Search always returns the nearest material, so the model gets evidence of what each result really contains
    /// (matched_keywords) and a note that being returned is not a match. The server still filters nothing: the model's
    /// picks are shown as they are.
    /// </summary>
    [Fact]
    public async Task Search_results_tell_the_model_which_of_its_keywords_each_result_contains()
    {
        var a = "rel" + Guid.NewGuid().ToString("N")[..12];
        var b = "rel" + Guid.NewGuid().ToString("N")[..12];
        Guid cropA, cropB;
        await app.SignInAsync(ApiFactory.Uploader);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var file = new DocFile
            {
                Title = "Relevance fixture", OriginalName = "relevance.pdf", Sha256 = Guid.NewGuid().ToString(),
                Course = await db.Courses.SingleAsync(c => c.Slug == "data-structures"),
                UploadedById = (await db.Users.SingleAsync(u => u.UniId == ApiFactory.Uploader)).Id,
                Status = FileStatuses.Ready, MaterialType = MaterialTypes.PastPaper, AcademicYear = 2029,
            };
            var page = new Page { File = file, Number = 1, Kind = PageKinds.Assessment, Text = $"{a} {b}", Extracted = true, Width = 600, Height = 800 };
            var ra = new Region { Page = page, Label = RegionLabels.Question, Text = $"Question about {a}", W = .8f, H = .3f };
            var rb = new Region { Page = page, Label = RegionLabels.Question, Text = $"Question about {b}", Y = .4f, W = .8f, H = .3f };
            page.Regions.AddRange([ra, rb]);
            file.Pages.Add(page);
            file.PageCount = 1;
            IndexText.Refresh(file, page);
            db.Files.Add(file);
            await db.SaveChangesAsync();
            (cropA, cropB) = (ra.Id, rb.Id);
        }

        var student = await app.SignInAsync("25-8894");
        var chat = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        app.ChatModel.EnqueueTool("search_material", new JsonObject
        {
            ["meaning"] = "questions about either fixture topic", ["keywords"] = new JsonArray(a, b),
            ["course"] = "data-structures", ["want"] = "questions", ["year"] = 2029,
        });
        JsonObject? tool = null;
        string? system = null;
        app.ChatModel.Enqueue(request =>
        {
            system = request["messages"]![0]!["content"]!.GetValue<string>();
            tool = JsonNode.Parse(request["messages"]!.AsArray().Last(m => m?["role"]?.GetValue<string>() == "tool")!["content"]!.GetValue<string>())!.AsObject();
            // The model picks only the crop that matches; the app must show exactly that.
            return ScriptedChatModel.Tool("reply", new JsonObject
            {
                ["commentary"] = "Here's the one about the first topic.", ["result_refs"] = new JsonArray($"r:{cropA}"),
                ["refused"] = false, ["more_commentary"] = new JsonArray(),
            });
        });
        var reply = await SendAsync(student, chat, "questions about the first topic", prepared: true);

        var results = tool!["results"]!.AsArray().ToDictionary(r => r!["ref"]!.GetValue<string>(), r => r!["matched_keywords"]!.AsArray().Select(k => k!.GetValue<string>()).ToList());
        Assert.Equal([a], results[$"r:{cropA}"]);
        Assert.Equal([b], results[$"r:{cropB}"]);
        Assert.Contains("offering and showing are separate turns", system);
        Assert.Contains("not confirmed matches", tool["note"]!.GetValue<string>());
        Assert.Equal($"r:{cropA}", Assert.Single(reply["results"]!.AsArray())!["ref"]!.GetValue<string>());
    }
}
