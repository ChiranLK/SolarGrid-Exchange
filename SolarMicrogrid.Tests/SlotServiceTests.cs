using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Slots;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using Xunit;

namespace SolarMicrogrid.Tests;

public sealed class SlotServiceTests
{
    private static readonly TimeZoneInfo ColomboTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");

    [Fact]
    public async Task CreateSlot_WithValidRequest_CreatesAvailableSlot()
    {
        (DateTime start, DateTime end) = FutureLocalSlot(10, 11);
        var database = CreateDatabaseWithStation(StationFor(start));
        database.ReturnSlots();
        EnergyBookingSlot? inserted = null;
        database.Slots
            .Setup(collection => collection.InsertOneAsync(
                It.IsAny<EnergyBookingSlot>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<EnergyBookingSlot, InsertOneOptions, CancellationToken>(
                (slot, _, _) => inserted = slot)
            .Returns(Task.CompletedTask);

        SlotResponseDto result = await new SlotService(database.Context).CreateSlotAsync(
            Request(database, start, end, 20), CancellationToken.None);

        Assert.NotNull(inserted);
        Assert.True(ObjectId.TryParse(result.Id, out _));
        Assert.Equal("Available", result.AvailabilityStatus);
        Assert.Equal(20, result.TotalCapacityKwh);
        Assert.Equal(20, result.AvailableCapacityKwh);
    }

    [Fact]
    public async Task CreateSlot_WhenStationIsMissing_IsRejected()
    {
        var database = new MongoTestContext();
        database.ReturnStations();
        (DateTime start, DateTime end) = FutureLocalSlot(10, 11);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new SlotService(database.Context).CreateSlotAsync(
                new CreateSlotRequestDto
                {
                    StationId = ObjectId.GenerateNewId().ToString(),
                    StartTimeUtc = start,
                    EndTimeUtc = end,
                    TotalCapacityKwh = 20
                },
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateSlot_WhenStationIsInactive_IsRejected()
    {
        (DateTime start, DateTime end) = FutureLocalSlot(10, 11);
        var database = CreateDatabaseWithStation(StationFor(start, isActive: false));

        await Assert.ThrowsAsync<ConflictException>(() =>
            new SlotService(database.Context).CreateSlotAsync(
                Request(database, start, end, 20), CancellationToken.None));
    }

    [Fact]
    public async Task CreateSlot_WhenStartIsAfterEnd_IsRejected()
    {
        (DateTime start, DateTime end) = FutureLocalSlot(10, 11);
        var database = CreateDatabaseWithStation(StationFor(start));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SlotService(database.Context).CreateSlotAsync(
                Request(database, end, start, 20), CancellationToken.None));
    }

    [Fact]
    public async Task CreateSlot_WhenStartIsInPast_IsRejected()
    {
        DateTime start = DateTime.UtcNow.AddHours(-2);
        DateTime end = DateTime.UtcNow.AddHours(-1);
        var database = CreateDatabaseWithStation(StationFor(start));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SlotService(database.Context).CreateSlotAsync(
                Request(database, start, end, 20), CancellationToken.None));
    }

    [Fact]
    public async Task CreateSlot_OutsideStationSchedule_IsRejected()
    {
        (DateTime start, DateTime end) = FutureLocalSlot(10, 11);
        DayOfWeek localDay = TimeZoneInfo.ConvertTimeFromUtc(start, ColomboTimeZone).DayOfWeek;
        var station = StationServiceTests.Station(schedule:
        [
            new StationOperatingSchedule
            {
                DayOfWeek = localDay,
                IsOpen = true,
                OpeningTime = "12:00",
                ClosingTime = "18:00"
            }
        ]);
        var database = CreateDatabaseWithStation(station);

        await Assert.ThrowsAsync<ConflictException>(() =>
            new SlotService(database.Context).CreateSlotAsync(
                Request(database, start, end, 20), CancellationToken.None));
    }

    [Fact]
    public async Task CreateSlot_WhenIdenticalSlotExists_IsRejected()
    {
        (DateTime start, DateTime end) = FutureLocalSlot(10, 11);
        var database = CreateDatabaseWithStation(StationFor(start));
        database.ReturnSlots(Slot(database, start, end));

        ConflictException exception = await Assert.ThrowsAsync<ConflictException>(() =>
            new SlotService(database.Context).CreateSlotAsync(
                Request(database, start, end, 20), CancellationToken.None));

        Assert.Contains("overlap", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSlot_WhenSlotOverlaps_IsRejected()
    {
        (DateTime start, DateTime end) = FutureLocalSlot(10, 12);
        var database = CreateDatabaseWithStation(StationFor(start));
        database.ReturnSlots(Slot(database, start.AddMinutes(30), end.AddMinutes(30)));

        await Assert.ThrowsAsync<ConflictException>(() =>
            new SlotService(database.Context).CreateSlotAsync(
                Request(database, start, end, 20), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateSlot_WithInvalidCapacity_IsRejected(decimal capacity)
    {
        (DateTime start, DateTime end) = FutureLocalSlot(10, 11);
        var database = CreateDatabaseWithStation(StationFor(start));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new SlotService(database.Context).CreateSlotAsync(
                Request(database, start, end, capacity), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateSlot_BelowReservedCapacity_IsRejected()
    {
        (DateTime start, DateTime end) = FutureLocalSlot(10, 11);
        var station = StationFor(start);
        var database = CreateDatabaseWithStation(station);
        EnergyBookingSlot existing = Slot(database, start, end, total: 10, available: 3);
        database.Slots
            .SetupSequence(collection => collection.FindAsync<EnergyBookingSlot>(
                It.IsAny<FilterDefinition<EnergyBookingSlot>>(),
                It.IsAny<FindOptions<EnergyBookingSlot, EnergyBookingSlot>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MongoTestContext.Cursor([existing]))
            .ReturnsAsync(MongoTestContext.Cursor<EnergyBookingSlot>([]));

        await Assert.ThrowsAsync<ConflictException>(() =>
            new SlotService(database.Context).UpdateSlotAsync(
                existing.Id,
                new UpdateSlotRequestDto
                {
                    StartTimeUtc = start,
                    EndTimeUtc = end,
                    TotalCapacityKwh = 6
                },
                CancellationToken.None));
    }

    [Fact]
    public async Task ChangeAvailability_CanDisableAndReEnableSlot()
    {
        (DateTime start, DateTime end) = FutureLocalSlot(10, 11);
        var database = new MongoTestContext();
        EnergyBookingSlot available = Slot(database, start, end);
        EnergyBookingSlot unavailable = Clone(available, SlotAvailabilityStatus.Unavailable);
        EnergyBookingSlot enabled = Clone(available, SlotAvailabilityStatus.Available);
        database.Slots
            .SetupSequence(collection => collection.FindAsync<EnergyBookingSlot>(
                It.IsAny<FilterDefinition<EnergyBookingSlot>>(),
                It.IsAny<FindOptions<EnergyBookingSlot, EnergyBookingSlot>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MongoTestContext.Cursor([available]))
            .ReturnsAsync(MongoTestContext.Cursor([unavailable]));
        database.Slots
            .SetupSequence(collection => collection.FindOneAndUpdateAsync<EnergyBookingSlot>(
                It.IsAny<FilterDefinition<EnergyBookingSlot>>(),
                It.IsAny<UpdateDefinition<EnergyBookingSlot>>(),
                It.IsAny<FindOneAndUpdateOptions<EnergyBookingSlot, EnergyBookingSlot>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(unavailable)
            .ReturnsAsync(enabled);
        var service = new SlotService(database.Context);

        SlotResponseDto? disabled = await service.ChangeSlotAvailabilityAsync(
            available.Id,
            new ChangeSlotAvailabilityRequestDto { Status = "Unavailable" },
            CancellationToken.None);
        SlotResponseDto? reEnabled = await service.ChangeSlotAvailabilityAsync(
            available.Id,
            new ChangeSlotAvailabilityRequestDto { Status = "Available" },
            CancellationToken.None);

        Assert.Equal("Unavailable", disabled?.AvailabilityStatus);
        Assert.Equal("Available", reEnabled?.AvailabilityStatus);
    }

    [Fact]
    public async Task AvailableSlotQuery_FiltersStatusCapacityAndExpiry()
    {
        (DateTime start, DateTime end) = FutureLocalSlot(10, 11);
        SolarStationInfo station = StationFor(start);
        var database = CreateDatabaseWithStation(station);
        EnergyBookingSlot available = Slot(database, start, end);
        FilterDefinition<EnergyBookingSlot>? requestedFilter = null;
        database.Slots
            .Setup(collection => collection.CountDocumentsAsync(
                It.IsAny<FilterDefinition<EnergyBookingSlot>>(),
                It.IsAny<CountOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        database.Slots
            .Setup(collection => collection.FindAsync<EnergyBookingSlot>(
                It.IsAny<FilterDefinition<EnergyBookingSlot>>(),
                It.IsAny<FindOptions<EnergyBookingSlot, EnergyBookingSlot>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<EnergyBookingSlot>, FindOptions<EnergyBookingSlot, EnergyBookingSlot>, CancellationToken>(
                (filter, _, _) => requestedFilter = filter)
            .ReturnsAsync(() => MongoTestContext.Cursor([available]));

        PagedSlotResponseDto result = await new SlotService(database.Context)
            .GetAvailableSlotsByStationAsync(
                station.Id,
                new SlotListQueryDto(),
                CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Available", result.Items[0].AvailabilityStatus);
        Assert.NotNull(requestedFilter);
        string filterJson = requestedFilter.Render(
            new RenderArgs<EnergyBookingSlot>(
                BsonSerializer.LookupSerializer<EnergyBookingSlot>(),
                BsonSerializer.SerializerRegistry))
            .ToJson();
        Assert.Contains("availability_status", filterJson);
        Assert.Contains("available_capacity_kwh", filterJson);
        Assert.Contains("end_time", filterJson);
    }

    private static MongoTestContext CreateDatabaseWithStation(SolarStationInfo station)
    {
        var database = new MongoTestContext();
        database.ReturnStations(station);
        return database;
    }

    private static CreateSlotRequestDto Request(
        MongoTestContext database,
        DateTime start,
        DateTime end,
        decimal capacity) => Request(
            database.ConfiguredStationId
                ?? throw new InvalidOperationException("The station mock was not configured."),
            start,
            end,
            capacity);

    private static CreateSlotRequestDto Request(
        string stationId,
        DateTime start,
        DateTime end,
        decimal capacity) => new()
    {
        StationId = stationId,
        StartTimeUtc = start,
        EndTimeUtc = end,
        TotalCapacityKwh = capacity
    };

    private static SolarStationInfo StationFor(DateTime slotStart, bool isActive = true)
    {
        DayOfWeek localDay = TimeZoneInfo.ConvertTimeFromUtc(slotStart, ColomboTimeZone).DayOfWeek;
        return StationServiceTests.Station(
            isActive,
            schedule:
            [
                new StationOperatingSchedule
                {
                    DayOfWeek = localDay,
                    IsOpen = true,
                    OpeningTime = "00:00",
                    ClosingTime = "23:59"
                }
            ]);
    }

    private static EnergyBookingSlot Slot(
        MongoTestContext database,
        DateTime start,
        DateTime end,
        decimal total = 20,
        decimal? available = null) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        StationId = database.ConfiguredStationId ?? ObjectId.GenerateNewId().ToString(),
        StartTimeUtc = start,
        EndTimeUtc = end,
        TotalCapacityKwh = total,
        AvailableCapacityKwh = available ?? total,
        AvailabilityStatus = SlotAvailabilityStatus.Available,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static EnergyBookingSlot Clone(
        EnergyBookingSlot source,
        SlotAvailabilityStatus status) => new()
    {
        Id = source.Id,
        StationId = source.StationId,
        StartTimeUtc = source.StartTimeUtc,
        EndTimeUtc = source.EndTimeUtc,
        TotalCapacityKwh = source.TotalCapacityKwh,
        AvailableCapacityKwh = source.AvailableCapacityKwh,
        AvailabilityStatus = status,
        CreatedAtUtc = source.CreatedAtUtc,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static (DateTime StartUtc, DateTime EndUtc) FutureLocalSlot(
        int startHour,
        int endHour)
    {
        DateTime localDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ColomboTimeZone)
            .Date
            .AddDays(2);
        DateTime localStart = DateTime.SpecifyKind(localDate.AddHours(startHour), DateTimeKind.Unspecified);
        DateTime localEnd = DateTime.SpecifyKind(localDate.AddHours(endHour), DateTimeKind.Unspecified);
        return (
            TimeZoneInfo.ConvertTimeToUtc(localStart, ColomboTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, ColomboTimeZone));
    }
}
