/*
 * Program.cs
 * -----------------------------------------------------------------------------
 * Purpose : Configures the central API, MongoDB/JWT infrastructure, and scoped
 *           domain services including Component 3 reservation consistency.
 * -----------------------------------------------------------------------------
 */

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Helpers;
using SolarMicrogrid.API.Middleware;
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
    .ValidateOnStart();

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    // Build one shared MongoDB client from validated configuration.
    MongoSettings settings = serviceProvider.GetRequiredService<IOptions<MongoSettings>>().Value;
    return new MongoClient(settings.ConnectionString);
});

builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddHostedService<MongoDbIndexInitializer>();

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
builder.Services.AddScoped<StationService>();
builder.Services.AddScoped<SlotService>();
builder.Services.AddScoped<StationAccessService>();
builder.Services.AddScoped<MongoTransactionRunner>();
builder.Services.AddScoped<ReservationCapacityService>();
builder.Services.AddScoped<ReservationSchedulingGuardService>();
builder.Services.AddScoped<ReservationService>();

builder.Services.AddOpenApi();

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseMiddleware<ExceptionMiddleware>();

// Authentication (who are you?) must come before authorization (what may you do?).
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
