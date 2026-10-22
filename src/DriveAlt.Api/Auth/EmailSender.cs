using System.Collections.Concurrent;
using System.Net.Http.Json;
using DriveAlt.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace DriveAlt.Api.Auth;

public interface IEmailSender
{
    Task SendOtpAsync(string to, string code, CancellationToken ct);
}

public sealed class ResendEmailSender(HttpClient http, IOptions<ResendOptions> opts, IOptions<AppOptions> app) : IEmailSender
{
    public async Task SendOtpAsync(string to, string code, CancellationToken ct)
    {
        var name = app.Value.Name;
        var html = $"""
            <div style="font-family:Nunito,Arial,sans-serif;background:#FFF3D9;padding:32px">
              <div style="max-width:420px;margin:auto;background:#fff;border-radius:28px;padding:32px;border-bottom:6px solid #EBD7AF">
                <h1 style="margin:0 0 8px;font-size:28px;color:#2A1B0F">Your {name} code</h1>
                <p style="color:#5E4329;font-size:16px;margin:0 0 20px">Type this in to sign in. It expires in 10 minutes.</p>
                <div style="font-size:40px;letter-spacing:10px;font-weight:900;color:#2A1B0F;background:#FFF3D9;border-radius:20px;padding:16px;text-align:center">{code}</div>
                <p style="color:#7A5A3A;font-size:13px;margin:20px 0 0">Didn't ask for this? Ignore it.</p>
              </div>
            </div>
            """;
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        req.Headers.Authorization = new("Bearer", opts.Value.ApiKey);
        req.Content = JsonContent.Create(new
        {
            from = opts.Value.From,
            to = new[] { to },
            subject = $"{code} is your {name} code",
            html,
            text = $"Your {name} sign-in code is {code}. It expires in 10 minutes.",
        });
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Resend failed ({(int)res.StatusCode}): {body}");
        }
    }
}

/// <summary>Development sender: logs the code and keeps it so local tests can read it from /api/dev/otp.</summary>
public sealed class DevEmailSender(ILogger<DevEmailSender> log) : IEmailSender
{
    public static readonly ConcurrentDictionary<string, string> LastCodes = new(StringComparer.OrdinalIgnoreCase);

    public Task SendOtpAsync(string to, string code, CancellationToken ct)
    {
        LastCodes[to] = code;
        log.LogWarning("DEV OTP for {To}: {Code}", to, code);
        return Task.CompletedTask;
    }
}
