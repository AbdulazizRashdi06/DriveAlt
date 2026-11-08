using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DriveAlt.Api.Data;
using DriveAlt.Api.Auth;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Tests;

/// <summary>
/// Owner, 2026-11-08: "i wanna make login optional in home screen you can continue as a guest". A guest gets a session
/// backed by a guest row: browse, view and chat (rate-limited per IP), but no saving, no reports and no uploader pages.
/// </summary>
public partial class ApiTests
{
    private static int _guestIp;

    /// <summary>A cookie client that looks like its own visitor IP, so per-IP limits don't leak between tests.</summary>
    private HttpClient NewVisitor()
    {
        var client = app.NewClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"203.0.113.{Interlocked.Increment(ref _guestIp)}");
        return client;
    }

    private static async Task<JsonObject> ContinueAsGuestAsync(HttpClient client)
    {
        var r = await client.PostAsync("/api/auth/guest", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private static async Task<string> NewChatAsync(HttpClient client) =>
        (await (await client.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();

    private void EnqueuePlainReply(string text) => app.ChatModel.Enqueue(_ => ScriptedChatModel.Tool("reply", new JsonObject
    {
        ["commentary"] = text, ["result_refs"] = new JsonArray(), ["refused"] = false, ["more_commentary"] = new JsonArray(),
    }));

    [Fact]
    public async Task Guests_can_browse_view_and_chat_but_not_save_report_or_upload()
    {
        var visitor = NewVisitor();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/courses")).StatusCode);

        var config = (await (await visitor.GetAsync("/api/config")).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.True(config["guestAccess"]!.GetValue<bool>());
        Assert.True(config["guestChat"]!.GetValue<bool>());

        var me = await ContinueAsGuestAsync(visitor);
        Assert.Equal("guest", me["role"]!.GetValue<string>());
        Assert.Equal("Guest", me["uniId"]!.GetValue<string>());
        Assert.Equal("", me["email"]!.GetValue<string>());
        // Asking again keeps the same guest instead of making another row.
        Assert.Equal(me["id"]!.GetValue<string>(), (await ContinueAsGuestAsync(visitor))["id"]!.GetValue<string>());
        Assert.Equal("guest", (await visitor.GetFromJsonAsync<JsonObject>("/api/auth/me"))!["role"]!.GetValue<string>());

        // Read side: the same endpoints a student uses.
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/api/courses")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/api/search?q=queue")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/files/{Guid.NewGuid()}")).StatusCode);

        // Saving and reporting need a uni ID.
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.GetAsync("/api/bookmarks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.PostAsJsonAsync("/api/bookmarks", new { @ref = $"p:{Guid.NewGuid()}" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.PostAsJsonAsync("/api/reports", new { @ref = $"p:{Guid.NewGuid()}", reason = "other" })).StatusCode);

        // Uploader pages stay closed.
        foreach (var path in new[] { "/api/uploads", "/api/admin/users", "/api/admin/stats", "/api/reports", "/api/admin/drive-sync/" })
            Assert.Equal(HttpStatusCode.Forbidden, (await visitor.GetAsync(path)).StatusCode);

        // Chat works like a student's.
        var chat = await NewChatAsync(visitor);
        EnqueuePlainReply("Hi! Which course are you studying for?");
        var reply = await SendAsync(visitor, chat, "hello", prepared: true);
        Assert.Equal("Hi! Which course are you studying for?", reply["text"]!.GetValue<string>());
        var chats = (await visitor.GetFromJsonAsync<JsonArray>("/api/chats"))!;
        Assert.Contains(chats, c => c!["id"]!.GetValue<string>() == chat);

        // Guests aren't in the uploader's people list.
        var uploader = await app.SignInAsync(ApiFactory.Uploader);
        var people = (await uploader.GetFromJsonAsync<JsonArray>("/api/admin/users"))!;
        Assert.DoesNotContain(people, u => u!["role"]!.GetValue<string>() == "guest");
        Assert.Equal(HttpStatusCode.NotFound,
            (await uploader.PutAsJsonAsync($"/api/admin/users/{me["id"]!.GetValue<string>()}/role", new { role = "uploader" })).StatusCode);
    }

    [Fact]
    public async Task Guest_chat_turns_are_limited_per_ip_but_students_are_not()
    {
        var guest = NewVisitor();
        await ContinueAsGuestAsync(guest);
        var chat = await NewChatAsync(guest);
        for (var i = 0; i < ApiFactory.GuestChatPerHour; i++)
        {
            EnqueuePlainReply($"Reply {i}");
            await SendAsync(guest, chat, $"message {i}", prepared: true);
        }

        var limited = await guest.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "one more" });
        Assert.Equal((HttpStatusCode)429, limited.StatusCode);
        Assert.Contains("Sign in with your uni ID", (await limited.Content.ReadFromJsonAsync<JsonObject>())!["error"]!.GetValue<string>());

        // Another visitor has their own allowance.
        var other = NewVisitor();
        await ContinueAsGuestAsync(other);
        var otherChat = await NewChatAsync(other);
        EnqueuePlainReply("Other visitor reply");
        Assert.Equal("Other visitor reply", (await SendAsync(other, otherChat, "hi", prepared: true))["text"]!.GetValue<string>());

        // A student is never held to the guest limit.
        var student = await app.SignInAsync("25-7101");
        var studentChat = await NewChatAsync(student);
        for (var i = 0; i <= ApiFactory.GuestChatPerHour; i++)
        {
            EnqueuePlainReply($"Student reply {i}");
            await SendAsync(student, studentChat, $"student message {i}", prepared: true);
        }
    }

    [Fact]
    public async Task Signing_in_from_a_guest_session_keeps_its_chats()
    {
        var visitor = NewVisitor();
        var guestId = Guid.Parse((await ContinueAsGuestAsync(visitor))["id"]!.GetValue<string>());
        var chat = await NewChatAsync(visitor);

        Assert.Equal(HttpStatusCode.OK, (await visitor.PostAsJsonAsync("/api/auth/request", new { uniId = "25-7102" })).StatusCode);
        var code = DevEmailSender.LastCodes["25-7102@student.gutech.edu.om"];
        var me = await visitor.PostAsJsonAsync("/api/auth/verify", new { uniId = "25-7102", code });
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("student", (await me.Content.ReadFromJsonAsync<JsonObject>())!["role"]!.GetValue<string>());

        var chats = (await visitor.GetFromJsonAsync<JsonArray>("/api/chats"))!;
        Assert.Contains(chats, c => c!["id"]!.GetValue<string>() == chat);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/api/bookmarks")).StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        Assert.False(await db.Users.AnyAsync(u => u.Id == guestId));
    }

    [Fact]
    public async Task Signing_out_as_a_guest_removes_the_guest_and_its_chats()
    {
        var visitor = NewVisitor();
        var guestId = Guid.Parse((await ContinueAsGuestAsync(visitor))["id"]!.GetValue<string>());
        var chat = Guid.Parse(await NewChatAsync(visitor));

        Assert.Equal(HttpStatusCode.OK, (await visitor.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await visitor.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/courses")).StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        Assert.False(await db.Users.AnyAsync(u => u.Id == guestId));
        Assert.False(await db.Conversations.AnyAsync(c => c.Id == chat));
    }

    [Fact]
    public async Task Starting_guest_sessions_is_rate_limited_per_ip()
    {
        var ip = $"198.51.100.{Interlocked.Increment(ref _guestIp)}";
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 11; i++)
        {
            var client = app.NewClient();
            client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
            last = (await client.PostAsync("/api/auth/guest", null)).StatusCode;
            if (i < 10) Assert.Equal(HttpStatusCode.OK, last);
        }
        Assert.Equal((HttpStatusCode)429, last);
    }
}
