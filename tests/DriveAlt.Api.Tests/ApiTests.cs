using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DriveAlt.Api.Auth;
using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using DriveAlt.Api.Processing;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace DriveAlt.Api.Tests;

public class ApiTests(ApiFactory app) : IClassFixture<ApiFactory>
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
        Assert.Contains("Abdulaziz", refused["text"]!.GetValue<string>());
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
    public async Task Answers_are_burned_out_of_pages_and_crops()
    {
        using var scope = app.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<Storage>();
        var renderer = scope.ServiceProvider.GetRequiredService<PageRenderer>();
        var fileId = Guid.NewGuid();
        Directory.CreateDirectory(storage.PageDir(fileId));
        using (var white = new SKBitmap(800, 1000))
        {
            white.Erase(SKColors.White);
            ImageOps.SaveJpeg(white, storage.PageImage(fileId, 1), 90);
        }
        var page = new Page { FileId = fileId, Number = 1, Width = 800, Height = 1000 };
        var question = new Region { Label = RegionLabels.Question, X = 0.1f, Y = 0.1f, W = 0.8f, H = 0.5f };
        page.Regions.Add(question);
        page.Regions.Add(new Region { Label = RegionLabels.Answer, X = 0.1f, Y = 0.4f, W = 0.8f, H = 0.2f });

        await renderer.RenderAsync(fileId, page, CancellationToken.None);

        using var masked = ImageOps.LoadUpright(storage.MaskedPage(fileId, 1));
        var inAnswer = masked.GetPixel(120, 420);
        Assert.True(inAnswer.Red < 90 && inAnswer.Green < 90, $"answer area should be hidden, was {inAnswer}");
        var outside = masked.GetPixel(400, 900);
        Assert.True(outside.Red > 240, "rest of the page stays visible");

        // The question crop overlaps the answer: its lower part must be hidden too.
        using var crop = ImageOps.LoadUpright(storage.Crop(question.Id));
        var low = crop.GetPixel(30, crop.Height - 30);
        Assert.True(low.Red < 90, "answer overlapping a question crop is hidden in the crop");
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
        Assert.True(File.Exists(scope.ServiceProvider.GetRequiredService<Storage>().Crop(questionId)));
    }

    private static async Task<JsonObject> SendAsync(HttpClient client, string chatId, string text)
    {
        var res = await client.PostAsJsonAsync($"/api/chats/{chatId}/messages", new { text });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        var data = body.Split("\n\n").First(c => c.StartsWith("event: message")).Split('\n')[1]["data: ".Length..];
        return JsonNode.Parse(data)!["message"]!.AsObject();
    }
}
