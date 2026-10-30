using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DriveAlt.Api.Chat;
using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using DriveAlt.Api.Processing;
using DriveAlt.Api.Search;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Tests;

public partial class ApiTests
{
    private sealed record KindFixture(string Topic, Guid Deck, Guid Slide, Guid Crop, Guid Bare, Guid Boxed, Guid Note);

    private async Task<KindFixture> KindMaterialAsync()
    {
        await app.SignInAsync(ApiFactory.Uploader);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var course = await db.Courses.SingleAsync(c => c.Slug == "data-structures");
        var owner = await db.Users.SingleAsync(u => u.UniId == ApiFactory.Uploader);
        var topic = "kind" + Guid.NewGuid().ToString("N");
        DocFile File(string type) => new()
        {
            Title = "Material kind fixture", OriginalName = "fixture.pdf", Sha256 = Guid.NewGuid().ToString(),
            Course = course, UploadedById = owner.Id, Status = FileStatuses.Ready, MaterialType = type,
            AcademicYear = 2026, AssessmentLabel = "Contract fixture",
        };
        Page Page(DocFile file, int number, string kind, string text)
        {
            var page = new Page { File = file, Number = number, Kind = kind, Text = text, Extracted = true, Width = 600, Height = 800 };
            file.Pages.Add(page);
            return page;
        }
        var deck = File(MaterialTypes.Slides);
        Page(deck, 1, PageKinds.Slide, "Introduction to this course");
        var slide = Page(deck, 2, PageKinds.Slide, topic);
        // A question-shaped box in slides must not leak into question searches either.
        slide.Regions.Add(new Region { Page = slide, Label = RegionLabels.Question, Text = topic, W = .8f, H = .5f });
        var paper = File(MaterialTypes.PastPaper);
        var boxed = Page(paper, 1, PageKinds.Assessment, topic);
        var crop = new Region { Page = boxed, Label = RegionLabels.Question, Text = topic, W = .8f, H = .5f };
        boxed.Regions.Add(crop);
        var bare = Page(paper, 2, PageKinds.Assessment, topic);
        // The page matches but its existing question crop doesn't: don't fall back to the whole boxed page.
        var unmatchedCropPage = Page(paper, 3, PageKinds.Assessment, topic);
        unmatchedCropPage.Regions.Add(new Region { Page = unmatchedCropPage, Label = RegionLabels.Question, Text = "Unrelated subject", W = .8f, H = .5f });
        var notes = File(MaterialTypes.Notes);
        var note = Page(notes, 1, PageKinds.Notes, topic);
        foreach (var file in new[] { deck, paper, notes })
        {
            file.PageCount = file.Pages.Count;
            foreach (var page in file.Pages) IndexText.Refresh(file, page);
            db.Files.Add(file);
        }
        await db.SaveChangesAsync();
        return new(topic, deck.Id, slide.Id, crop.Id, bare.Id, boxed.Id, note.Id);
    }

    private static JsonObject SearchKind(KindFixture f, string want) => new()
    {
        ["query"] = f.Topic, ["course"] = "data-structures", ["want"] = want,
        ["year"] = 2026, ["assessment_label"] = "Contract fixture",
    };

    private static JsonObject KindReply(string kind, params string[] refs) => new()
    {
        ["commentary"] = "No matching questions yet. Want the lecture instead?", ["asked_for"] = kind,
        ["result_refs"] = new JsonArray(refs.Select(r => (JsonNode)r).ToArray()), ["refused"] = false,
        ["more_commentary"] = new JsonArray("More original material.", "Another batch.", "More from the search."),
    };

    private static JsonArray SearchOutput(JsonObject request) => JsonNode.Parse(request["messages"]!.AsArray()
        .Last(m => m?["role"]?.GetValue<string>() == "tool")!["content"]!.GetValue<string>())!["results"]!.AsArray();

