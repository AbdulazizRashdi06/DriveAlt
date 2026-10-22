using System.Collections.Concurrent;
using System.Security.Claims;
using DriveAlt.Api.Auth;
using DriveAlt.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DriveAlt.Api.Infrastructure;

/// <summary>Keeps the role in the auth cookie in sync with the database (promotions/demotions apply within a minute).</summary>
public static class RoleCache
{
    private static readonly ConcurrentDictionary<Guid, (string? Role, DateTime At)> Cache = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    public static void Forget(Guid userId) => Cache.TryRemove(userId, out _);

    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var id = ctx.Principal?.UserId();
        if (id is null) { ctx.RejectPrincipal(); return; }

        if (!Cache.TryGetValue(id.Value, out var entry) || DateTime.UtcNow - entry.At > Ttl)
        {
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDb>();
            var user = await db.Users.FindAsync(id.Value);
            entry = (user?.Role, DateTime.UtcNow);
            Cache[id.Value] = entry;
        }

        if (entry.Role is null)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        var current = ctx.Principal!.FindFirstValue(ClaimTypes.Role);
        if (current != entry.Role)
        {
            var identity = new ClaimsIdentity(ctx.Principal!.Claims.Where(c => c.Type != ClaimTypes.Role)
                .Append(new Claim(ClaimTypes.Role, entry.Role)), CookieAuthenticationDefaults.AuthenticationScheme);
            ctx.ReplacePrincipal(new ClaimsPrincipal(identity));
            ctx.ShouldRenew = true;
        }
    }
}
