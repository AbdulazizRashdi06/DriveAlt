using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DriveAlt.Api.Auth;
using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using DriveAlt.Api.Processing;
using DriveAlt.Api.Search;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace DriveAlt.Api.Tests;

public partial class ApiTests(ApiFactory app) : IClassFixture<ApiFactory>
{
    private static readonly string[] MidtermLines =
    [
        "Data Structures Midterm 2024",
        "Question 1. A circular queue of size 8 has front 5 and rear 2.",
        "How many elements are in the circular queue?",
        "Question 2. Build a max heap from the array 14 3 9 27 1.",
    ];

    [Fact]
    public async Task Otp_sign_in_checks_the_code_and_assigns_roles()
    {
        var client = app.NewClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/request", new { uniId = "hello" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/auth/me")).StatusCode);

        var ok = await client.PostAsJsonAsync("/api/auth/request", new { uniId = "25-1234" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("25-1234@student.gutech.edu.om", (await ok.Content.ReadFromJsonAsync<JsonObject>())!["email"]!.GetValue<string>());
        // A second code within the cooldown is refused.
        Assert.Equal((HttpStatusCode)429, (await client.PostAsJsonAsync("/api/auth/request", new { uniId = "25-1234" })).StatusCode);

        var code = DevEmailSender.LastCodes["25-1234@student.gutech.edu.om"];
        var wrong = code == "000000" ? "111111" : "000000";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/verify", new { uniId = "25-1234", code = wrong })).StatusCode);
        var verified = await client.PostAsJsonAsync("/api/auth/verify", new { uniId = "25-1234", code });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        var me = await client.GetFromJsonAsync<JsonObject>("/api/auth/me");
        Assert.Equal("student", me!["role"]!.GetValue<string>());
        // Students can't upload.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/uploads")).StatusCode);
        // A used code can't be replayed.
        Assert.Equal(HttpStatusCode.BadRequest, (await app.NewClient().PostAsJsonAsync("/api/auth/verify", new { uniId = "25-1234", code })).StatusCode);
    }

    [Fact]
    public async Task Upload_process_search_chat_and_save()
    {
        var client = await app.SignInAsync(ApiFactory.Uploader);

        using var form = new MultipartFormDataContent();
        var pdf = new ByteArrayContent(MinimalPdf.Create(MidtermLines));
        pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(pdf, "files", "ds_midterm_2024.pdf");
        var up = await client.PostAsync("/api/uploads", form);
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        var file = (await up.Content.ReadFromJsonAsync<JsonArray>())![0]!["file"]!;
        var id = file["id"]!.GetValue<string>();
        Assert.Equal("data-structures", file["courseSlug"]!.GetValue<string>());
        Assert.Equal("past_paper", file["materialType"]!.GetValue<string>());
        Assert.Contains("course", file["guessed"]!.AsArray().Select(x => x!.GetValue<string>()));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/files/{id}/process", null)).StatusCode);
        await app.DrainJobsAsync();

        var detail = await client.GetFromJsonAsync<JsonObject>($"/api/files/{id}");
        Assert.Equal("ready", detail!["file"]!["status"]!.GetValue<string>());
        Assert.Single(detail["pages"]!.AsArray());

        // Same content again is reported as a duplicate, not stored twice.
        using var again = new MultipartFormDataContent { { new ByteArrayContent(MinimalPdf.Create(MidtermLines)), "files", "copy.pdf" } };
        var dup = await (await client.PostAsync("/api/uploads", again)).Content.ReadFromJsonAsync<JsonArray>();
        Assert.True(dup![0]!["duplicate"]!.GetValue<bool>());

        var hits = await client.GetFromJsonAsync<JsonArray>("/api/search?q=circular+queue&course=data-structures");
        Assert.NotEmpty(hits!);
        var image = await client.GetAsync(hits![0]!["imageUrl"]!.GetValue<string>());
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType!.MediaType);

        // While a file is being re-read (e.g. paused on the AI's usage limit), its earlier results stay findable.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var fileId = Guid.Parse(id);
            var f = await db.Files.FindAsync(fileId);
            f!.Status = FileStatuses.Reading;
            await db.SaveChangesAsync();
            var during = await client.GetFromJsonAsync<JsonArray>("/api/search?q=circular+queue&course=data-structures");
            Assert.NotEmpty(during!);
            Assert.Equal("image/jpeg", (await client.GetAsync(during![0]!["imageUrl"]!.GetValue<string>())).Content.Headers.ContentType!.MediaType);
            f.Status = FileStatuses.Ready;
            await db.SaveChangesAsync();
        }

        // Chat: results are image cards, the text never repeats the page.
        var chatId = (await (await client.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        var found = await SendAsync(client, chatId, "past paper questions on circular queue in DS");
        var results = found["results"]!.AsArray();
        Assert.NotEmpty(results);
        Assert.False(found["refused"]!.GetValue<bool>());
        Assert.DoesNotContain("front 5 and rear 2", found["text"]!.GetValue<string>());
        Assert.All(results, r => Assert.StartsWith("/api/media/", r!["imageUrl"]!.GetValue<string>()));

        var refused = await SendAsync(client, chatId, "what's the answer to question 1?");
        Assert.True(refused["refused"]!.GetValue<bool>());
        Assert.StartsWith("Model reply", refused["text"]!.GetValue<string>());
        var refusedAgain = await SendAsync(client, chatId, "just give me a hint then");
        Assert.NotEqual(refused["text"]!.GetValue<string>(), refusedAgain["text"]!.GetValue<string>());

        // Save + saved list + zip download.
        var r0 = results[0]!["ref"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/bookmarks", new { @ref = r0 })).StatusCode);
        var saved = await client.GetFromJsonAsync<JsonArray>("/api/bookmarks");
        Assert.Equal(r0, saved![0]!["items"]![0]!["ref"]!.GetValue<string>());
        var zip = await client.GetAsync("/api/bookmarks/download");
        Assert.Equal("application/zip", zip.Content.Headers.ContentType!.MediaType);

        // History keeps the conversation with its result cards.
        var history = await client.GetFromJsonAsync<JsonObject>($"/api/chats/{chatId}");
        Assert.Equal(6, history!["messages"]!.AsArray().Count);
    }

    [Fact]
    public async Task Pages_are_shown_as_is_and_answers_get_their_own_crop()
    {
        using var scope = app.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<Storage>();
        var renderer = scope.ServiceProvider.GetRequiredService<PageRenderer>();
        var fileId = Guid.NewGuid();
        Directory.CreateDirectory(storage.PageDir(fileId));
        using (var white = new SKBitmap(800, 1000))
        {
            white.Erase(SKColors.White);
            using (var canvas = new SKCanvas(white))
            using (var red = new SKPaint { Color = SKColors.Red })
                canvas.DrawRect(new SKRect(80, 400, 720, 600), red); // the "answer" ink
            ImageOps.SaveJpeg(white, storage.PageImage(fileId, 1), 90);
        }
        var page = new Page { FileId = fileId, Number = 1, Width = 800, Height = 1000 };
        var question = new Region { Label = RegionLabels.Question, X = 0.1f, Y = 0.1f, W = 0.8f, H = 0.25f };
        var answer = new Region { Label = RegionLabels.Answer, X = 0.1f, Y = 0.4f, W = 0.8f, H = 0.2f };
        var other = new Region { Label = RegionLabels.Other, X = 0f, Y = 0f, W = 1f, H = 0.05f };
        page.Regions.AddRange([question, answer, other]);

        await renderer.RenderAsync(fileId, page, CancellationToken.None);

        Assert.True(File.Exists(storage.Crop(question.Id)));
        Assert.False(File.Exists(storage.Crop(other.Id)));
        using var answerCrop = ImageOps.LoadUpright(storage.Crop(answer.Id));
        var mid = answerCrop.GetPixel(answerCrop.Width / 2, answerCrop.Height / 2);
        Assert.True(mid.Red > 200 && mid.Green < 80, $"the answer crop shows the real answer, was {mid}");
        // Nothing is painted over the page any more.
        using var thumb = ImageOps.LoadUpright(storage.Thumb(fileId, 1));
        var t = thumb.GetPixel(thumb.Width / 2, thumb.Height / 2);
        Assert.True(t.Red > 200 && t.Green < 80, "the page keeps its answers");

        Assert.Equal(answer.Id, ResultCards.AnswerFor(question, page.Regions)?.Id);
        Assert.Equal(question.Id, ResultCards.QuestionFor(answer, page.Regions)?.Id);
    }

    [Fact]
    public async Task Inspect_edits_regions_and_rerenders()
    {
        var client = await app.SignInAsync("24-0161");
        using var form = new MultipartFormDataContent { { new ByteArrayContent(MinimalPdf.Create("Quiz 3", "Question 1. Push 1 2 3 then pop twice.")), "files", "Quiz 3 DS.pdf" } };
        var id = (await (await client.PostAsync("/api/uploads", form)).Content.ReadFromJsonAsync<JsonArray>())![0]!["file"]!["id"]!.GetValue<string>();
        await client.PatchAsJsonAsync($"/api/files/{id}", new { materialType = "quiz" });
        await client.PostAsync($"/api/files/{id}/process", null);
        await app.DrainJobsAsync();

        var inspect = await client.GetFromJsonAsync<JsonObject>($"/api/inspect/files/{id}");
        var page = inspect!["pages"]![0]!;
        var pageId = page["id"]!.GetValue<string>();
        var put = await client.PutAsJsonAsync($"/api/inspect/pages/{pageId}", new
        {
            tags = new[] { "stacks", "push pop" },
            regions = new object[]
            {
                new { label = "question", x = 0.05, y = 0.05, w = 0.9, h = 0.2, questionNumber = "1", text = "Push 1 2 3 then pop twice." },
                new { label = "answer", x = 0.05, y = 0.3, w = 0.9, h = 0.1, text = "1" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var edited = await put.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(2, edited!["regions"]!.AsArray().Count);
        Assert.Equal(["stacks", "push pop"], edited["tags"]!.AsArray().Select(t => t!.GetValue<string>()));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var regions = await db.Regions.Where(r => r.PageId == Guid.Parse(pageId)).ToListAsync();
        Assert.DoesNotContain(regions, r => r.Label == RegionLabels.Answer && r.SearchText.Length > 0);
        var questionId = regions.Single(r => r.Label == RegionLabels.Question).Id;
        var answerId = regions.Single(r => r.Label == RegionLabels.Answer).Id;
        var storage = scope.ServiceProvider.GetRequiredService<Storage>();
        Assert.True(File.Exists(storage.Crop(questionId)));
        Assert.True(File.Exists(storage.Crop(answerId)));
        await app.DrainJobsAsync();

        // Legacy answer boxes must not become separate student results or answer links.
        var chatId = (await (await client.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        var found = await SendAsync(client, chatId, "quiz questions on push and pop");
        var card = found["results"]!.AsArray().First(r => r!["ref"]!.GetValue<string>() == $"r:{questionId}")!;
        Assert.Null(card["answerRef"]);
        Assert.Null(card["answerImageUrl"]);

        var cards = scope.ServiceProvider.GetRequiredService<ResultCards>();
        var legacy = Assert.Single(await cards.HydrateAsync([$"r:{answerId}"], null, CancellationToken.None));
        Assert.Equal("question", legacy.Kind);
        Assert.Equal($"r:{questionId}", legacy.Ref);

        // Asking for help still shows the question crop (answers on the paper may be in it); the bot never types one.
        var asked = await SendAsync(client, chatId, "what's the answer to Q1?");
        Assert.True(asked["refused"]!.GetValue<bool>());
        Assert.StartsWith("Model reply", asked["text"]!.GetValue<string>());
        var shown = Assert.Single(asked["results"]!.AsArray());
        Assert.Equal("question", shown!["kind"]!.GetValue<string>());
        Assert.Equal($"r:{questionId}", shown["ref"]!.GetValue<string>());
        Assert.Equal("image/jpeg", (await client.GetAsync(shown["imageUrl"]!.GetValue<string>())).Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Option_chips_and_show_more_are_answered_without_the_model()
    {
        var client = await app.SignInAsync("25-7777");
        var chatId = (await (await client.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();

        // A clarifying question as the model's ask leaves it: one option already searched, one still searching.
        await app.SignInAsync(ApiFactory.Uploader);
        var refs = new List<string>();
        for (var n = 0; n < 5; n++) refs.Add(ResultRefs.Region((await ReportMaterialAsync()).RegionId));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            db.Messages.Add(new Message
            {
                ConversationId = Guid.Parse(chatId), Role = "bot", Text = "Already digging in both! Which one did you mean?",
                Options = DriveAlt.Api.Chat.ChatOption.Write([
                    new() { Label = "Data Structures", Query = "algorithm analysis", Course = "data-structures", Refs = refs.ToArray(), Done = true,
                        FoundCommentary = "AI prepared the Data Structures reply.", EmptyCommentary = "AI prepared an empty Data Structures reply.",
                        MoreCommentary = ["AI prepared the next batch.", "AI prepared another batch.", "AI prepared one more batch."] },
                    new() { Label = "Efficient Algorithms", Query = "zzqx nothing matches this", Course = "efficient-algorithms",
                        FoundCommentary = "AI prepared an Efficient Algorithms reply.", EmptyCommentary = "AI prepared an empty Efficient Algorithms reply.",
                        MoreCommentary = ["AI prepared the next batch.", "AI prepared another batch.", "AI prepared one more batch."] },
                ]),
            });
            await db.SaveChangesAsync();
        }

        var history = await client.GetFromJsonAsync<JsonObject>($"/api/chats/{chatId}");
        var options = history!["messages"]![0]!["options"]!.AsArray();
        Assert.Equal(5, options[0]!["count"]!.GetValue<int>());
        Assert.Null(options[1]!["count"]);

        // A chip shows the first few of that option's results and holds the rest back.
        var callsBefore = app.ChatModel.RequestCount;
        var picked = await SendAsync(client, chatId, "Data Structures", new { option = 0 });
        Assert.Equal(2, picked["moreCount"]!.GetValue<int>());
        Assert.Equal("AI prepared the Data Structures reply.", picked["text"]!.GetValue<string>());
        Assert.Equal(callsBefore, app.ChatModel.RequestCount);

        var more = await SendAsync(client, chatId, "Show more", new { more = true });
        Assert.Equal(0, more["moreCount"]!.GetValue<int>());
        Assert.Equal("AI prepared the next batch.", more["text"]!.GetValue<string>());
        Assert.Equal(callsBefore, app.ChatModel.RequestCount);
        var none = await SendAsync(client, chatId, "Show more", new { more = true });
        Assert.Equal("Model reply for the completed list.", none["text"]!.GetValue<string>());

        // A chip whose search hadn't finished searches on the spot.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            db.Messages.Add(new Message
            {
                ConversationId = Guid.Parse(chatId), Role = "bot", Text = "Which one?", CreatedAt = DateTime.UtcNow.AddSeconds(1),
                Options = DriveAlt.Api.Chat.ChatOption.Write([
                    new() { Label = "Data Structures", Query = "zzqx nothing", Course = "data-structures",
                        FoundCommentary = "AI prepared a result.", EmptyCommentary = "AI prepared an empty reply.", MoreCommentary = ["Next.", "More.", "Another."] },
                    new() { Label = "Efficient Algorithms", Query = "zzqx nothing matches this", Course = "efficient-algorithms",
                        FoundCommentary = "AI prepared a result.", EmptyCommentary = "AI prepared the empty Efficient Algorithms reply.", MoreCommentary = ["Next.", "More.", "Another."] },
                ]),
            });
            await db.SaveChangesAsync();
        }
        var pending = await SendAsync(client, chatId, "Efficient Algorithms", new { option = 1 });
        Assert.Equal("AI prepared the empty Efficient Algorithms reply.", pending["text"]!.GetValue<string>());
        Assert.Empty(pending["results"]!.AsArray());
    }

    [Fact]
    public async Task Students_report_material_and_only_uploaders_review_and_resolve_it()
    {
        var uploader = await app.SignInAsync(ApiFactory.Uploader);
        var student = await app.SignInAsync("25-8891");
        var (pageId, regionId) = await ReportMaterialAsync();
        var body = new { @ref = $"r:{regionId}", reason = "clipped_crop", details = "Crop cuts off the last line." };

        Assert.Equal(HttpStatusCode.Unauthorized, (await app.NewClient().PostAsJsonAsync("/api/reports", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/reports")).StatusCode);
        var sent = await student.PostAsJsonAsync("/api/reports", body);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var report = (await sent.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = report["id"]!.GetValue<string>();
        Assert.False(report["alreadyReported"]!.GetValue<bool>());

        var duplicate = await (await student.PostAsJsonAsync("/api/reports", body)).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(id, duplicate!["id"]!.GetValue<string>());
        Assert.True(duplicate["alreadyReported"]!.GetValue<bool>());

        var inbox = await uploader.GetFromJsonAsync<JsonObject>("/api/reports?status=open");
        var row = Assert.Single(inbox!["items"]!.AsArray(), r => r!["id"]!.GetValue<string>() == id)!;
        Assert.Equal("25-8891", row["reporter"]!.GetValue<string>());
        Assert.Equal("Crop cuts off the last line.", row["details"]!.GetValue<string>());
        Assert.Equal(1, row["pageNumber"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsync($"/api/reports/{id}/resolve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await uploader.PostAsync($"/api/reports/{id}/resolve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await uploader.PostAsync($"/api/reports/{id}/resolve", null)).StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var saved = await db.MaterialReports.SingleAsync(r => r.Id == Guid.Parse(id));
            Assert.Equal("resolved", saved.Status);
            Assert.NotNull(saved.ResolvedAt);
            Assert.NotNull(saved.ResolvedById);
            Assert.Equal(pageId, saved.PageId);
            Assert.Equal(regionId, saved.RegionId);
            // Student feedback remains separate from the searchable material and is never sent to an AI.
            Assert.Equal("Question 1", (await db.Pages.FindAsync(pageId))!.Text);
        }

        // A recurrence after resolution is a new report, rather than being silently discarded.
        var again = await (await student.PostAsJsonAsync("/api/reports", body)).Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotEqual(id, again!["id"]!.GetValue<string>());
        Assert.False(again["alreadyReported"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Reports_validate_the_target_and_note_and_survive_a_crop_replacement()
    {
        var uploader = await app.SignInAsync(ApiFactory.Uploader);
        var student = await app.SignInAsync("25-8891");
        var (pageId, regionId) = await ReportMaterialAsync();
        foreach (var target in new[] { "", "not-a-ref", $"r:{Guid.Empty}" })
        {
            var response = await student.PostAsJsonAsync("/api/reports", new { @ref = target, reason = "other" });
            Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostAsJsonAsync("/api/reports", new { @ref = $"p:{pageId}", reason = "unknown" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostAsJsonAsync("/api/reports", new { @ref = $"p:{pageId}", reason = "other", details = new string('x', 1001) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await uploader.GetAsync("/api/reports?status=unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await uploader.PostAsync($"/api/reports/{Guid.NewGuid()}/resolve", null)).StatusCode);

        var (hiddenId, _) = await ReportMaterialAsync(FileStatuses.Uploaded);
        Assert.Equal(HttpStatusCode.NotFound, (await student.PostAsJsonAsync("/api/reports", new { @ref = $"p:{hiddenId}", reason = "other" })).StatusCode);
        var ids = new List<Guid>();
        // Reports for different crops must stay distinct even after both crops are replaced in Inspect.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var extra = new Region { PageId = pageId, Label = RegionLabels.Question, Text = "Question 2" };
            db.Regions.Add(extra);
            await db.SaveChangesAsync();
            ids.AddRange([regionId, extra.Id]);
        }
        foreach (var region in ids)
            Assert.Equal(HttpStatusCode.OK, (await student.PostAsJsonAsync("/api/reports", new { @ref = $"r:{region}", reason = "wrong_course" })).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            db.Regions.RemoveRange(await db.Regions.Where(r => ids.Contains(r.Id)).ToListAsync());
            await db.SaveChangesAsync();
            var reports = await db.MaterialReports.Where(r => r.PageId == pageId).ToListAsync();
            Assert.Equal(2, reports.Count);
            Assert.All(reports, r => Assert.Null(r.RegionId));
        }
        Assert.Equal(HttpStatusCode.OK, (await student.PostAsJsonAsync("/api/reports", new { @ref = $"p:{pageId}", reason = "missing_page" })).StatusCode);
    }

    [Fact]
    public async Task File_reader_returns_page_and_question_bookmarks_for_only_the_signed_in_user()
    {
        var owner = await app.SignInAsync(ApiFactory.Uploader);
        var student = await app.SignInAsync("25-8879");
        var (pageId, regionId) = await ReportMaterialAsync();
        Guid fileId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            fileId = (await db.Pages.FindAsync(pageId))!.FileId;
        }
        (await student.PostAsJsonAsync("/api/bookmarks", new { @ref = $"p:{pageId}" })).EnsureSuccessStatusCode();
        (await student.PostAsJsonAsync("/api/bookmarks", new { @ref = $"r:{regionId}" })).EnsureSuccessStatusCode();
        foreach (var (client, expected) in new[] { (student, true), (owner, false) })
        {
            var detail = await client.GetFromJsonAsync<JsonObject>($"/api/files/{fileId}");
            var page = Assert.Single(detail!["pages"]!.AsArray());
            Assert.Equal(expected, page!["saved"]!.GetValue<bool>());
            var region = Assert.Single(page["regions"]!.AsArray());
            Assert.Equal(expected, region!["saved"]!.GetValue<bool>());
        }
        (await student.DeleteAsync($"/api/bookmarks/r:{regionId}")).EnsureSuccessStatusCode();
        var after = await student.GetFromJsonAsync<JsonObject>($"/api/files/{fileId}");
        Assert.True(after!["pages"]![0]!["saved"]!.GetValue<bool>());
        Assert.False(after["pages"]![0]!["regions"]![0]!["saved"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Student_reports_have_an_hourly_limit_without_counting_duplicate_submissions()
    {
        await app.SignInAsync(ApiFactory.Uploader);
        var student = await app.SignInAsync("25-8892");
        var (pageId, _) = await ReportMaterialAsync();
        Guid fileId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            fileId = (await db.Pages.FindAsync(pageId))!.FileId;
        }
        for (var i = 0; i < 20; i++)
        {
            Guid id;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                var page = new Page { FileId = fileId, Number = i + 2 };
                db.Pages.Add(page);
                await db.SaveChangesAsync();
                id = page.Id;
            }
            Assert.Equal(HttpStatusCode.OK, (await student.PostAsJsonAsync("/api/reports", new { @ref = $"p:{id}", reason = "other" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await student.PostAsJsonAsync("/api/reports", new { @ref = $"p:{id}", reason = "other" })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await student.PostAsJsonAsync("/api/reports", new { @ref = $"p:{pageId}", reason = "other" })).StatusCode);
    }

    [Fact]
    public async Task Whole_paper_requests_return_distinct_full_files_and_topic_requests_keep_crops()
    {
        await app.SignInAsync(ApiFactory.Uploader);
        var student = await app.SignInAsync("25-8894");
        var paperIds = new List<Guid>();
        Guid exerciseId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var course = await db.Courses.SingleAsync(c => c.Slug == "data-structures");
            var owner = await db.Users.SingleAsync(u => u.UniId == ApiFactory.Uploader);
            for (var i = 0; i < 5; i++)
            {
                var file = new DocFile
                {
                    Title = $"File request fixture {i}", OriginalName = $"paper-{i}.pdf", Sha256 = Guid.NewGuid().ToString(),
                    UploadedById = owner.Id, Course = course, AcademicYear = 2026, PageCount = 2,
                    MaterialType = i == 4 ? MaterialTypes.Exercise : MaterialTypes.PastPaper,
                    Status = i == 3 ? FileStatuses.Reading : FileStatuses.Ready,
                };
                for (var number = 1; number <= 2; number++)
                {
                    var page = new Page { File = file, Number = number, Width = 600, Height = 800, Extracted = i != 3,
                        Text = i == 3 ? "" : "Questions about queues", Kind = PageKinds.Assessment };
                    file.Pages.Add(page);
                    if (i != 3)
                    {
                        page.Regions.Add(new Region { Page = page, Label = RegionLabels.Question, Text = "Question about queues", QuestionNumber = number.ToString(), W = .8f, H = .4f });
                        IndexText.Refresh(file, page);
                    }
                }
                db.Files.Add(file);
                if (i < 4) paperIds.Add(file.Id);
            }
            await db.SaveChangesAsync();
            exerciseId = await db.Files.Where(f => f.Title == "File request fixture 4").Select(f => f.Id).SingleAsync();
        }
        var chatId = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        var reply = await SendAsync(student, chatId, "ok i want full ds past papers 2026");
        Assert.False(reply["refused"]!.GetValue<bool>());
        Assert.Equal(1, reply["moreCount"]!.GetValue<int>());
        var cards = reply["results"]!.AsArray().ToList();
        Assert.Equal(3, cards.Count);
        var callsBeforeMore = app.ChatModel.RequestCount;
        var more = await SendAsync(student, chatId, "Show more", new { more = true });
        Assert.Equal(callsBeforeMore, app.ChatModel.RequestCount);
        Assert.Equal("AI prepared batch one.", more["text"]!.GetValue<string>());
        cards.AddRange(more["results"]!.AsArray());
        Assert.Equal(4, cards.Count);
        Assert.Equal(paperIds.Order(), cards.Select(c => Guid.Parse(c!["fileId"]!.GetValue<string>())).Order());
        Assert.DoesNotContain(cards, c => c!["fileId"]!.GetValue<string>() == exerciseId.ToString());
        Assert.All(cards, c =>
        {
            Assert.Equal("file", c!["kind"]!.GetValue<string>());
            Assert.StartsWith("f:", c["ref"]!.GetValue<string>());
            Assert.Null(c["regionId"]);
            Assert.Null(c["box"]);
            Assert.Equal(1, c["pageNumber"]!.GetValue<int>());
            Assert.Equal(2, c["pageCount"]!.GetValue<int>());
            Assert.Contains("/api/media/pages/", c["imageUrl"]!.GetValue<string>());
        });
        var stored = await student.GetFromJsonAsync<JsonObject>($"/api/chats/{chatId}");
        Assert.All(stored!["messages"]!.AsArray().Where(m => m!["role"]!.GetValue<string>() == "bot")
            .SelectMany(m => m!["results"]!.AsArray()), c => Assert.Equal("file", c!["kind"]!.GetValue<string>()));

        var questions = await SendAsync(student, chatId, "past paper questions about queues in DS 2026");
        Assert.NotEmpty(questions["results"]!.AsArray());
        Assert.All(questions["results"]!.AsArray(), c => Assert.Equal("question", c!["kind"]!.GetValue<string>()));
        using var check = app.Services.CreateScope();
        var dbCheck = check.ServiceProvider.GetRequiredService<AppDb>();
        Assert.False(await dbCheck.Jobs.AnyAsync(j => paperIds.Contains(j.FileId)));
        Assert.All(await dbCheck.Pages.Where(p => p.FileId == paperIds[3]).ToListAsync(), p => Assert.False(p.Extracted));
    }

    [Fact]
    public async Task Model_replies_are_sent_verbatim_and_interpret_followups_with_history()
    {
        var student = await app.SignInAsync("25-8894");
        var chatId = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            db.Messages.Add(new Message { ConversationId = Guid.Parse(chatId), Role = "bot", Text = "Would you like more questions?" });
            await db.SaveChangesAsync();
        }
        app.ChatModel.EnqueueTool("reply", new JsonObject { ["commentary"] = "I can show you the original pages with the answers on them.", ["result_refs"] = new JsonArray(), ["refused"] = false, ["more_commentary"] = new JsonArray() });
        var correction = await SendAsync(student, chatId, "noo answers", prepared: true);
        Assert.False(correction["refused"]!.GetValue<bool>());
        Assert.Equal("I can show you the original pages with the answers on them.", correction["text"]!.GetValue<string>());
        Assert.Empty(correction["results"]!.AsArray());
        var sentContext = app.ChatModel.LastRequest!.ToJsonString();
        Assert.Contains("Would you like more questions?", sentContext);
        Assert.Contains("noo answers", sentContext);
        app.ChatModel.EnqueueTool("reply", new JsonObject { ["commentary"] = "First sentence. Second sentence. Third sentence.", ["result_refs"] = new JsonArray(), ["refused"] = false, ["more_commentary"] = new JsonArray() });
        var introduction = await SendAsync(student, chatId, "i am abdulaziz", prepared: true);
        Assert.False(introduction["refused"]!.GetValue<bool>());
        Assert.Equal("First sentence. Second sentence. Third sentence.", introduction["text"]!.GetValue<string>());
        var help = await SendAsync(student, chatId, "I am Abdulaziz, give me a hint");
        Assert.True(help["refused"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Clarification_replies_are_generated_before_the_choice_is_clicked()
    {
        await app.SignInAsync(ApiFactory.Uploader);
        var student = await app.SignInAsync("25-8894");
        await ReportMaterialAsync();
        var chatId = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        JsonObject Option(string label, string course) => new()
        {
            ["label"] = label, ["course"] = course, ["query"] = "", ["material_type"] = "other", ["want"] = "files",
            ["found_commentary"] = $"Model prepared results for {label}.",
            ["empty_commentary"] = $"Model prepared no results for {label}.",
            ["more_commentary"] = new JsonArray("Prepared next.", "Prepared another.", "Prepared later."),
        };
        app.ChatModel.EnqueueTool("ask", new JsonObject
        {
            ["question"] = "Which course should I check?", ["options"] = new JsonArray(Option("Data Structures", "data-structures"), Option("Efficient Algorithms", "efficient-algorithms")),
        });
        var before = app.ChatModel.RequestCount;
        var ask = await SendAsync(student, chatId, "find full papers for algorithm analysis", prepared: true);
        Assert.Equal("Which course should I check?", ask["text"]!.GetValue<string>());
        Assert.Equal(before + 1, app.ChatModel.RequestCount);
        var picked = await SendAsync(student, chatId, "Data Structures", new { option = 0 });
        Assert.Equal("Model prepared results for Data Structures.", picked["text"]!.GetValue<string>());
        Assert.NotEmpty(picked["results"]!.AsArray());
        Assert.All(picked["results"]!.AsArray(), c => Assert.Equal("file", c!["kind"]!.GetValue<string>()));
        Assert.Equal(before + 1, app.ChatModel.RequestCount);
    }

    [Fact]
    public async Task Incomplete_clarification_drafts_degrade_instead_of_failing_the_turn()
    {
        await app.SignInAsync(ApiFactory.Uploader);
        var student = await app.SignInAsync("25-8894");
        await ReportMaterialAsync();
        var chatId = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        JsonObject Option(string label, string course) => new()
        {
            ["label"] = label, ["course"] = course, ["query"] = "", ["material_type"] = "other", ["want"] = "files",
            ["found_commentary"] = "", ["empty_commentary"] = "", ["more_commentary"] = new JsonArray("Only one line."),
        };
        // The model leaves the drafts out, adds a blank option, and its repair call returns nothing useful.
        app.ChatModel.EnqueueTool("ask", new JsonObject
        {
            ["question"] = "Which course should I check?",
            ["options"] = new JsonArray(Option("Data Structures", "data-structures"), Option("Efficient Algorithms", "efficient-algorithms"), Option(" ", "data-structures")),
        });
        app.ChatModel.EnqueueText("not json");
        var ask = await SendAsync(student, chatId, "find full papers for algorithm analysis", prepared: true);
        Assert.Equal("Which course should I check?", ask["text"]!.GetValue<string>());
        Assert.Equal(2, ask["options"]!.AsArray().Count);
        // Picking writes the missing line then.
        app.ChatModel.EnqueueText("Model wrote the pick reply.");
        var picked = await SendAsync(student, chatId, "Data Structures", new { option = 0 });
        Assert.Equal("Model wrote the pick reply.", picked["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Model_failures_are_streamed_as_errors_not_fake_bot_answers()
    {
        var student = await app.SignInAsync("25-8894");
        var chatId = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        app.ChatModel.Enqueue(_ => throw new InvalidOperationException("Test model failure"));
        var response = await student.PostAsJsonAsync($"/api/chats/{chatId}/messages", new { text = "Hello" });
        var stream = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: error", stream);
        Assert.DoesNotContain("event: message", stream);
        var saved = await student.GetFromJsonAsync<JsonObject>($"/api/chats/{chatId}");
        Assert.DoesNotContain(saved!["messages"]!.AsArray(), m => m!["role"]!.GetValue<string>() == "bot");
    }

    [Fact]
    public async Task Older_chat_results_without_prepared_text_get_a_model_reply()
    {
        var student = await app.SignInAsync("25-8894");
        var chatId = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        await app.SignInAsync(ApiFactory.Uploader);
        var older = await ReportMaterialAsync();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            db.Messages.Add(new Message { ConversationId = Guid.Parse(chatId), Role = "bot", Text = "Earlier results", More = [ResultRefs.Region(older.RegionId)] });
            await db.SaveChangesAsync();
        }
        app.ChatModel.EnqueueText("{\"lines\":[\"Model prepared the older follow-up.\"]}");
        var before = app.ChatModel.RequestCount;
        var more = await SendAsync(student, chatId, "Show more", new { more = true });
        Assert.Equal("Model prepared the older follow-up.", more["text"]!.GetValue<string>());
        Assert.Equal(before + 1, app.ChatModel.RequestCount);
    }

    [Fact]
    public async Task Course_inventory_counts_existing_unread_files_without_processing_them()
    {
        await app.SignInAsync(ApiFactory.Uploader);
        var student = await app.SignInAsync("25-8894");
        Guid fileId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var file = new DocFile
            {
                Title = "Unread inventory fixture", OriginalName = "inventory.pdf", Sha256 = Guid.NewGuid().ToString(),
                UploadedById = (await db.Users.SingleAsync(u => u.UniId == ApiFactory.Uploader)).Id,
                CourseId = (await db.Courses.SingleAsync(c => c.Slug == "data-structures")).Id,
                Status = FileStatuses.Reading, MaterialType = MaterialTypes.PastPaper, PageCount = 1,
            };
            db.Pages.Add(new Page { File = file, Number = 1, Extracted = false });
            await db.SaveChangesAsync();
            fileId = file.Id;
        }
        app.ChatModel.EnqueueTool("list_courses", new JsonObject());
        app.ChatModel.Enqueue(request =>
        {
            var tool = request["messages"]!.AsArray().Last(m => m?["role"]?.GetValue<string>() == "tool")!;
            var output = JsonNode.Parse(tool["content"]!.GetValue<string>())!;
            var course = output["courses"]!.AsArray().Single(c => c!["slug"]!.GetValue<string>() == "data-structures")!;
            Assert.True(course["files"]!.GetValue<int>() > course["searchable_files"]!.GetValue<int>());
            return ScriptedChatModel.Tool("reply", new JsonObject { ["commentary"] = "Model checked the course inventory.",
                ["result_refs"] = new JsonArray(), ["refused"] = false, ["more_commentary"] = new JsonArray() });
        });
        var chatId = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        var reply = await SendAsync(student, chatId, "What files are available?", prepared: true);
        Assert.Equal("Model checked the course inventory.", reply["text"]!.GetValue<string>());
        using var check = app.Services.CreateScope();
        var dbCheck = check.ServiceProvider.GetRequiredService<AppDb>();
        Assert.False(await dbCheck.Jobs.AnyAsync(j => j.FileId == fileId));
        Assert.False((await dbCheck.Pages.SingleAsync(p => p.FileId == fileId)).Extracted);
    }

    private async Task<(Guid PageId, Guid RegionId)> ReportMaterialAsync(string status = FileStatuses.Ready)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var file = new DocFile
        {
            Title = "Report test paper", OriginalName = "report-test.pdf", Sha256 = Guid.NewGuid().ToString(),
            UploadedById = (await db.Users.SingleAsync(u => u.UniId == ApiFactory.Uploader)).Id,
            CourseId = (await db.Courses.SingleAsync(c => c.Slug == "data-structures")).Id,
            Status = status, PageCount = 1,
        };
        var page = new Page { File = file, Number = 1, Text = "Question 1", Extracted = true };
        var region = new Region { Page = page, Label = RegionLabels.Question, Text = "Question 1" };
        db.Regions.Add(region);
        await db.SaveChangesAsync();
        return (page.Id, region.Id);
    }

    private async Task<JsonObject> SendAsync(HttpClient client, string chatId, string text, object? extra = null, bool prepared = false)
    {
        if (!prepared && extra is null) await PrepareModelTurnAsync(chatId, text);
        if (!prepared && extra is not null && JsonSerializer.SerializeToNode(extra)?["more"]?.GetValue<bool>() == true)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var last = await db.Messages.AsNoTracking().Where(m => m.ConversationId == Guid.Parse(chatId) && m.Role == "bot")
                .OrderByDescending(m => m.CreatedAt).FirstAsync();
            if (last.More.Length == 0) app.ChatModel.EnqueueText("Model reply for the completed list.");
        }
        var res = await client.PostAsJsonAsync($"/api/chats/{chatId}/messages",
            extra is null ? new JsonObject { ["text"] = text } : Merge(text, extra));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        var data = body.Split("\n\n").First(c => c.StartsWith("event: message")).Split('\n')[1]["data: ".Length..];
        return JsonNode.Parse(data)!["message"]!.AsObject();
    }

    private async Task PrepareModelTurnAsync(string chatId, string text)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var last = await db.Messages.AsNoTracking().Where(m => m.ConversationId == Guid.Parse(chatId) && m.Role == "bot")
            .OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync();
        var lower = text.ToLowerInvariant();
        if (lower.Contains("what's the answer") || lower.Contains("hint"))
        {
            var refs = new JsonArray((last?.Results ?? []).Take(1).Select(r => (JsonNode)r).ToArray());
            app.ChatModel.EnqueueTool("reply", new JsonObject { ["commentary"] = $"Model reply {app.ChatModel.RequestCount + 1}.",
                ["result_refs"] = refs, ["asked_for"] = "questions", ["refused"] = true, ["more_commentary"] = new JsonArray() });
            return;
        }
        var courses = await db.Courses.AsNoTracking().ToListAsync();
        var course = Heuristics.MatchCourse(lower, courses);
        var want = lower.Contains("full") && lower.Contains("papers") ? MaterialWants.Files : MaterialWants.Questions;
        // Like the real model: a whole-file request described fully by filters has no search text of its own.
        var args = new JsonObject { ["meaning"] = want == MaterialWants.Files ? "" : text, ["keywords"] = new JsonArray(), ["want"] = want };
        if (course is not null) args["course"] = course.Slug;
        if (Heuristics.MaterialType(lower) is { } type) args["material_type"] = type;
        if (Heuristics.Year(text) is { } year) args["year"] = year;
        if (Heuristics.Label(text) is { } label) args["assessment_label"] = label;

        app.ChatModel.EnqueueTool("search_material", args);
        app.ChatModel.Enqueue(request =>
        {
            var messages = request["messages"]!.AsArray();
            var tool = messages.Last(m => m?["role"]?.GetValue<string>() == "tool")!;
            var result = JsonNode.Parse(tool["content"]!.GetValue<string>())!;
            var refs = new JsonArray(result["results"]!.AsArray().Take(12).Select(r => (JsonNode)r!["ref"]!.GetValue<string>()).ToArray());
            return ScriptedChatModel.Tool("reply", new JsonObject { ["commentary"] = $"Model reply {app.ChatModel.RequestCount}.",
                ["result_refs"] = refs, ["asked_for"] = want, ["refused"] = false,
                ["more_commentary"] = new JsonArray("AI prepared batch one.", "AI prepared batch two.", "AI prepared batch three.") });
        });
    }

    private static JsonObject Merge(string text, object extra)
    {
        var body = JsonSerializer.SerializeToNode(extra)!.AsObject();
        body["text"] = text;
        return body;
    }
}
