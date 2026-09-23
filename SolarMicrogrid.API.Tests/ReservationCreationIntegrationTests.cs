/*
 * ReservationCreationIntegrationTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Verifies reservation creation rules, idempotency, overlap checks,
 *           and last-allocation concurrency against an isolated real MongoDB.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Bson;
using MongoDB.Driver;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using SolarMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SolarMicrogrid.API.Tests;

[Collection(Component3MongoCollection.Name)]
public sealed class ReservationCreationIntegrationTests : IAsyncLifetime
{
    private readonly Component3MongoFixture _fixture;

    public ReservationCreationIntegrationTests(Component3MongoFixture fixture)
    {
        // Reuse the collection's isolated MongoDB deployment while resetting data per test.
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        // Start every fact with empty indexed collections and authoritative references.
        return _fixture.ResetAsync();
    }

    public Task DisposeAsync()
    {
        // Per-test cleanup occurs in the next reset and final fixture disposal.
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ValidFutureBookingBecomesPending()
    {
        // Exercise the production transaction and assert both reservation and allocation state.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));

        ReservationCreationResult result = await _fixture.CreateOwnAsync(
            slot,
            "create-valid-future-001");
        EnergyReservation stored = await _fixture.LoadReservationAsync(result.Reservation.Id);
        EnergyBookingSlot storedSlot = await _fixture.LoadSlotAsync(slot.Id);

        Assert.Equal(ReservationStatus.Pending.ToString(), result.Reservation.Status);
        Assert.Equal(ReservationStatus.Pending, stored.Status);
        Assert.Equal(ReservationCapacityState.Held, stored.CapacityState);
        Assert.Contains(storedSlot.CapacityAllocations, claim => claim.ReservationId == stored.Id);
        Assert.Equal(5m, storedSlot.AvailableCapacityKwh);
    }

    [Fact]
    public async Task ExactlySevenDaysIsAccepted()
    {
        // Pin the inclusive booking-horizon boundary to the fixed server clock.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(7));

        ReservationCreationResult result = await _fixture.CreateOwnAsync(
            slot,
            "create-seven-days-001");

        Assert.Equal(ReservationStatus.Pending.ToString(), result.Reservation.Status);
    }

    [Fact]
    public async Task MoreThanSevenDaysIsRejected()
    {
        // Move one BSON-preserved millisecond beyond the inclusive horizon.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(7).AddMilliseconds(1));

        await Assert.ThrowsAsync<ConflictException>(() => _fixture.CreateOwnAsync(
            slot,
            "create-after-horizon-001"));
        Assert.Equal(0, await _fixture.Context.Reservations.CountDocumentsAsync(
            Builders<EnergyReservation>.Filter.Empty));
    }

    [Fact]
    public async Task PastSlotIsRejected()
    {
        // Prove a stored slot cannot bypass the server-clock future requirement.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddHours(-2));

        await Assert.ThrowsAsync<ConflictException>(() => _fixture.CreateOwnAsync(
            slot,
            "create-past-slot-001"));
    }

    [Fact]
    public async Task InactiveStationAndDanglingStationSlotAreRejected()
    {
        // Reject both an inactive current station and a slot whose station reference is invalid.
        await _fixture.Context.Stations.UpdateOneAsync(
            item => item.Id == _fixture.StationOneId,
            Builders<SolarStationInfo>.Update.Set(item => item.IsActive, false));
        EnergyBookingSlot inactiveSlot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));

        await Assert.ThrowsAsync<ConflictException>(() => _fixture.CreateOwnAsync(
            inactiveSlot,
            "create-inactive-station-001"));

        EnergyBookingSlot danglingSlot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(2),
            stationId: ObjectId.GenerateNewId().ToString());
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.CreateOwnAsync(
            danglingSlot,
            "create-dangling-station-001"));
    }

    [Fact]
    public async Task InvalidStationSlotPairIsRejected()
    {
        // Validate the defensive reference contract even though create derives station from slot.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1),
            stationId: _fixture.StationTwoId);
        var mismatchedReservation = new EnergyReservation
        {
            Id = ObjectId.GenerateNewId().ToString(),
            ProsumerNic = Component3MongoFixture.ProsumerOneNic,
            StationId = _fixture.StationOneId,
            SlotId = slot.Id,
            ScheduledStartTimeUtc = slot.StartTimeUtc,
            ScheduledEndTimeUtc = slot.EndTimeUtc,
            RequestedEnergyKwh = 5m,
            Status = ReservationStatus.Pending
        };

        await Assert.ThrowsAsync<ConflictException>(() =>
            _fixture.Reservations.LoadAndValidateReferencesAsync(
                mismatchedReservation,
                requireActiveStation: true,
                requireBookableSlot: false,
                session: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task UnavailableCapacityIsRejected()
    {
        // Keep the slot nominally available while its atomic remaining capacity is insufficient.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1),
            totalCapacityKwh: 10m,
            availableCapacityKwh: 2m);

        await Assert.ThrowsAsync<ConflictException>(() => _fixture.CreateOwnAsync(
            slot,
            "create-no-capacity-001",
            energyKwh: 5m));
        EnergyBookingSlot storedSlot = await _fixture.LoadSlotAsync(slot.Id);
        Assert.Equal(2m, storedSlot.AvailableCapacityKwh);
        Assert.Empty(storedSlot.CapacityAllocations);
    }

    [Fact]
    public async Task DuplicateAndOverlappingActiveReservationsAreRejected()
    {
        // Persist one active booking, then try both its exact slot and an intersecting slot.
        DateTime startUtc = Component3MongoFixture.FixedNowUtc.AddDays(1);
        EnergyBookingSlot firstSlot = await _fixture.AddSlotAsync(startUtc);
        await _fixture.CreateOwnAsync(firstSlot, "create-overlap-base-001");

        await Assert.ThrowsAsync<ConflictException>(() => _fixture.CreateOwnAsync(
            firstSlot,
            "create-duplicate-slot-001"));

        EnergyBookingSlot overlappingSlot = await _fixture.AddSlotAsync(startUtc.AddMinutes(30));
        await Assert.ThrowsAsync<ConflictException>(() => _fixture.CreateOwnAsync(
            overlappingSlot,
            "create-overlapping-slot-001"));
        Assert.Equal(1, await _fixture.Context.Reservations.CountDocumentsAsync(
            Builders<EnergyReservation>.Filter.Empty));
    }

    [Fact]
    public async Task SimultaneousRequestsCannotBothTakeLastAllocation()
    {
        // Race different Prosumer guards against one real slot compare-and-swap boundary.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1),
            totalCapacityKwh: 5m);
        Task<Exception?> first = CaptureAsync(() => _fixture.CreateOwnAsync(
            slot,
            "create-last-one-001",
            5m,
            Component3MongoFixture.ProsumerOneNic));
        Task<Exception?> second = CaptureAsync(() => _fixture.CreateOwnAsync(
            slot,
            "create-last-two-001",
            5m,
            Component3MongoFixture.ProsumerTwoNic));

        Exception?[] outcomes = await Task.WhenAll(first, second);
        EnergyBookingSlot storedSlot = await _fixture.LoadSlotAsync(slot.Id);

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is ConflictException);
        Assert.Single(storedSlot.CapacityAllocations);
        Assert.Equal(0m, storedSlot.AvailableCapacityKwh);
    }

    [Fact]
    public async Task RetriedCreationDoesNotCreateDuplicate()
    {
        // Reuse the same actor-scoped idempotency key and verify identity and capacity are stable.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));

        ReservationCreationResult first = await _fixture.CreateOwnAsync(
            slot,
            "create-idempotent-001");
        ReservationCreationResult replay = await _fixture.CreateOwnAsync(
            slot,
            "create-idempotent-001");
        EnergyBookingSlot storedSlot = await _fixture.LoadSlotAsync(slot.Id);

        Assert.Equal(first.Reservation.Id, replay.Reservation.Id);
        Assert.True(replay.IdempotencyReplayed);
        Assert.Equal(1, await _fixture.Context.Reservations.CountDocumentsAsync(
            Builders<EnergyReservation>.Filter.Empty));
        Assert.Single(storedSlot.CapacityAllocations);
        Assert.Equal(5m, storedSlot.AvailableCapacityKwh);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task<ReservationCreationResult>> action)
    {
        // Return the observed exception so concurrent outcomes can be asserted together.
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
