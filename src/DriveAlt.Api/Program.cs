using System.Threading.RateLimiting;
using DriveAlt.Api.Admin;
using DriveAlt.Api.Ai;
using DriveAlt.Api.Auth;
using DriveAlt.Api.Bookmarks;
using DriveAlt.Api.Browse;
using DriveAlt.Api.Chat;
using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using DriveAlt.Api.Import;
using DriveAlt.Api.Infrastructure;
using DriveAlt.Api.Inspect;
using DriveAlt.Api.Processing;
using DriveAlt.Api.Search;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
var importMode = args.Length > 0 && args[0] == "import-drive";

builder.Services.Configure<AppOptions>(cfg.GetSection("App"));
builder.Services.Configure<AuthOptions>(cfg.GetSection("Auth"));
builder.Services.Configure<ResendOptions>(cfg.GetSection("Resend"));
builder.Services.Configure<GeminiOptions>(cfg.GetSection("Gemini"));
builder.Services.Configure<StorageOptions>(cfg.GetSection("Storage"));
builder.Services.Configure<ProcessingOptions>(cfg.GetSection("Processing"));
builder.Services.Configure<GoogleOptions>(cfg.GetSection("Google"));

builder.Services.AddDbContext<AppDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Db"), npg => npg.UseVector())
    .UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Storage>();
builder.Services.AddSingleton<DocumentConverter>();
builder.Services.AddSingleton<PageRenderer>();
builder.Services.AddHttpClient<GeminiClient>(c => c.Timeout = TimeSpan.FromMinutes(3));
builder.Services.AddScoped<IMaterialAi, MaterialAi>();
builder.Services.AddScoped<ProcessingService>();
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<ResultCards>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddHttpClient<DriveImporter>(c => c.Timeout = TimeSpan.FromMinutes(10));

if (string.IsNullOrWhiteSpace(cfg["Resend:ApiKey"]))
    builder.Services.AddSingleton<IEmailSender, DevEmailSender>();
else
    builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>();

if (!importMode && cfg.GetValue("Processing:Workers", 2) > 0)
    builder.Services.AddHostedService<JobWorker>();
builder.Services.AddSingleton<JobWorker>();

var storageRoot = cfg["Storage:Root"] ?? "storage";
if (!Path.IsPathRooted(storageRoot)) storageRoot = Path.Combine(builder.Environment.ContentRootPath, storageRoot);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(Directory.CreateDirectory(Path.Combine(storageRoot, "keys")))
    .SetApplicationName("DriveAlt");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "drivealt_session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
        o.Events.OnValidatePrincipal = RoleCache.ValidateAsync;
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthEndpoints.UploaderPolicy, p => p.RequireRole(Roles.Uploader));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("otp", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(10) }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddProblemDetails();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Database.MigrateAsync();
    await Seeder.SeedCoursesAsync(db);
}

if (importMode)
{
    await using var scope = app.Services.CreateAsyncScope();
    var importer = scope.ServiceProvider.GetRequiredService<DriveImporter>();
    return await importer.RunAsync(args.Skip(1).ToArray(), CancellationToken.None);
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { ok = true }));
app.MapGet("/api/config", (Microsoft.Extensions.Options.IOptions<AppOptions> a, Microsoft.Extensions.Options.IOptions<AuthOptions> au,
    Microsoft.Extensions.Options.IOptions<GeminiOptions> g) => new
{
    name = a.Value.Name,
    emailDomain = au.Value.EmailTemplate.Replace("{id}", ""),
    aiOnline = g.Value.Enabled,
});
AuthEndpoints.Map(app);
FileEndpoints.Map(app);
BrowseEndpoints.Map(app);
ChatEndpoints.Map(app);
InspectEndpoints.Map(app);
BookmarkEndpoints.Map(app);
AdminEndpoints.Map(app);

app.MapFallback("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
return 0;

public partial class Program;
