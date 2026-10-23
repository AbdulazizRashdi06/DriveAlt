using System.Net;
using System.Net.Http.Json;
using System.Text;
using DriveAlt.Api.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace DriveAlt.Api.Tests;

/// <summary>
/// Runs the real API against a throwaway Postgres database (needs pgvector) and a temp storage folder, with the
/// offline AI (no Gemini key) and the dev email sender. Set DRIVEALT_TEST_PG to point at another server.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Uploader = "24-0161";
    private static readonly string Admin = Environment.GetEnvironmentVariable("DRIVEALT_TEST_PG")
        ?? "Host=localhost;Username=postgres;Password=postgres;Database=postgres";

    private readonly string _db = "drivealt_test_" + Guid.NewGuid().ToString("N")[..12];
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "drivealt-test-" + Guid.NewGuid().ToString("N")[..8]);

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(Admin);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand($"CREATE DATABASE {_db}", conn)) await cmd.ExecuteNonQueryAsync();

        var cs = new NpgsqlConnectionStringBuilder(Admin) { Database = _db }.ConnectionString;
        // Environment variables are read before the host is built (Program decides on workers/email at startup).
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ConnectionStrings__Db", cs);
        Environment.SetEnvironmentVariable("Storage__Root", StorageRoot);
        Environment.SetEnvironmentVariable("Processing__Workers", "0");
        Environment.SetEnvironmentVariable("Auth__Uploaders__0", Uploader);
        Environment.SetEnvironmentVariable("Gemini__ApiKey", "");
        Environment.SetEnvironmentVariable("DeepSeek__ApiKey", "");
        Environment.SetEnvironmentVariable("Codex__BaseUrl", "");
        Environment.SetEnvironmentVariable("Resend__ApiKey", "");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(Admin);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_db} WITH (FORCE)", conn)) await cmd.ExecuteNonQueryAsync();
        try { Directory.Delete(StorageRoot, true); } catch (IOException) { }
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

    private readonly Dictionary<string, HttpClient> _clients = [];

    /// <summary>Signs in once per uni ID (the server only sends one code per 45 s) and reuses that session.</summary>
    public async Task<HttpClient> SignInAsync(string uniId)
    {
        if (_clients.TryGetValue(uniId, out var existing)) return existing;
        var client = NewClient();
        _clients[uniId] = client;
        var r = await client.PostAsJsonAsync("/api/auth/request", new { uniId });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var code = DevEmailSender.LastCodes[$"{uniId}@student.gutech.edu.om"];
        var v = await client.PostAsJsonAsync("/api/auth/verify", new { uniId, code });
        Assert.Equal(HttpStatusCode.OK, v.StatusCode);
        return client;
    }

    /// <summary>Runs queued processing jobs until the queue is empty.</summary>
    public async Task DrainJobsAsync()
    {
        var worker = Services.GetRequiredService<DriveAlt.Api.Processing.JobWorker>();
        for (var i = 0; i < 50 && await worker.RunOnceAsync(CancellationToken.None); i++) { }
    }
}

/// <summary>Writes a tiny but valid one-page PDF with real text, so poppler can render and extract it.</summary>
public static class MinimalPdf
{
    public static byte[] Create(params string[] lines)
    {
        var content = new StringBuilder("BT /F1 12 Tf 50 780 Td 16 TL\n");
        foreach (var l in lines) content.Append('(').Append(l.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")).Append(") Tj T*\n");
        content.Append("ET");
        var stream = content.ToString();
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
