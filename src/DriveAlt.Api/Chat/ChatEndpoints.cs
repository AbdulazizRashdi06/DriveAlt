using System.Text.Json;
using DriveAlt.Api.Auth;
using DriveAlt.Api.Data;
using DriveAlt.Api.Search;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Chat;

public static class ChatEndpoints
{
    public sealed record ConversationDto(Guid Id, string Title, DateTime UpdatedAt);
    /// <param name="Option">A chip clicked on the last clarifying question.</param>
    /// <param name="More">"Show more" clicked on the last results.</param>
    public sealed record SendBody(string Text, int? Option = null, bool More = false);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/chats").RequireAuthorization();

        g.MapGet("/", async (HttpContext http, AppDb db, CancellationToken ct) =>
        {
            var uid = http.User.RequiredUserId();
            return await db.Conversations.AsNoTracking().Where(c => c.UserId == uid)
                .OrderByDescending(c => c.UpdatedAt).Take(100)
                .Select(c => new ConversationDto(c.Id, c.Title, c.UpdatedAt)).ToListAsync(ct);
        });

        g.MapPost("/", async (HttpContext http, AppDb db, CancellationToken ct) =>
        {
            var c = new Conversation { UserId = http.User.RequiredUserId() };
            db.Conversations.Add(c);
            await db.SaveChangesAsync(ct);
            return new ConversationDto(c.Id, c.Title, c.UpdatedAt);
        });

        g.MapGet("/{id:guid}", async (Guid id, HttpContext http, AppDb db, ResultCards cards, CancellationToken ct) =>
        {
            var uid = http.User.RequiredUserId();
            var c = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid, ct);
            if (c is null) return Results.NotFound();
            var messages = await db.Messages.AsNoTracking().Where(m => m.ConversationId == id).OrderBy(m => m.CreatedAt).ToListAsync(ct);
            var all = await cards.HydrateAsync(messages.SelectMany(m => m.Results).Distinct().ToList(), uid, ct);
            var byRef = all.ToDictionary(x => x.Ref);
            return Results.Ok(new
            {
                conversation = new ConversationDto(c.Id, c.Title, c.UpdatedAt),
                messages = messages.Select(m => new MessageDto(m.Id, m.Role, m.Text, m.Refused, m.CreatedAt,
                    m.Results.Where(byRef.ContainsKey).Select(r => byRef[r]).ToList(), m.More.Length, ChatOption.Dtos(m.Options))),
            });
        });

        g.MapDelete("/{id:guid}", async (Guid id, HttpContext http, AppDb db, CancellationToken ct) =>
        {
            var uid = http.User.RequiredUserId();
            var n = await db.Conversations.Where(x => x.Id == id && x.UserId == uid).ExecuteDeleteAsync(ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Streams Server-Sent Events: "status" while searching, then "message" with the bot reply, then for a clarifying
        // question one "options" event per option search that finishes.
        g.MapPost("/{id:guid}/messages", async (Guid id, SendBody body, HttpContext http, AppDb db, ChatService chat, CancellationToken ct) =>
        {
            var uid = http.User.RequiredUserId();
            var convo = await db.Conversations.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid, ct);
            if (convo is null) { http.Response.StatusCode = 404; return; }
            if (string.IsNullOrWhiteSpace(body.Text)) { http.Response.StatusCode = 400; return; }

            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            async Task Send(string evt, object data)
            {
                await http.Response.WriteAsync($"event: {evt}\ndata: {JsonSerializer.Serialize(data, JsonDefaults.Options)}\n\n", ct);
                await http.Response.Body.FlushAsync(ct);
            }

            await Send("status", new ChatStatus("Sniffing around…"));
            try
            {
                await chat.SendAsync(convo, uid, body.Text, body.Option, body.More, new ChatStream(
                    s => Send("status", s),
                    m => Send("message", new { conversation = new ConversationDto(convo.Id, convo.Title, convo.UpdatedAt), message = m }),
                    (messageId, options) => Send("options", new { messageId, options })), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                app.Logger.LogWarning("Chat reply failed: {ErrorType}", e.GetType().Name);
                await Send("error", new { error = "The AI couldn't complete this reply. Please try again." });
            }
            await Send("done", new { });
        });
    }
}
