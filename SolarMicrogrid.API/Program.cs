/*
 * Program.cs
 * -----------------------------------------------------------------------------
 * Purpose : Configures the central API, MongoDB/JWT infrastructure, and scoped
 *           domain services including Component 3 reservation consistency.
 * -----------------------------------------------------------------------------
 */

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Health;
using SolarMicrogrid.API.Helpers;
using SolarMicrogrid.API.Middleware;
using SolarMicrogrid.API.OpenApi;
using SolarMicrogrid.API.Settings;
using SolarMicrogrid.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<MongoSettings>()
    .Bind(builder.Configuration.GetSection(MongoSettings.SectionName))
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.ConnectionString),
        "MongoSettings:ConnectionString is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.DatabaseName),
        "MongoSettings:DatabaseName is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.UsersCollectionName),
        "MongoSettings:UsersCollectionName is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.StationsCollectionName),
        "MongoSettings:StationsCollectionName is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.SlotsCollectionName),
        "MongoSettings:SlotsCollectionName is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.ReservationsCollectionName),
        "MongoSettings:ReservationsCollectionName is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.ReservationSchedulingGuardsCollectionName),
        "MongoSettings:ReservationSchedulingGuardsCollectionName is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.QrTransactionsCollectionName),
        "MongoSettings:QrTransactionsCollectionName is required.")
    .ValidateOnStart();

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    // Build one shared MongoDB client from validated configuration.
    MongoSettings settings = serviceProvider.GetRequiredService<IOptions<MongoSettings>>().Value;
    return new MongoClient(settings.ConnectionString);
});

builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddHostedService<MongoDbIndexInitializer>();
builder.Services.AddSingleton(TimeProvider.System);

// Optional first-Backoffice bootstrap; disabled unless BootstrapBackoffice:Enabled=true.
// Registered after the index initializer so the unique email index exists first.
builder.Services
    .AddOptions<BootstrapBackofficeSettings>()
    .Bind(builder.Configuration.GetSection(BootstrapBackofficeSettings.SectionName));
builder.Services.AddHostedService<BackofficeBootstrapInitializer>();

builder.Services
    .AddOptions<DeploymentSettings>()
    .Bind(builder.Configuration.GetSection(DeploymentSettings.SectionName))
    .Validate(settings => settings.MongoHealthTimeoutSeconds is >= 1 and <= 30,
        "Deployment:MongoHealthTimeoutSeconds must be between 1 and 30.")
    .ValidateOnStart();

bool requireHttpsCorsOrigins = !builder.Environment.IsDevelopment();
builder.Services
    .AddOptions<CorsSettings>()
    .Bind(builder.Configuration.GetSection(CorsSettings.SectionName))
    .Validate(settings => settings.HasValidOrigins(requireHttpsCorsOrigins),
        "Cors:AllowedOrigins must contain exact HTTP(S) origins without wildcards, paths, or credentials; production origins must use HTTPS.")
    .ValidateOnStart();

builder.Services
    .AddOptions<OpenApiSettings>()
    .Bind(builder.Configuration.GetSection(OpenApiSettings.SectionName));

CorsSettings configuredCorsSettings = builder.Configuration
    .GetSection(CorsSettings.SectionName)
    .Get<CorsSettings>() ?? new CorsSettings();
string[] allowedOrigins = configuredCorsSettings.GetNormalizedOrigins();
builder.Services.AddCors(options =>
{
    // Bearer authentication does not require browser credentials; keep the policy origin-specific.
    options.AddPolicy("WebClient", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins.Select(origin => origin.Trim().TrimEnd('/')).ToArray())
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddHealthChecks()
    .AddCheck("api", () => HealthCheckResult.Healthy("API process is available."))
    .AddCheck<MongoDbHealthCheck>("database");

builder.Services
    .AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Key) && settings.Key.Length >= 32,
        "JwtSettings:Key is required and must be at least 32 characters.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Issuer),
        "JwtSettings:Issuer is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Audience),
        "JwtSettings:Audience is required.")
    .Validate(settings => settings.ExpirationMinutes > 0,
        "JwtSettings:ExpirationMinutes must be greater than 0.")
    .ValidateOnStart();

builder.Services
    .AddOptions<BusinessRules>()
    .Bind(builder.Configuration.GetSection(BusinessRules.SectionName))
    .Validate(rules => rules.MaxBookingDaysAhead > 0,
        "BusinessRules:MaxBookingDaysAhead must be greater than 0.")
    .Validate(rules => rules.MinChangeNoticeHours > 0,
        "BusinessRules:MinChangeNoticeHours must be greater than 0.")
    .ValidateOnStart();

builder.Services
    .AddOptions<TransactionSettings>()
    .Bind(builder.Configuration.GetSection(TransactionSettings.SectionName))
    .Validate(settings => settings.QrTokenLifetimeMinutes is >= 1 and <= 30,
        "TransactionSettings:QrTokenLifetimeMinutes must be between 1 and 30.")
    .Validate(settings => settings.VerificationLifetimeMinutes is >= 1 and <= 30,
        "TransactionSettings:VerificationLifetimeMinutes must be between 1 and 30.")
    .ValidateOnStart();

// Creates tokens at login (used by AuthService).
builder.Services.AddSingleton<JwtHelper>();


builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, jwtOptions) =>
    {
        // Apply the validated issuer, audience, signature, and lifetime checks to JWT bearer auth.
        JwtSettings settings = jwtOptions.Value;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
            ValidateLifetime = true
        };
    });

builder.Services.AddControllers();

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ReservationGuardService>();
builder.Services.AddScoped<StationService>();
builder.Services.AddScoped<SlotService>();
builder.Services.AddScoped<StationAccessService>();
builder.Services.AddScoped<MongoTransactionRunner>();
builder.Services.AddScoped<ReservationCapacityService>();
builder.Services.AddScoped<ReservationSchedulingGuardService>();
builder.Services.AddScoped<ReservationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ProsumerService>();

builder.Services.AddOpenApi(options =>
{
    // Keep OpenAPI metadata centralized and generated from the real controller/DTO surface.
    options.AddDocumentTransformer<BearerSecurityDocumentTransformer>();
    options.AddOperationTransformer<Member4OperationTransformer>();
});

var app = builder.Build();


OpenApiSettings openApiSettings = app.Services
    .GetRequiredService<IOptions<OpenApiSettings>>()
    .Value;
if (openApiSettings.Enabled)
{
    app.MapOpenApi();
}

// Development clients (Android emulator on http://10.0.2.2:5076) cannot follow the HTTPS redirect.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();

// Serve the published React client from IIS on the API origin so browser requests can use /api.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("WebClient");

// Authentication (who are you?) must come before authorization (what may you do?).
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    },
    ResponseWriter = SafeHealthResponseWriter.WriteAsync
});

app.MapControllers();

// Preserve client-side routing for non-file web URLs without turning unknown API routes into HTML.
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    string indexPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
    if (!File.Exists(indexPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
});

app.Run();
