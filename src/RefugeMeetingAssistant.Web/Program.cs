using Amazon.Batch;
using Amazon.SQS;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using MudBlazor.Services;
using MySqlConnector;
using RefugeMeetingAssistant.Api.Data;
using RefugeMeetingAssistant.Api.Models;
using RefugeMeetingAssistant.Api.Services;
using RefugeMeetingAssistant.Web.Components;
using RefugeMeetingAssistant.Web.Data;
using RefugeMeetingAssistant.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Database (EF Core + Aurora MySQL via Pomelo — matching FRED pattern) ----
var dbBuilder = new MySqlConnectionStringBuilder
{
    Server = Environment.GetEnvironmentVariable("FORTRESS_DB_HOST") ?? "localhost",
    Port = uint.Parse(Environment.GetEnvironmentVariable("FORTRESS_DB_PORT") ?? "3306"),
    UserID = Environment.GetEnvironmentVariable("FORTRESS_DB_USER") ?? "root",
    Password = Environment.GetEnvironmentVariable("FORTRESS_DB_PASS") ?? "",
    Database = Environment.GetEnvironmentVariable("MEETINGS_DB_NAME") ?? "meetings_dev",
    AllowPublicKeyRetrieval = true,
    SslMode = MySqlSslMode.None
};
var mysqlConnectionString = dbBuilder.ConnectionString;

builder.Services.AddDbContextFactory<MeetingAssistantDbContext>(options =>
    options.UseMySql(mysqlConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mysql => mysql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));

// ---- Authentication — RN is a cookie consumer of RISE portal ----
// No OIDC here. RISE portal owns Entra SSO and sets .RISE.Session on .refugems.ai.
// If cookie is missing → redirect to portal.refugems.ai for login.
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/auth/redirect-to-login";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.Name = builder.Configuration["Auth__CookieName"] ?? ".RISE.Session";
    options.Cookie.Domain = builder.Configuration["Auth__CookieDomain"] ?? ".refugems.ai";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.IsEssential = true;
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

// ---- DataProtection — shared key ring with RISE portal (rn_fip DB) ----
var keyRingCsb = new MySqlConnectionStringBuilder
{
    Server = Environment.GetEnvironmentVariable("FORTRESS_DB_HOST") ?? "localhost",
    Port = uint.Parse(Environment.GetEnvironmentVariable("FORTRESS_DB_PORT") ?? "3306"),
    UserID = Environment.GetEnvironmentVariable("FORTRESS_DB_USER") ?? "root",
    Password = Environment.GetEnvironmentVariable("FORTRESS_DB_PASS") ?? "",
    Database = Environment.GetEnvironmentVariable("FIP_KEYRING_DB_NAME") ?? "rn_fip",
    ConnectionTimeout = 10
};

builder.Services.AddDbContext<DataProtectionKeyContext>(options =>
    options.UseMySql(keyRingCsb.ConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mysql => mysql.EnableRetryOnFailure(3)));

// Consumer only — RISE portal creates/rotates keys, RN just reads them
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<DataProtectionKeyContext>()
    .SetApplicationName(builder.Configuration["DataProtection__ApplicationName"] ?? "RISE")
    .DisableAutomaticKeyGeneration();

// ---- MudBlazor ----
builder.Services.AddMudServices();

// ---- Blazor Services ----
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddControllers();

// ---- AWS Services ----
builder.Services.AddSingleton<IAmazonSQS>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var serviceUrl = config["AWS:SQS:ServiceUrl"];
    if (!string.IsNullOrEmpty(serviceUrl))
    {
        return new AmazonSQSClient(new AmazonSQSConfig
        {
            ServiceURL = serviceUrl,
            AuthenticationRegion = config["AWS:Region"] ?? "us-east-1"
        });
    }
    return new AmazonSQSClient();
});

builder.Services.AddSingleton<IAmazonBatch>(_ => new AmazonBatchClient(Amazon.RegionEndpoint.USEast1));

// ---- LMA Integration ----
builder.Services.AddHttpClient<LmaClient>(client =>
{
    var appSyncUrl = builder.Configuration["LMA:AppSyncUrl"];
    if (!string.IsNullOrEmpty(appSyncUrl))
    {
        client.BaseAddress = new Uri(appSyncUrl);
    }
});

// ---- Application Services (merged from API) ----
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<BotConfigService>();
builder.Services.AddScoped<MeetingService>();
builder.Services.AddSingleton<SqsService>();
builder.Services.AddScoped<BatchTranscriptionService>();

// Register MeetingApiClient for Blazor pages (still uses HttpClient for internal calls)
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:8080";
builder.Services.AddHttpClient<MeetingApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

var app = builder.Build();

// ---- Forwarded Headers (ALB terminates TLS — tell ASP.NET Core about HTTPS) ----
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor 
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
};
// Trust all proxies/networks (ALB is in the same VPC)
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// ---- Health endpoint ----
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "meetings", timestamp = DateTime.UtcNow }))
    .AllowAnonymous();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ---- Auth Endpoints ----

// Redirect unauthenticated users to RISE portal for login
app.MapGet("/auth/redirect-to-login", (HttpContext ctx, IConfiguration config) =>
{
    var portalUrl = config["FIP__LoginUrl"] ?? "https://portal.refugems.ai";
    var returnUrl = Uri.EscapeDataString($"{ctx.Request.Scheme}://{ctx.Request.Host}/");
    return Results.Redirect($"{portalUrl}?returnUrl={returnUrl}");
}).AllowAnonymous();

app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    var portalUrl = ctx.RequestServices.GetRequiredService<IConfiguration>()["FIP__LoginUrl"] ?? "https://portal.refugems.ai";
    ctx.Response.Redirect(portalUrl);
}).AllowAnonymous();

// Map API controllers (VP bot needs PATCH /api/meetings/{id}/status)
app.MapControllers();

// VpCallback: VP bot calls this when recording ends and audio is ready in S3
app.MapPost("/api/vp/callback", async (
    VpCallbackRequest req,
    BatchTranscriptionService batchSvc,
    IDbContextFactory<MeetingAssistantDbContext> dbFactory,
    ILogger<Program> logger) =>
{
    logger.LogInformation("VpCallback: meeting {MeetingId}, s3Key {Key}", req.MeetingId, req.AudioS3Key);

    await using var db = await dbFactory.CreateDbContextAsync();
    var meeting = await db.Meetings.FindAsync(req.MeetingId);
    if (meeting != null)
    {
        meeting.Status = "transcribing";
        meeting.EndedAt ??= DateTime.UtcNow;
        meeting.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    var jobId = await batchSvc.SubmitTranscriptionJobAsync(req.MeetingId, req.AudioS3Key, req.MeetingDate);
    return Results.Ok(new { jobId, meetingId = req.MeetingId });
}).AllowAnonymous().DisableAntiforgery();

// Map Razor pages
app.MapRazorPages();

// Map Blazor
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ---- Database Initialization (matching FRED pattern) ----
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MeetingAssistantDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var creator = db.Database.GetService<IRelationalDatabaseCreator>();
        if (creator != null)
        {
            if (!await creator.ExistsAsync())
            {
                await creator.CreateAsync();
                logger.LogInformation("Database created");
            }
            if (!await creator.HasTablesAsync())
            {
                await creator.CreateTablesAsync();
                logger.LogInformation("Database tables created");
            }
            else
            {
                logger.LogInformation("Database tables already exist");
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Database initialization failed. App will start but DB operations may fail.");
    }
}

app.Run();
