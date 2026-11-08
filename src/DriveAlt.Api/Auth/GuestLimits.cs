using System.Threading.RateLimiting;

namespace DriveAlt.Api.Auth;

/// <summary>Marks the endpoint that sends a chat turn, so the guest limits apply to it and nothing else.</summary>
public sealed class ChatTurnEndpoint;

/// <summary>Rate limits for guests (sign-in is optional since 2026-11-08; the site is public).</summary>
public static class GuestLimits
{
    /// <summary>The visitor's IP. Caddy puts Cloudflare's CF-Connecting-IP into X-Forwarded-For (deploy/ensure-caddy.sh).</summary>
    public static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static bool IsGuestChatTurn(HttpContext ctx) =>
        ctx.User.IsGuest() && ctx.GetEndpoint()?.Metadata.GetMetadata<ChatTurnEndpoint>() is not null;

    /// <summary>
    /// Guest chat turns: <paramref name="perHour"/> per IP per hour and <paramref name="perDay"/> for all guests together
    /// per day. Every other request gets no limiter.
    /// </summary>
    public static PartitionedRateLimiter<HttpContext> ChatTurns(int perHour, int perDay) => PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => IsGuestChatTurn(ctx)
            ? RateLimitPartition.GetFixedWindowLimiter("ip:" + ClientIp(ctx),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, perHour), Window = TimeSpan.FromHours(1) })
            : RateLimitPartition.GetNoLimiter("")),
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => IsGuestChatTurn(ctx)
            ? RateLimitPartition.GetFixedWindowLimiter("all-guests",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, perDay), Window = TimeSpan.FromDays(1) })
            : RateLimitPartition.GetNoLimiter("")));
}
