using Amazon.SQS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.OpenApi.Models;
using MySqlConnector;
using RefugeMeetingAssistant.Api.Data;
using RefugeMeetingAssistant.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Database (EF Core + Aurora MySQL via Pomelo) ----
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

// Also register DbContext for direct injection (controllers use it)
builder.Services.AddDbContext<MeetingAssistantDbContext>(options =>
    options.UseMySql(mysqlConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mysql => mysql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));

// ---- Authentication (Cognito JWT Bearer) ----
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:CognitoAuthority"];
        options.Audience = builder.Configuration["Auth:CognitoClientId"];
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = false, // Cognito client_id is in client_id claim, not aud
            RoleClaimType = "cognito:groups"
        };
    });

builder.Services.AddAuthorization();

// ---- AWS Services ----

// SQS (for VP bot orchestration, dev mode with LocalStack)
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
// AppSync GraphQL client for reading transcripts/summaries from LMA
builder.Services.AddHttpClient<LmaClient>(client =>
{
    var appSyncUrl = builder.Configuration["LMA:AppSyncUrl"];
    if (!string.IsNullOrEmpty(appSyncUrl))
    {
        client.BaseAddress = new Uri(appSyncUrl);
    }
});

// ---- Application Services ----
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<BotConfigService>();
builder.Services.AddScoped<MeetingService>();
builder.Services.AddSingleton<SqsService>();

// ---- API Configuration ----
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Refuge Meeting Assistant API",
        Version = "v1",
        Description = "Extension layer on top of AWS LMA — multi-user meeting intelligence with Teams VP bot support.\n\n" +
                      "LMA (CloudFormation) provides: transcription, summarization, Chrome extension, web UI.\n" +
                      "This API adds: Entra auth, per-user bot config, Teams VP orchestration, action item management.",
        Contact = new OpenApiContact
        {
            Name = "Fred White",
            Email = "fwhite@refugems.com"
        }
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. In dev mode, any token works.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

// ---- Middleware Pipeline ----
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Refuge Meeting Assistant API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/api/health/live");

// ---- Database Initialization (IRelationalDatabaseCreator — matching FRED pattern) ----
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
        logger.LogWarning(ex, "Database initialization failed. API will start but DB operations may fail.");
    }
}

app.Logger.LogInformation("Refuge Meeting Assistant API started");
app.Logger.LogInformation("  Swagger: {BaseUrl}/swagger", app.Urls.FirstOrDefault() ?? "http://localhost:5000");
app.Logger.LogInformation("  Auth: Cognito JWT Bearer");
app.Logger.LogInformation("  LMA: {Mode}", builder.Configuration.GetValue<bool>("LMA:UseMock", true) ? "Mock (LMA not connected)" : "Connected");

app.Run();