    [Theory]
    [InlineData("questions", true)]
    [InlineData("slides", true)]
    [InlineData("questions", false)]
    public async Task Replies_drop_wrong_kinds_even_when_the_model_misreads_the_request(string declaration, bool includeQuestion)
    {
        var f = await KindMaterialAsync();
        var student = await app.SignInAsync("25-8894");
        var chat = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        // Authorize each ref through searches first; provenance alone must not let a wrong kind through.
        app.ChatModel.EnqueueTool("search_material", SearchKind(f, "slide_pages"));
        app.ChatModel.EnqueueTool("search_material", SearchKind(f, "questions"));
        app.ChatModel.EnqueueTool("search_material", SearchKind(f, "slides"));
        app.ChatModel.EnqueueTool("reply", KindReply(declaration, includeQuestion
            ? [ResultRefs.Page(f.Slide), ResultRefs.File(f.Deck, 2), ResultRefs.Region(f.Crop)]
            : [ResultRefs.Page(f.Slide), ResultRefs.File(f.Deck, 2)]));
        var reply = await SendAsync(student, chat, "questions on " + f.Topic, prepared: true);
        Assert.Equal(includeQuestion ? 1 : 0, reply["results"]!.AsArray().Count);
        if (includeQuestion) Assert.Equal(ResultRefs.Region(f.Crop), reply["results"]![0]!["ref"]!.GetValue<string>());
        Assert.Equal("No matching questions yet. Want the lecture instead?", reply["text"]!.GetValue<string>());
        Assert.Equal(0, reply["moreCount"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("questions")]
    [InlineData("slides")]
    [InlineData("slide_pages")]
    [InlineData("notes_pages")]
    [InlineData("files")]
    public async Task Each_want_searches_only_its_kind_and_slide_decks_keep_the_best_page(string want)
    {
        var f = await KindMaterialAsync();
        var student = await app.SignInAsync("25-8894");
        var chat = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        app.ChatModel.EnqueueTool("search_material", SearchKind(f, want));
        app.ChatModel.Enqueue(request =>
        {
            var results = SearchOutput(request);
            Assert.NotEmpty(results);
            var refs = results.Select(r => r!["ref"]!.GetValue<string>()).ToArray();
            switch (want)
            {
                case "questions":
                    Assert.Equal(new[] { ResultRefs.Region(f.Crop), ResultRefs.Page(f.Bare) }.Order(), refs.Order());
                    Assert.DoesNotContain(ResultRefs.Page(f.Boxed), refs);
                    break;
                case "slides":
                    Assert.Equal(ResultRefs.File(f.Deck, 2), Assert.Single(refs));
                    Assert.Equal(2, results[0]!["page"]!.GetValue<int>());
                    break;
                case "slide_pages": Assert.Equal(ResultRefs.Page(f.Slide), Assert.Single(refs)); break;
                case "notes_pages": Assert.Equal(ResultRefs.Page(f.Note), Assert.Single(refs)); break;
                case "files": Assert.All(refs, r => Assert.StartsWith("f:", r)); Assert.DoesNotContain(refs, r => r.StartsWith(ResultRefs.File(f.Deck))); break;
            }
            return ScriptedChatModel.Tool("reply", KindReply(want, refs));
        });
        var text = want == "slide_pages" ? "the slide with " + f.Topic : want == "slides" ? "slides about " + f.Topic : f.Topic;
        var reply = await SendAsync(student, chat, text, prepared: true);
        Assert.NotEmpty(reply["results"]!.AsArray());
        if (want == "slides")
        {
            Assert.Equal("file", reply["results"]![0]!["kind"]!.GetValue<string>());
            Assert.Equal(2, reply["results"]![0]!["pageNumber"]!.GetValue<int>());
            var history = await student.GetFromJsonAsync<JsonObject>($"/api/chats/{chat}");
            Assert.Equal(2, history!["messages"]!.AsArray().Last()!["results"]![0]!["pageNumber"]!.GetValue<int>());
        }
    }

    [Fact]
    public async Task Legacy_file_option_still_returns_whole_files()
    {
        var f = await KindMaterialAsync();
        var student = await app.SignInAsync("25-8894");
        var chat = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            db.Messages.Add(new Message { ConversationId = Guid.Parse(chat), Role = "bot", Text = "Which material?",
                Options = new JsonArray(new JsonObject {
                    ["label"] = "Past papers", ["query"] = f.Topic, ["files"] = true, ["type"] = "past_paper",
                    ["foundCommentary"] = "Your original papers.", ["emptyCommentary"] = "Nothing yet.",
                    ["moreCommentary"] = new JsonArray("Next.", "More.", "Another."),
                }).ToJsonString() });
            await db.SaveChangesAsync();
        }
        var before = app.ChatModel.RequestCount;
        var reply = await SendAsync(student, chat, "Past papers", new { option = 0 });
        Assert.NotEmpty(reply["results"]!.AsArray());
        Assert.All(reply["results"]!.AsArray(), r => Assert.Equal("file", r!["kind"]!.GetValue<string>()));
        Assert.Equal(before, app.ChatModel.RequestCount);
    }

    [Fact]
    public async Task Saved_question_options_and_more_revalidate_real_ref_kinds()
    {
        var f = await KindMaterialAsync();
        var student = await app.SignInAsync("25-8894");
        var chat = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            db.Messages.Add(new Message { ConversationId = Guid.Parse(chat), Role = "bot", Text = "Questions?",
                Options = ChatOption.Write([new() { Label = "Questions", Want = "questions", Done = true,
                    Refs = [ResultRefs.Page(f.Slide), ResultRefs.Region(f.Crop), ResultRefs.File(f.Deck, 2)],
                    FoundCommentary = "Prepared question line.", EmptyCommentary = "No questions yet.", MoreCommentary = ["More.", "Next.", "Another."] }]) });
            await db.SaveChangesAsync();
        }
        var picked = await SendAsync(student, chat, "Questions", new { option = 0 });
        Assert.Equal(ResultRefs.Region(f.Crop), Assert.Single(picked["results"]!.AsArray())!["ref"]!.GetValue<string>());
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            db.Messages.Add(new Message { ConversationId = Guid.Parse(chat), Role = "bot", Text = "Original questions",
                Results = [ResultRefs.Region(f.Crop)], More = [ResultRefs.File(f.Deck, 2), ResultRefs.Page(f.Slide), ResultRefs.Page(f.Bare)],
                MoreCommentary = ["Prepared more line."] });
            await db.SaveChangesAsync();
        }
        var before = app.ChatModel.RequestCount;
        var more = await SendAsync(student, chat, "Show more", new { more = true });
        Assert.Equal(ResultRefs.Page(f.Bare), Assert.Single(more["results"]!.AsArray())!["ref"]!.GetValue<string>());
        Assert.Equal(before, app.ChatModel.RequestCount);
    }

    [Fact]
    public async Task Ambiguous_kind_options_search_separately_and_only_show_the_selected_kind()
    {
        var f = await KindMaterialAsync();
        var student = await app.SignInAsync("25-8894");
        var chat = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        JsonObject Option(string want) => new()
        {
            ["label"] = want, ["want"] = want, ["query"] = f.Topic, ["course"] = "data-structures",
            ["found_commentary"] = "Here is the material you selected.", ["empty_commentary"] = "Nothing found yet.",
            ["more_commentary"] = new JsonArray("More.", "Next.", "Another."),
        };
        app.ChatModel.EnqueueTool("ask", new JsonObject {
            ["question"] = "Searching both. Slides or questions?", ["options"] = new JsonArray(Option("slides"), Option("questions")),
        });
        var ask = await SendAsync(student, chat, f.Topic + " stuff", prepared: true);
        Assert.Empty(ask["results"]!.AsArray());
        var history = await student.GetFromJsonAsync<JsonObject>($"/api/chats/{chat}");
        var options = history!["messages"]!.AsArray().Last()!["options"]!.AsArray();
        Assert.Equal(1, options[0]!["count"]!.GetValue<int>());
        Assert.Equal(2, options[1]!["count"]!.GetValue<int>());
        var before = app.ChatModel.RequestCount;
        var picked = await SendAsync(student, chat, "slides", new { option = 0 });
        Assert.Equal(before, app.ChatModel.RequestCount);
        var deck = Assert.Single(picked["results"]!.AsArray())!;
        Assert.Equal("file", deck["kind"]!.GetValue<string>());
        Assert.Equal(2, deck["pageNumber"]!.GetValue<int>());
    }
}
