/*
 * CrossMemberIntegrityIntegrationTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Real-MongoDB checks for the Component 3 integration guards with
 *           Member 2 stations and slots: the station reservation-write version
 *           that closes the create-vs-deactivate race, legacy station documents
 *           without that field, and slot delete/time-change guards that keep
 *           active reservations valid.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Bson;
using MongoDB.Driver;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Slots;
using SolarMicrogrid.API.Models.DTOs.Stations;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using SolarMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SolarMicrogrid.API.Tests;

[Collection(Component3MongoCollection.Name)]
public sealed class CrossMemberIntegrityIntegrationTests : IAsyncLifetime
{
    private readonly Component3MongoFixture _fixture;

    public CrossMemberIntegrityIntegrationTests(Component3MongoFixture fixture)
    {
        // Share the isolated database and production services with the other Component 3 suites.
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Start every test from the seeded users/stations. Member 2's SlotService reads the real
        // clock, so align the Component 3 clock with it to keep both services consistent.
        await _fixture.ResetAsync();
        _fixture.Clock.SetUtcNow(DateTime.SpecifyKind(
            DateTime.UtcNow.Date.AddHours(1),
            DateTimeKind.Utc));
    }

    public Task DisposeAsync()
    {
        // Documents are cleared by the next ResetAsync and the database by the fixture.
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreatingReservation_BumpsTheStationReservationWriteVersion()
    {
        // Proves the create transaction writes the station, which deactivation then compares.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(SlotStart(days: 2));
        long before = (await LoadStationAsync(_fixture.StationOneId)).ReservationWriteVersion;

        await _fixture.CreateOwnAsync(slot, "integrity-bump-001");

        long after = (await LoadStationAsync(_fixture.StationOneId)).ReservationWriteVersion;
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task ReservationForDeactivatedStation_IsRejectedByTheConditionalStationWrite()
    {
        // Proves a station deactivated before the create commits cannot receive a new hold.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(SlotStart(days: 2));
        await StationService().DeactivateStationAsync(_fixture.StationOneId, CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(
            () => _fixture.CreateOwnAsync(slot, "integrity-inactive-001"));

        Assert.Equal(0, await CountActiveReservationsAsync(_fixture.StationOneId));
        Assert.Empty((await _fixture.LoadSlotAsync(slot.Id)).CapacityAllocations);
    }

    [Fact]
    public async Task DeactivationThatMissedAnUncommittedReservation_FailsInsteadOfWinning()
    {
        // Forces the dangerous interleaving deterministically: a reservation transaction has written
        // the station counter and inserted a Pending reservation but not committed. Deactivation's
        // reservation check cannot see it, and its station write blocks behind the transaction.
        // After the commit, the deactivation compare-and-swap must fail with 409.
        string stationId = ObjectId.GenerateNewId().ToString();
        await InsertStationAsync(stationId);

        using IClientSessionHandle session = await _fixture.Client.StartSessionAsync();
        session.StartTransaction();
        await _fixture.Context.Stations.UpdateOneAsync(
            session,
            Builders<SolarStationInfo>.Filter.And(
                Builders<SolarStationInfo>.Filter.Eq(item => item.Id, stationId),
                Builders<SolarStationInfo>.Filter.Eq(item => item.IsActive, true)),
            Builders<SolarStationInfo>.Update.Inc(item => item.ReservationWriteVersion, 1L));
        await _fixture.Context.Reservations.InsertOneAsync(session, PendingReservationAt(stationId));

        Task<StationResponseDto?> deactivate =
            StationService().DeactivateStationAsync(stationId, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(750));
        Assert.False(deactivate.IsCompleted, "Deactivation should be blocked behind the open transaction.");
        await session.CommitTransactionAsync();

        await Assert.ThrowsAsync<ConflictException>(() => deactivate);
        Assert.True((await LoadStationAsync(stationId)).IsActive);
        Assert.Equal(1, await CountActiveReservationsAsync(stationId));
    }

    [Fact]
    public async Task ConcurrentCreateAndDeactivate_NeverLeaveAHoldAtAnInactiveStation()
    {
        // Smoke test with the real workflows racing: exactly one may win, never both.
        const int rounds = 12;
        for (int round = 0; round < rounds; round++)
        {
            string stationId = ObjectId.GenerateNewId().ToString();
            await InsertStationAsync(stationId);
            EnergyBookingSlot slot = await _fixture.AddSlotAsync(
                SlotStart(days: 2).AddHours(round % 10),
                stationId: stationId);

            Task create = _fixture.CreateOwnAsync(slot, $"integrity-race-{round:D3}");
            Task deactivate = StationService().DeactivateStationAsync(stationId, CancellationToken.None);
            await IgnoreConflictAsync(create);
            await IgnoreConflictAsync(deactivate);

            SolarStationInfo station = await LoadStationAsync(stationId);
            long active = await CountActiveReservationsAsync(stationId);
            Assert.False(
                !station.IsActive && active > 0,
                $"Round {round}: station was deactivated while {active} active reservation(s) exist.");
        }
    }

    [Fact]
    public async Task StationWithoutTheNewField_CanStillBeEditedAndDeactivated()
    {
        // Proves stations stored before reservation_write_version existed keep working.
        await _fixture.Context.Stations.UpdateOneAsync(
            Builders<SolarStationInfo>.Filter.Eq(item => item.Id, _fixture.StationTwoId),
            Builders<SolarStationInfo>.Update.Unset("reservation_write_version"));

        await StationService().UpdateStationAsync(_fixture.StationTwoId, ValidStationUpdate(), CancellationToken.None);
        await _fixture.Context.Stations.UpdateOneAsync(
            Builders<SolarStationInfo>.Filter.Eq(item => item.Id, _fixture.StationTwoId),
            Builders<SolarStationInfo>.Update.Unset("reservation_write_version"));
        var result = await StationService().DeactivateStationAsync(_fixture.StationTwoId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task SlotWithACapacityClaim_CannotBeDeleted_EvenIfTheReservationCheckMissedIt()
    {
        // Simulates the race: the claim exists but no reservation document is visible yet.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(SlotStart(days: 2), availableCapacityKwh: 7m);
        await AddRawClaimAsync(slot.Id, energyKwh: 3m);

        await Assert.ThrowsAsync<ConflictException>(
            () => new SlotService(_fixture.Context).DeleteSlotAsync(slot.Id, CancellationToken.None));

        Assert.True(await _fixture.Context.Slots.Find(item => item.Id == slot.Id).AnyAsync());
    }

    [Fact]
    public async Task SlotWithoutClaims_CanStillBeDeleted()
    {
        // Proves the new delete filter matches slots with an empty or missing claims array.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(SlotStart(days: 2));
        await _fixture.Context.Slots.UpdateOneAsync(
            Builders<EnergyBookingSlot>.Filter.Eq(item => item.Id, slot.Id),
            Builders<EnergyBookingSlot>.Update.Unset("capacity_allocations"));

        Assert.True(await new SlotService(_fixture.Context).DeleteSlotAsync(slot.Id, CancellationToken.None));
    }

    [Fact]
    public async Task HeldSlot_TimesCannotMove_ButCapacityCanStillChange()
    {
        // Proves reservations keep valid scheduled times while capacity edits remain possible.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(SlotStart(days: 2));
        await _fixture.CreateOwnAsync(slot, "integrity-slot-time-001", energyKwh: 4m);
        var slots = new SlotService(_fixture.Context);

        await Assert.ThrowsAsync<ConflictException>(() => slots.UpdateSlotAsync(
            slot.Id,
            new UpdateSlotRequestDto
            {
                StartTimeUtc = slot.StartTimeUtc.AddHours(2),
                EndTimeUtc = slot.EndTimeUtc.AddHours(2),
                TotalCapacityKwh = slot.TotalCapacityKwh
            },
            CancellationToken.None));

        SlotResponseDto? capacityOnly = await slots.UpdateSlotAsync(
            slot.Id,
            new UpdateSlotRequestDto
            {
                StartTimeUtc = slot.StartTimeUtc,
                EndTimeUtc = slot.EndTimeUtc,
                TotalCapacityKwh = 12m
            },
            CancellationToken.None);

        EnergyBookingSlot stored = await _fixture.LoadSlotAsync(slot.Id);
        Assert.NotNull(capacityOnly);
        Assert.Equal(slot.StartTimeUtc, stored.StartTimeUtc);
        Assert.Equal(12m, stored.TotalCapacityKwh);
        Assert.Equal(8m, stored.AvailableCapacityKwh);
    }

    [Fact]
    public async Task UnheldSlot_TimesCanStillMove()
    {
        // Proves the time guard only applies while reservations hold capacity.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(SlotStart(days: 2));

        SlotResponseDto? moved = await new SlotService(_fixture.Context).UpdateSlotAsync(
            slot.Id,
            new UpdateSlotRequestDto
            {
                StartTimeUtc = slot.StartTimeUtc.AddHours(2),
                EndTimeUtc = slot.EndTimeUtc.AddHours(2),
                TotalCapacityKwh = slot.TotalCapacityKwh
            },
            CancellationToken.None);

        Assert.NotNull(moved);
        Assert.Equal(slot.StartTimeUtc.AddHours(2), (await _fixture.LoadSlotAsync(slot.Id)).StartTimeUtc);
    }

    private StationService StationService()
    {
        // Member 2's production station service over the same isolated database.
        return new StationService(_fixture.Context, new ReservationGuardService(_fixture.Context));
    }

    private DateTime SlotStart(int days)
    {
        // A future slot at 04:00 UTC (09:30 Colombo), inside the fixture's open schedule and horizon.
        DateTime now = _fixture.Clock.GetUtcNow().UtcDateTime;
        return DateTime.SpecifyKind(now.Date.AddDays(days).AddHours(4), DateTimeKind.Utc);
    }

    private async Task<SolarStationInfo> LoadStationAsync(string stationId)
    {
        // Read the persisted station document including the reservation-write counter.
        return await _fixture.Context.Stations.Find(item => item.Id == stationId).SingleAsync();
    }

    private async Task<long> CountActiveReservationsAsync(string stationId)
    {
        // Count reservations that hold or await capacity at the station (Member 2's guard definition).
        return await _fixture.Context.Reservations.CountDocumentsAsync(
            Builders<EnergyReservation>.Filter.And(
                Builders<EnergyReservation>.Filter.Eq(item => item.StationId, stationId),
                Builders<EnergyReservation>.Filter.In(
                    item => item.Status,
                    new[] { ReservationStatus.Pending, ReservationStatus.Approved })));
    }

    private async Task InsertStationAsync(string stationId)
    {
        // Copy the seeded station's schedule so the fresh station accepts the same slot times.
        SolarStationInfo template = await LoadStationAsync(_fixture.StationOneId);
        template.Id = stationId;
        template.ReservationWriteVersion = 0;
        await _fixture.Context.Stations.InsertOneAsync(template);
    }

    private async Task AddRawClaimAsync(string slotId, decimal energyKwh)
    {
        // Persist a capacity claim directly to model a hold whose reservation is not yet visible.
        await _fixture.Context.Slots.UpdateOneAsync(
            Builders<EnergyBookingSlot>.Filter.Eq(item => item.Id, slotId),
            Builders<EnergyBookingSlot>.Update.Push(
                item => item.CapacityAllocations,
                new SlotCapacityAllocation
                {
                    ReservationId = ObjectId.GenerateNewId().ToString(),
                    EnergyKwh = energyKwh,
                    ReservationVersion = 1
                }));
    }

    private EnergyReservation PendingReservationAt(string stationId)
    {
        // A minimal Pending reservation document, as the create transaction would insert it.
        DateTime start = SlotStart(days: 2);
        return new EnergyReservation
        {
            Id = ObjectId.GenerateNewId().ToString(),
            ProsumerNic = Component3MongoFixture.ProsumerOneNic,
            StationId = stationId,
            SlotId = ObjectId.GenerateNewId().ToString(),
            Status = ReservationStatus.Pending,
            CapacityState = ReservationCapacityState.HoldPending,
            RequestedEnergyKwh = 1m,
            ScheduledStartTimeUtc = start,
            ScheduledEndTimeUtc = start.AddHours(1),
            Version = 1,
            CreatedAtUtc = _fixture.Clock.GetUtcNow().UtcDateTime,
            UpdatedAtUtc = _fixture.Clock.GetUtcNow().UtcDateTime
        };
    }

    private static UpdateStationRequestDto ValidStationUpdate()
    {
        // A valid Member 2 edit that keeps an all-day schedule.
        return new UpdateStationRequestDto
        {
            Name = "Edited legacy station",
            Address = "Integration test address",
            Latitude = 6.9271,
            Longitude = 79.8612,
            EnergyGenerationCapacityKw = 100m,
            BatteryStorageCapacityKwh = 200m,
            OperatingSchedule = Enum.GetValues<DayOfWeek>()
                .Select(day => new OperatingScheduleDto
                {
                    DayOfWeek = day.ToString(),
                    IsOpen = true,
                    OpeningTime = "00:00",
                    ClosingTime = "23:59"
                })
                .ToList()
        };
    }

    private static async Task IgnoreConflictAsync(Task task)
    {
        // Either side of the race may legitimately lose with a 409.
        try
        {
            await task;
        }
        catch (ConflictException)
        {
        }
    }
}
