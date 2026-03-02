using Amazon.SQS;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using MudBlazor.Services;
using MySqlConnector;
using RefugeMeetingAssistant.Api.Data;
using RefugeMeetingAssistant.Api.Services;
using RefugeMeetingAssistant.Web.Components;
using RefugeMeetingAssistant.Web.Services;
using System.Security.Claims;

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

// ---- Authentication (Cognito OIDC) ----
var cognitoAuthority = builder.Configuration["Auth:CognitoAuthority"];
var cognitoClientId = builder.Configuration["Auth:CognitoClientId"];
var cognitoClientSecret = builder.Configuration["Auth:CognitoClientSecret"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
})
.AddOpenIdConnect(options =>
{
    options.Authority = cognitoAuthority;
    options.ClientId = cognitoClientId;
    options.ClientSecret = cognitoClientSecret;
    options.ResponseType = "code";
    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = true;
    options.CallbackPath = "/signin-oidc";
    options.SignedOutCallbackPath = "/signout-callback-oidc";

    // Map cognito:groups claim to roles
    options.TokenValidationParameters = new TokenValidationParameters
    {
        RoleClaimType = "cognito:groups"
    };

    options.Events = new OpenIdConnectEvents
    {
        OnRedirectToIdentityProvider = ctx =>
        {
            // Force HTTPS redirect URI — always behind ALB TLS termination
            if (ctx.ProtocolMessage.RedirectUri != null && ctx.ProtocolMessage.RedirectUri.StartsWith("http://"))
            {
                ctx.ProtocolMessage.RedirectUri = ctx.ProtocolMessage.RedirectUri.Replace("http://", "https://");
            }
            if (ctx.ProtocolMessage.PostLogoutRedirectUri != null && ctx.ProtocolMessage.PostLogoutRedirectUri.StartsWith("http://"))
            {
                ctx.ProtocolMessage.PostLogoutRedirectUri = ctx.ProtocolMessage.PostLogoutRedirectUri.Replace("http://", "https://");
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = ctx =>
        {
            var groups = ctx.Principal?.FindAll("cognito:groups").Select(c => c.Value) ?? [];
            var identity = ctx.Principal?.Identity as ClaimsIdentity;
            if (identity != null)
            {
                foreach (var group in groups)
                    identity.AddClaim(new Claim(ClaimTypes.Role, group));
            }
            return Task.CompletedTask;
        }
    };

    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("email");
    options.Scope.Add("profile");
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

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
app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
}).AllowAnonymous();

app.MapGet("/auth/dev-login", async (HttpContext ctx) =>
{
    await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
    {
        RedirectUri = "/"
    });
}).AllowAnonymous();

// Map API controllers (VP bot needs PATCH /api/meetings/{id}/status)
app.MapControllers();

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
