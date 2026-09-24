using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;
using Moq;
using SolarMicrogrid.API.Controllers;
using SolarMicrogrid.API.Models.DTOs.Stations;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using Xunit;

namespace SolarMicrogrid.Tests;

public sealed class StationServiceTests
{
    [Fact]
    public async Task CreateStation_WithValidRequest_CreatesActiveStation()
    {
        var database = new MongoTestContext();
        SolarStationInfo? inserted = null;
        database.Stations
            .Setup(collection => collection.InsertOneAsync(
                It.IsAny<SolarStationInfo>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<SolarStationInfo, InsertOneOptions, CancellationToken>(
                (station, _, _) => inserted = station)
            .Returns(Task.CompletedTask);

        StationResponseDto result = await Service(database)
            .CreateStationAsync(ValidCreateRequest(), CancellationToken.None);

        Assert.NotNull(inserted);
        Assert.True(ObjectId.TryParse(result.Id, out _));
        Assert.True(result.IsActive);
        Assert.Equal("Central Solar Hub", inserted.Name);
        Assert.Equal(result.CreatedAtUtc, result.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(-90.01)]
    [InlineData(90.01)]
    [InlineData(double.NaN)]
    public async Task CreateStation_WithInvalidLatitude_IsRejected(double latitude)
    {
        var request = ValidCreateRequest();
        request.Latitude = latitude;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Service(new MongoTestContext())
                .CreateStationAsync(request, CancellationToken.None));
    }

    [Theory]
    [InlineData(-180.01)]
    [InlineData(180.01)]
    [InlineData(double.PositiveInfinity)]
    public async Task CreateStation_WithInvalidLongitude_IsRejected(double longitude)
    {
        var request = ValidCreateRequest();
        request.Longitude = longitude;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Service(new MongoTestContext())
                .CreateStationAsync(request, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateStation_WithNonPositiveGenerationCapacity_IsRejected(decimal capacity)
    {
        var request = ValidCreateRequest();
        request.EnergyGenerationCapacityKw = capacity;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Service(new MongoTestContext())
                .CreateStationAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateStation_WithInvalidSchedule_IsRejected()
    {
        var request = ValidCreateRequest();
        request.OperatingSchedule[0].OpeningTime = "18:00";
        request.OperatingSchedule[0].ClosingTime = "08:00";

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(new MongoTestContext())
                .CreateStationAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateStation_WithDuplicateScheduleDay_IsRejected()
    {
        var request = ValidCreateRequest();
        request.OperatingSchedule.Add(new OperatingScheduleDto
        {
            DayOfWeek = "Monday",
            IsOpen = false
        });

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(new MongoTestContext())
                .CreateStationAsync(request, CancellationToken.None));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateStation_PreservesIdAndCreationDate()
    {
        var database = new MongoTestContext();
        DateTime createdAt = DateTime.UtcNow.AddDays(-7);
        SolarStationInfo existing = Station(createdAt: createdAt);
        database.ReturnStations(existing);
        SolarStationInfo? replacement = null;
        database.Stations
            .Setup(collection => collection.ReplaceOneAsync(
                It.IsAny<FilterDefinition<SolarStationInfo>>(),
                It.IsAny<SolarStationInfo>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<SolarStationInfo>, SolarStationInfo, ReplaceOptions, CancellationToken>(
                (_, station, _, _) => replacement = station)
            .ReturnsAsync(Mock.Of<ReplaceOneResult>());

        var request = new UpdateStationRequestDto
        {
            Name = "Updated Hub",
            Address = "Updated address",
            Latitude = 6.91,
            Longitude = 79.87,
            EnergyGenerationCapacityKw = 120,
            BatteryStorageCapacityKwh = 40,
            OperatingSchedule = ValidSchedule()
        };
        StationResponseDto? result = await Service(database)
            .UpdateStationAsync(existing.Id, request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(replacement);
        Assert.Equal(existing.Id, result.Id);
        Assert.Equal(createdAt, result.CreatedAtUtc);
        Assert.Equal(existing.Id, replacement.Id);
        Assert.Equal(createdAt, replacement.CreatedAtUtc);
    }

    [Fact]
    public void CreateStation_RequiresAuthenticatedBackofficeUser()
    {
        AuthorizeAttribute? controllerAuthorization = typeof(StationsController)
            .GetCustomAttribute<AuthorizeAttribute>();
        AuthorizeAttribute? actionAuthorization = typeof(StationsController)
            .GetMethod(nameof(StationsController.CreateStation))!
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(controllerAuthorization);
        Assert.Equal(nameof(UserRole.Backoffice), actionAuthorization?.Roles);
    }

    [Fact]
    public async Task NearbyQuery_RequestsActiveStationsOnly()
    {
        var database = new MongoTestContext();
        SolarStationInfo activeStation = Station();
        FilterDefinition<SolarStationInfo>? requestedFilter = null;
        database.Stations
            .Setup(collection => collection.FindAsync<SolarStationInfo>(
                It.IsAny<FilterDefinition<SolarStationInfo>>(),
                It.IsAny<FindOptions<SolarStationInfo, SolarStationInfo>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<SolarStationInfo>, FindOptions<SolarStationInfo, SolarStationInfo>, CancellationToken>(
                (filter, _, _) => requestedFilter = filter)
            .ReturnsAsync(() => MongoTestContext.Cursor([activeStation]));

        IReadOnlyList<NearbyStationResponseDto> result = await Service(database)
            .GetNearbyStationsAsync(6.9, 79.86, 25, 20, CancellationToken.None);

        Assert.Single(result);
        Assert.True(result[0].Station.IsActive);
        Assert.NotNull(requestedFilter);
        string filterJson = requestedFilter.Render(
            new RenderArgs<SolarStationInfo>(
                BsonSerializer.LookupSerializer<SolarStationInfo>(),
                BsonSerializer.SerializerRegistry))
            .ToJson();
        Assert.Contains("is_active", filterJson);
        Assert.Contains("true", filterJson, StringComparison.OrdinalIgnoreCase);
    }

    private static StationService Service(MongoTestContext database) =>
        new(database.Context, new ReservationGuardService(database.Context));

    internal static CreateStationRequestDto ValidCreateRequest() => new()
    {
        Name = " Central Solar Hub ",
        Description = "Test station",
        Address = "Colombo",
        Latitude = 6.9,
        Longitude = 79.86,
        EnergyGenerationCapacityKw = 100,
        BatteryStorageCapacityKwh = 25,
        OperatingSchedule = ValidSchedule()
    };

    internal static List<OperatingScheduleDto> ValidSchedule() =>
    [
        new()
        {
            DayOfWeek = "Monday",
            IsOpen = true,
            OpeningTime = "08:00",
            ClosingTime = "18:00"
        }
    ];

    internal static SolarStationInfo Station(
        bool isActive = true,
        DateTime? createdAt = null,
        List<StationOperatingSchedule>? schedule = null) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        Name = "Central Solar Hub",
        Address = "Colombo",
        Location = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
            new GeoJson2DGeographicCoordinates(79.86, 6.9)),
        EnergyGenerationCapacityKw = 100,
        BatteryStorageCapacityKwh = 25,
        OperatingSchedule = schedule ??
        [
            new StationOperatingSchedule
            {
                DayOfWeek = DayOfWeek.Monday,
                IsOpen = true,
                OpeningTime = "08:00",
                ClosingTime = "18:00"
            }
        ],
        IsActive = isActive,
        CreatedAtUtc = createdAt ?? DateTime.UtcNow.AddDays(-1),
        UpdatedAtUtc = DateTime.UtcNow.AddDays(-1)
    };
}
