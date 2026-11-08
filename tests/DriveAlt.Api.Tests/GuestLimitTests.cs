using System.Net;
using System.Security.Claims;
using DriveAlt.Api.Auth;
using DriveAlt.Api.Data;
using Microsoft.AspNetCore.Http;

namespace DriveAlt.Api.Tests;

/// <summary>The guest chat limits on their own, without a database: per-IP hourly allowance and the shared daily ceiling.</summary>
public class GuestLimitTests
{
    private static HttpContext Turn(string role, string ip, bool chatTurn = true)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test")),
        };
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        var metadata = chatTurn ? new EndpointMetadataCollection(new ChatTurnEndpoint()) : new EndpointMetadataCollection();
        ctx.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));
        return ctx;
    }

    [Fact]
    public void Each_ip_gets_its_own_hourly_allowance()
    {
        using var limiter = GuestLimits.ChatTurns(perHour: 2, perDay: 100);
        Assert.True(limiter.AttemptAcquire(Turn(Roles.Guest, "203.0.113.1")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Turn(Roles.Guest, "203.0.113.1")).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Turn(Roles.Guest, "203.0.113.1")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Turn(Roles.Guest, "203.0.113.2")).IsAcquired);
    }

    [Fact]
    public void All_guests_share_one_daily_ceiling()
    {
        using var limiter = GuestLimits.ChatTurns(perHour: 10, perDay: 3);
        for (var i = 1; i <= 3; i++) Assert.True(limiter.AttemptAcquire(Turn(Roles.Guest, $"203.0.113.{i}")).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Turn(Roles.Guest, "203.0.113.9")).IsAcquired);
    }

    [Fact]
    public void Students_and_other_requests_are_never_limited()
    {
        using var limiter = GuestLimits.ChatTurns(perHour: 1, perDay: 1);
        Assert.True(limiter.AttemptAcquire(Turn(Roles.Guest, "203.0.113.1")).IsAcquired);
        for (var i = 0; i < 5; i++)
        {
            Assert.True(limiter.AttemptAcquire(Turn(Roles.Student, "203.0.113.1")).IsAcquired);
            Assert.True(limiter.AttemptAcquire(Turn(Roles.Uploader, "203.0.113.1")).IsAcquired);
            Assert.True(limiter.AttemptAcquire(Turn(Roles.Guest, "203.0.113.1", chatTurn: false)).IsAcquired);
        }
    }
}
