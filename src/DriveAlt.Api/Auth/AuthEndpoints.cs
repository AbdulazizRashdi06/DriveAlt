using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DriveAlt.Api.Data;
using DriveAlt.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DriveAlt.Api.Auth;

public static partial class AuthEndpoints
{
    public const string UploaderPolicy = "uploader";
    /// <summary>Signed in with a uni ID: anyone but a guest. Saving and reporting need it.</summary>
    public const string MemberPolicy = "member";
    public const string GuestRateLimit = "guest";
    public const string ChatTurnRateLimit = "chat-turn";

    [GeneratedRegex(@"^\d{2}-\d{4}$")]
    private static partial Regex UniIdPattern();

    public static string NormalizeUniId(string? raw) => (raw ?? "").Trim().Replace('–', '-').Replace(' ', '-');

    public static bool IsValidUniId(string uniId) => UniIdPattern().IsMatch(uniId);

    public static string EmailFor(AuthOptions o, string uniId) => o.EmailTemplate.Replace("{id}", uniId);

    public static string HashCode(AuthOptions o, string uniId, string code)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(o.OtpSecret));
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes($"{uniId}:{code}")));
    }

    public sealed record RequestCodeBody(string UniId);
    public sealed record VerifyBody(string UniId, string Code);
    public sealed record MeDto(Guid Id, string UniId, string Email, string Role, string Initials);

    public static MeDto ToDto(User u) => u.Role == Roles.Guest
        ? new(u.Id, "Guest", "", u.Role, "G")
        : new(u.Id, u.UniId, u.Email, u.Role, Initials(u.UniId));

    private static string Initials(string uniId) => uniId.Replace("-", "")[^2..];

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapPost("/request", async (RequestCodeBody body, AppDb db, IEmailSender email, IOptions<AuthOptions> opts, CancellationToken ct) =>
        {
            var o = opts.Value;
            var uniId = NormalizeUniId(body.UniId);
            if (!IsValidUniId(uniId))
                return Results.BadRequest(new { error = "That doesn't look like a uni ID. It should look like 24-0161." });
            if (o.AllowedPrefixes.Count > 0 && !o.AllowedPrefixes.Any(p => uniId.StartsWith(p, StringComparison.Ordinal)))
                return Results.BadRequest(new { error = "Only students on the batch list can get in. Not on it? Ping your batch rep." });

            var now = DateTime.UtcNow;
            var recent = await db.OtpCodes.Where(c => c.UniId == uniId && c.CreatedAt > now.AddHours(-1))
                .OrderByDescending(c => c.CreatedAt).Select(c => c.CreatedAt).ToListAsync(ct);
            if (recent.Count > 0 && recent[0] > now.AddSeconds(-45))
                return Results.Json(new { error = "Hang on a few seconds before asking for another code." }, statusCode: 429);
            if (recent.Count >= 6)
                return Results.Json(new { error = "Too many codes this hour. Try again later." }, statusCode: 429);

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            db.OtpCodes.Add(new OtpCode
            {
                UniId = uniId,
                CodeHash = HashCode(o, uniId, code),
                ExpiresAt = now.AddMinutes(o.OtpMinutes),
            });
            await db.SaveChangesAsync(ct);
            var to = EmailFor(o, uniId);
            await email.SendOtpAsync(to, code, ct);
            return Results.Ok(new { email = to });
        }).RequireRateLimiting("otp");

        g.MapPost("/verify", async (VerifyBody body, AppDb db, IOptions<AuthOptions> opts, HttpContext http, CancellationToken ct) =>
        {
            var o = opts.Value;
            var uniId = NormalizeUniId(body.UniId);
            var code = new string((body.Code ?? "").Where(char.IsDigit).ToArray());
            var now = DateTime.UtcNow;
            var otp = await db.OtpCodes
                .Where(c => c.UniId == uniId && c.ConsumedAt == null && c.ExpiresAt > now)
                .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
            if (otp is null)
                return Results.BadRequest(new { error = "That code expired. Ask for a new one." });
            if (otp.Attempts >= o.OtpMaxAttempts)
                return Results.BadRequest(new { error = "Too many tries. Ask for a new code." });

            otp.Attempts++;
            var expected = Convert.FromHexString(otp.CodeHash);
            var actual = Convert.FromHexString(HashCode(o, uniId, code));
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                await db.SaveChangesAsync(ct);
                return Results.BadRequest(new { error = "Wrong code. Check your uni email and try again." });
            }
            otp.ConsumedAt = now;

            var user = await db.Users.FirstOrDefaultAsync(u => u.UniId == uniId, ct);
            if (user is null)
            {
                user = new User { UniId = uniId, Email = EmailFor(o, uniId) };
                db.Users.Add(user);
            }
            if (o.Uploaders.Contains(uniId, StringComparer.OrdinalIgnoreCase))
                user.Role = Roles.Uploader;
            user.LastLoginAt = now;
            await db.SaveChangesAsync(ct);

            // Signing in from a guest session keeps the guest's chats; the guest row itself is no longer needed.
            if (http.User.IsGuest() && http.User.UserId() is { } guestId && guestId != user.Id)
                await ForgetGuestAsync(db, guestId, keepChatsFor: user.Id, ct);

            await SignInAsync(http, user);
            return Results.Ok(ToDto(user));
        }).RequireRateLimiting("otp");

        // "Continue as a guest": a session backed by a guest row, so chats work like a student's without a uni ID.
        g.MapPost("/guest", async (AppDb db, IOptions<AuthOptions> opts, HttpContext http, CancellationToken ct) =>
        {
            if (!opts.Value.GuestAccess)
                return Results.Json(new { error = "Guest access is off. Sign in with your uni ID." }, statusCode: 403);
            if (http.User.UserId() is { } id && await db.Users.FindAsync([id], ct) is { } current)
                return Results.Ok(ToDto(current));
            var guest = new User { UniId = "guest-" + Guid.NewGuid().ToString("N"), Email = "", Role = Roles.Guest, LastLoginAt = DateTime.UtcNow };
            db.Users.Add(guest);
            await db.SaveChangesAsync(ct);
            await SignInAsync(http, guest);
            return Results.Ok(ToDto(guest));
        }).RequireRateLimiting(GuestRateLimit);

        g.MapPost("/logout", async (HttpContext http, AppDb db, CancellationToken ct) =>
        {
            // A guest's chats can't be reached again once the cookie is gone, so they go with it.
            if (http.User.IsGuest() && http.User.UserId() is { } guestId)
                await ForgetGuestAsync(db, guestId, keepChatsFor: null, ct);
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        g.MapGet("/me", async (HttpContext http, AppDb db, CancellationToken ct) =>
        {
            // 204 = signed out (not an error: the SPA asks this on every load).
            var id = http.User.UserId();
            if (id is null) return Results.NoContent();
            var user = await db.Users.FindAsync([id.Value], ct);
            return user is null ? Results.NoContent() : Results.Ok(ToDto(user));
        });

        if (app.Environment.IsDevelopment())
        {
            app.MapGet("/api/dev/otp/{uniId}", (string uniId, IOptions<AuthOptions> opts) =>
                DevEmailSender.LastCodes.TryGetValue(EmailFor(opts.Value, uniId), out var code)
                    ? Results.Ok(new { code })
                    : Results.NotFound());
        }
    }

    /// <summary>Removes a guest row, moving its chats to <paramref name="keepChatsFor"/> or deleting them.</summary>
    private static async Task ForgetGuestAsync(AppDb db, Guid guestId, Guid? keepChatsFor, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == guestId && u.Role == Roles.Guest, ct)) return;
        var chats = db.Conversations.Where(c => c.UserId == guestId);
        if (keepChatsFor is { } owner) await chats.ExecuteUpdateAsync(x => x.SetProperty(c => c.UserId, owner), ct);
        else await chats.ExecuteDeleteAsync(ct);
        // Guests can't save or report, but clear anything left from before a policy change so the row can go.
        await db.Bookmarks.Where(b => b.UserId == guestId).ExecuteDeleteAsync(ct);
        if (await db.MaterialReports.AnyAsync(r => r.UserId == guestId, ct)) return;
        await db.Users.Where(u => u.Id == guestId).ExecuteDeleteAsync(ct);
        RoleCache.Forget(guestId);
    }

    public static Task SignInAsync(HttpContext http, User user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UniId),
            new Claim(ClaimTypes.Role, user.Role),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
}

public static class ClaimsExtensions
{
    public static Guid? UserId(this ClaimsPrincipal p) =>
        Guid.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static Guid RequiredUserId(this ClaimsPrincipal p) =>
        p.UserId() ?? throw new UnauthorizedAccessException();

    public static bool IsUploader(this ClaimsPrincipal p) => p.IsInRole(Roles.Uploader);

    public static bool IsGuest(this ClaimsPrincipal p) => p.IsInRole(Roles.Guest);
}
