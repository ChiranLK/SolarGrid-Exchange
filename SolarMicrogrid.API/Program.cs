using Microsoft.Extensions.Options;
using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Settings;

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
    .ValidateOnStart();

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    MongoSettings settings = serviceProvider.GetRequiredService<IOptions<MongoSettings>>().Value;
    return new MongoClient(settings.ConnectionString);
});

builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddHostedService<MongoDbIndexInitializer>();

builder.Services.AddControllers();

builder.Services.AddOpenApi();

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
