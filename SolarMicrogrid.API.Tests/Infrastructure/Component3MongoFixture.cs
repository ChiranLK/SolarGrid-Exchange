/*
 * Component3MongoFixture.cs
 * -----------------------------------------------------------------------------
 * Purpose : Creates a unique real MongoDB test database, production services,
 *           deterministic clock, indexes, and reusable Component 3 test data.
 * Isolation: Documents are reset before each test and the unique database is
 *            dropped after the collection.
 * -----------------------------------------------------------------------------
 */

using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Models.DTOs.Reservations;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using SolarMicrogrid.API.Settings;
using Xunit;

namespace SolarMicrogrid.API.Tests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Component3MongoCollection : ICollectionFixture<Component3MongoFixture>
{
    public const string Name = "Component 3 MongoDB integration";
}

public sealed class Component3MongoFixture : IAsyncLifetime
{
    internal const string ProsumerOneNic = "200012345678";
    internal const string ProsumerTwoNic = "200112345678";
    internal const string BackofficeNic = "199012345678";
    internal const string GridOperatorNic = "199112345678";
    internal const string InactiveProsumerNic = "200212345678";

    internal static readonly DateTime FixedNowUtc =
        new(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly MongoSettings _settings;

    public Component3MongoFixture()
    {
        // Require the explicit integration-test connection supplied by the runner script.
        _connectionString = Environment.GetEnvironmentVariable("COMPONENT3_TEST_MONGODB_URI")
            ?? "mongodb://localhost:27018/?replicaSet=rs0&directConnection=true";
        _databaseName = $"component3_tests_{Guid.NewGuid():N}";
        _settings = new MongoSettings
        {
            ConnectionString = _connectionString,
            DatabaseName = _databaseName,
            UsersCollectionName = "Users",
            StationsCollectionName = "SolarStationInfo",
            SlotsCollectionName = "EnergyBookingSlots",
            ReservationsCollectionName = "EnergyReservations",
            ReservationSchedulingGuardsCollectionName = "ReservationSchedulingGuards"
        };
        Client = new MongoClient(_connectionString);
        Clock = new FixedTimeProvider(FixedNowUtc);
        RebuildServices();
    }

    internal IMongoClient Client { get; }

    internal FixedTimeProvider Clock { get; }

    internal MongoDbContext Context { get; private set; } = null!;

    internal ReservationService Reservations { get; private set; } = null!;

    internal string StationOneId { get; private set; } = string.Empty;

    internal string StationTwoId { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        // Fail honestly when MongoDB is unavailable, then create indexes once per isolated database.
        await Client.GetDatabase("admin").RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1));
        var indexInitializer = new MongoDbIndexInitializer(Context);
        await indexInitializer.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        // Remove the isolated database regardless of individual test outcomes.
        await Client.DropDatabaseAsync(_databaseName);
    }

    internal async Task ResetAsync()
    {
        // Clear documents while retaining indexes, then seed authoritative references for one test.
        await Context.ReservationSchedulingGuards.DeleteManyAsync(
            Builders<ReservationSchedulingGuard>.Filter.Empty);
        await Context.Reservations.DeleteManyAsync(Builders<EnergyReservation>.Filter.Empty);
        await Context.Slots.DeleteManyAsync(Builders<EnergyBookingSlot>.Filter.Empty);
        await Context.Stations.DeleteManyAsync(Builders<SolarStationInfo>.Filter.Empty);
        await Context.Users.DeleteManyAsync(Builders<User>.Filter.Empty);
        Clock.SetUtcNow(FixedNowUtc);

        StationOneId = ObjectId.GenerateNewId().ToString();
        StationTwoId = ObjectId.GenerateNewId().ToString();
        await Context.Users.InsertManyAsync(
        [
            BuildUser(ProsumerOneNic, UserRole.Prosumer, UserStatus.Active),
            BuildUser(ProsumerTwoNic, UserRole.Prosumer, UserStatus.Active),
            BuildUser(BackofficeNic, UserRole.Backoffice, UserStatus.Active),
            BuildUser(
                GridOperatorNic,
                UserRole.GridOperator,
                UserStatus.Active,
                StationOneId),
            BuildUser(InactiveProsumerNic, UserRole.Prosumer, UserStatus.Deactivated)
        ]);
        await Context.Stations.InsertManyAsync(
        [
            BuildStation(StationOneId, isActive: true),
            BuildStation(StationTwoId, isActive: true)
        ]);
    }

    internal async Task<EnergyBookingSlot> AddSlotAsync(
        DateTime startUtc,
        decimal totalCapacityKwh = 10m,
        decimal? availableCapacityKwh = null,
        SlotAvailabilityStatus status = SlotAvailabilityStatus.Available,
        string? stationId = null)
    {
        // Persist a slot directly so reservation tests isolate Component 3 behavior.
        var slot = new EnergyBookingSlot
        {
            Id = ObjectId.GenerateNewId().ToString(),
            StationId = stationId ?? StationOneId,
            StartTimeUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            EndTimeUtc = DateTime.SpecifyKind(startUtc.AddHours(1), DateTimeKind.Utc),
            TotalCapacityKwh = totalCapacityKwh,
            AvailableCapacityKwh = availableCapacityKwh ?? totalCapacityKwh,
            AvailabilityStatus = status,
            CreatedAtUtc = FixedNowUtc,
            UpdatedAtUtc = FixedNowUtc
        };
        await Context.Slots.InsertOneAsync(slot);
        return slot;
    }

    internal Task<ReservationCreationResult> CreateOwnAsync(
        EnergyBookingSlot slot,
        string key,
        decimal energyKwh = 5m,
        string prosumerNic = ProsumerOneNic)
    {
        // Call the production creation workflow with authenticated Prosumer identity.
        return Reservations.CreateOwnReservationAsync(
            prosumerNic,
            UserRole.Prosumer.ToString(),
            new CreateReservationRequestDto
            {
                SlotId = slot.Id,
                RequestedEnergyKwh = energyKwh
            },
            key,
            CancellationToken.None);
    }

    internal async Task<EnergyReservation> LoadReservationAsync(string reservationId)
    {
        // Read the stored entity only inside tests that assert persistence invariants.
        return await Context.Reservations
            .Find(item => item.Id == reservationId)
            .SingleAsync();
    }

    internal async Task<EnergyBookingSlot> LoadSlotAsync(string slotId)
    {
        // Read current capacity and allocation claims from the real MongoDB document.
        return await Context.Slots.Find(item => item.Id == slotId).SingleAsync();
    }

    internal async Task<bool> TryCompleteWithCompareAndSwapAsync(
        string reservationId,
        long expectedVersion,
        string verificationId)
    {
        // Model Member 4's documented completion CAS against Component 3 lifecycle fields.
        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(item => item.Id, reservationId),
            Builders<EnergyReservation>.Filter.Eq(item => item.Status, ReservationStatus.Approved),
            Builders<EnergyReservation>.Filter.Eq(item => item.Version, expectedVersion),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.CapacityState,
                ReservationCapacityState.Held));
        UpdateDefinition<EnergyReservation> update = Builders<EnergyReservation>.Update
            .Set(item => item.Status, ReservationStatus.Completed)
            .Set(item => item.Version, expectedVersion + 1)
            .Set(item => item.CapacityState, ReservationCapacityState.Consumed)
            .Set(item => item.CompletedAtUtc, Clock.GetUtcNow().UtcDateTime)
            .Set(item => item.CompletedByActorNic, GridOperatorNic)
            .Set(item => item.CompletedVerificationId, verificationId)
            .Set(item => item.UpdatedAtUtc, Clock.GetUtcNow().UtcDateTime)
            .Set(item => item.UpdatedByActorNic, GridOperatorNic);
        UpdateResult result = await Context.Reservations.UpdateOneAsync(filter, update);
        return result.ModifiedCount == 1;
    }

    private void RebuildServices()
    {
        // Rebind collection handles and services after the isolated database is dropped.
        Context = new MongoDbContext(Client, Options.Create(_settings));
        var capacityService = new ReservationCapacityService(Context, Clock);
        var transactionRunner = new MongoTransactionRunner(Client);
        var schedulingGuard = new ReservationSchedulingGuardService(Context, Clock);
        Reservations = new ReservationService(
            Context,
            capacityService,
            transactionRunner,
            schedulingGuard,
            Options.Create(new BusinessRules
            {
                MaxBookingDaysAhead = 7,
                MinChangeNoticeHours = 12
            }),
            Clock);
    }

    private static User BuildUser(
        string nic,
        UserRole role,
        UserStatus status,
        string? assignedStationId = null)
    {
        // Build the authoritative user fields consumed by reservation authorization.
        return new User
        {
            Nic = nic,
            FullName = $"Test {role}",
            Email = $"{nic}@example.test",
            Phone = "0700000000",
            PasswordHash = "not-used-by-tests",
            Role = role,
            Status = status,
            AssignedStationId = assignedStationId,
            CreatedAtUtc = FixedNowUtc,
            UpdatedAtUtc = FixedNowUtc
        };
    }

    private static SolarStationInfo BuildStation(string stationId, bool isActive)
    {
        // Keep every UTC test slot inside an open Colombo operating day.
        return new SolarStationInfo
        {
            Id = stationId,
            Name = $"Station {stationId[^4..]}",
            Address = "Integration test address",
            IsActive = isActive,
            EnergyGenerationCapacityKw = 100m,
            BatteryStorageCapacityKwh = 200m,
            CreatedAtUtc = FixedNowUtc,
            UpdatedAtUtc = FixedNowUtc,
            OperatingSchedule = Enum.GetValues<DayOfWeek>()
                .Select(day => new StationOperatingSchedule
                {
                    DayOfWeek = day,
                    IsOpen = true,
                    OpeningTime = "00:00",
                    ClosingTime = "23:59"
                })
                .ToList()
        };
    }
}
