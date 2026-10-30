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

    private async Task<KindFixture> KindMaterialAsync(string assessmentType = MaterialTypes.PastPaper)
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
            // Keep these fixtures outside the existing 2026 whole-paper inventory test.
            AcademicYear = 2028, AssessmentLabel = "Contract fixture",
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
        var paper = File(assessmentType);
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
        ["year"] = 2028, ["assessment_label"] = "Contract fixture",
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
    [InlineData("none", true)]
    public async Task Replies_enforce_the_model_declared_kind_without_keyword_overrides(string declaration, bool includeQuestion)
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
        var expected = declaration == "slides" ? ResultRefs.File(f.Deck, 2)
            : declaration == "questions" && includeQuestion ? ResultRefs.Region(f.Crop) : null;
        Assert.Equal(expected is null ? 0 : 1, reply["results"]!.AsArray().Count);
        if (expected is not null) Assert.Equal(expected, reply["results"]![0]!["ref"]!.GetValue<string>());
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

    [Theory]
    [InlineData("Send me exercise sheet 6", "files", "exercise")]
    [InlineData("exercise 6 pdf", "files", "exercise")]
    [InlineData("lecture notes on hashing", "notes_pages", "notes")]
    [InlineData("Lecture 5 notes", "notes_pages", "notes")]
    [InlineData("Send me the questions pdf", "files", "exercise")]
    [InlineData("I don't want slides, show questions", "questions", "exercise")]
    [InlineData("\u0645\u0627 \u0623\u0628\u063a\u0649 \u0633\u0644\u0627\u064a\u062f\u0627\u062a\u060c \u0623\u0628\u063a\u0649 \u0623\u0633\u0626\u0644\u0629", "questions", "exercise")]
    [InlineData("\u0623\u0631\u0633\u0644 \u0644\u064a \u0645\u0644\u0641 \u0627\u0644\u0623\u0633\u0626\u0644\u0629 PDF", "files", "exercise")]
    [InlineData("questions", "files", "exercise")]
    public async Task File_and_notes_wording_does_not_override_the_requested_kind(string text, string want, string type)
    {
        var f = await KindMaterialAsync(MaterialTypes.Exercise);
        var student = await app.SignInAsync("25-8894");
        var chat = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        var args = SearchKind(f, want);
        args["material_type"] = type;
        app.ChatModel.EnqueueTool("search_material", args);
        app.ChatModel.Enqueue(request => ScriptedChatModel.Tool("reply", KindReply(want,
            SearchOutput(request).Select(r => r!["ref"]!.GetValue<string>()).ToArray())));
        var reply = await SendAsync(student, chat, text, prepared: true);
        var results = reply["results"]!.AsArray();
        Assert.NotEmpty(results);
        Assert.All(results, card => Assert.Equal(type, card!["materialType"]!.GetValue<string>()));
        if (want == "questions")
        {
            Assert.Equal(new[] { ResultRefs.Region(f.Crop), ResultRefs.Page(f.Bare) }.Order(),
                results.Select(r => r!["ref"]!.GetValue<string>()).Order());
        }
        else
        {
            var card = Assert.Single(results)!;
            Assert.Equal(want == "files" ? "file" : "page", card["kind"]!.GetValue<string>());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Typed_followups_use_the_current_model_kind_without_reinterpreting_words(bool pick)
    {
        var f = await KindMaterialAsync();
        var student = await app.SignInAsync("25-8894");
        var chat = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            db.Messages.Add(new Message { ConversationId = Guid.Parse(chat), Role = "bot", Text = "Earlier material",
                Results = [ResultRefs.File(f.Deck, 2)],
                More = [ResultRefs.File(f.Deck, 2), ResultRefs.Region(f.Crop), ResultRefs.Page(f.Bare)],
                Options = ChatOption.Write([new() { Label = "Earlier search", Want = "slides", Done = true,
                    Refs = [ResultRefs.File(f.Deck, 2), ResultRefs.Region(f.Crop)] }]) });
            await db.SaveChangesAsync();
        }
        var response = KindReply("questions");
        if (pick) response["option"] = 0;
        else response["show_more"] = true;
        app.ChatModel.EnqueueTool("reply", response);
        var reply = await SendAsync(student, chat, "I don't want slides", prepared: true);
        var refs = reply["results"]!.AsArray().Select(r => r!["ref"]!.GetValue<string>()).ToList();
        Assert.Contains(ResultRefs.Region(f.Crop), refs);
        Assert.DoesNotContain(ResultRefs.File(f.Deck, 2), refs);
        Assert.Equal(pick ? 1 : 2, refs.Count);
    }
}
