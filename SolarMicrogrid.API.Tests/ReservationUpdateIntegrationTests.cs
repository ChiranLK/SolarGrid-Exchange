/*
 * ReservationUpdateIntegrationTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Verifies update boundaries, optimistic concurrency, conflicts, safe
 *           rescheduling, and Approved-to-Pending behavior with real MongoDB.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Driver;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Reservations;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using SolarMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SolarMicrogrid.API.Tests;

[Collection(Component3MongoCollection.Name)]
public sealed class ReservationUpdateIntegrationTests : IAsyncLifetime
{
    private readonly Component3MongoFixture _fixture;

    public ReservationUpdateIntegrationTests(Component3MongoFixture fixture)
    {
        // Reuse the collection fixture while retaining per-test database isolation.
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        // Reset persistent state and the fixed clock before each update scenario.
        return _fixture.ResetAsync();
    }

    public Task DisposeAsync()
    {
        // The fixture owns database cleanup.
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ValidUpdateMovesAllocation()
    {
        // Reschedule to a distinct valid slot and verify both allocation documents.
        EnergyBookingSlot originalSlot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        EnergyBookingSlot destinationSlot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(2));
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            originalSlot,
            "update-valid-create-001");

        ReservationUpdateResult updated = await UpdateAsync(
            created,
            destinationSlot,
            7m,
            "update-valid-move-001");
        EnergyBookingSlot storedOriginal = await _fixture.LoadSlotAsync(originalSlot.Id);
        EnergyBookingSlot storedDestination = await _fixture.LoadSlotAsync(destinationSlot.Id);

        Assert.Equal(destinationSlot.Id, updated.Reservation.SlotId);
        Assert.Equal(7m, updated.Reservation.RequestedEnergyKwh);
        Assert.Empty(storedOriginal.CapacityAllocations);
        Assert.Equal(originalSlot.TotalCapacityKwh, storedOriginal.AvailableCapacityKwh);
        Assert.Single(storedDestination.CapacityAllocations);
        Assert.Equal(3m, storedDestination.AvailableCapacityKwh);
    }

    [Fact]
    public async Task ExactlyTwelveHoursIsAccepted()
    {
        // Pin the inclusive change-notice boundary to the controllable clock.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddHours(12));
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            slot,
            "update-twelve-create-001");

        ReservationUpdateResult updated = await UpdateAsync(
            created,
            slot,
            6m,
            "update-twelve-hours-001");

        Assert.Equal(6m, updated.Reservation.RequestedEnergyKwh);
        Assert.Equal(2, updated.Reservation.Version);
    }

    [Fact]
    public async Task LessThanTwelveHoursIsRejected()
    {
        // Move one BSON-preserved millisecond inside the prohibited notice interval.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddHours(12).AddMilliseconds(-1));
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            slot,
            "update-under-twelve-create-001");

        await Assert.ThrowsAsync<ConflictException>(() => UpdateAsync(
            created,
            slot,
            6m,
            "update-under-twelve-001"));
    }

    [Fact]
    public async Task FinalStatusIsRejected()
    {
        // Cancel the reservation first, then prove a final record cannot be edited.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            slot,
            "update-final-create-001");
        await _fixture.Reservations.CancelReservationAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer.ToString(),
            created.Reservation.Id,
            new CancelReservationRequestDto { ExpectedVersion = created.Reservation.Version },
            "update-final-cancel-001",
            CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => UpdateAsync(
            created,
            slot,
            6m,
            "update-final-attempt-001"));
    }

    [Fact]
    public async Task StaleVersionIsRejected()
    {
        // Submit an otherwise valid material update with an obsolete expected version.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            slot,
            "update-stale-create-001");

        await Assert.ThrowsAsync<ConflictException>(() =>
            _fixture.Reservations.UpdateReservationAsync(
                Component3MongoFixture.ProsumerOneNic,
                UserRole.Prosumer.ToString(),
                created.Reservation.Id,
                new UpdateReservationRequestDto
                {
                    SlotId = slot.Id,
                    RequestedEnergyKwh = 6m,
                    ExpectedVersion = 0
                },
                "update-stale-version-001",
                CancellationToken.None));
    }

    [Fact]
    public async Task DestinationConflictIsRejected()
    {
        // Create two non-overlapping bookings, then move the first into the second interval.
        EnergyBookingSlot firstSlot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        EnergyBookingSlot existingSlot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(2));
        EnergyBookingSlot conflictingDestination = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(2).AddMinutes(30));
        ReservationCreationResult first = await _fixture.CreateOwnAsync(
            firstSlot,
            "update-conflict-first-001");
        await _fixture.CreateOwnAsync(existingSlot, "update-conflict-second-001");

        await Assert.ThrowsAsync<ConflictException>(() => UpdateAsync(
            first,
            conflictingDestination,
            5m,
            "update-destination-conflict-001"));
    }

    [Fact]
    public async Task FailedReschedulePreservesOriginalBookingAndAllocation()
    {
        // Force a destination capacity failure and verify transaction rollback is complete.
        EnergyBookingSlot originalSlot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        EnergyBookingSlot destinationSlot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(2),
            totalCapacityKwh: 10m,
            availableCapacityKwh: 1m);
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            originalSlot,
            "update-rollback-create-001");

        await Assert.ThrowsAsync<ConflictException>(() => UpdateAsync(
            created,
            destinationSlot,
            5m,
            "update-rollback-move-001"));
        EnergyReservation stored = await _fixture.LoadReservationAsync(created.Reservation.Id);
        EnergyBookingSlot storedOriginal = await _fixture.LoadSlotAsync(originalSlot.Id);
        EnergyBookingSlot storedDestination = await _fixture.LoadSlotAsync(destinationSlot.Id);

        Assert.Equal(originalSlot.Id, stored.SlotId);
        Assert.Equal(ReservationStatus.Pending, stored.Status);
        Assert.Single(storedOriginal.CapacityAllocations);
        Assert.Equal(5m, storedOriginal.AvailableCapacityKwh);
        Assert.Empty(storedDestination.CapacityAllocations);
        Assert.Equal(1m, storedDestination.AvailableCapacityKwh);
    }

    [Fact]
    public async Task MaterialApprovedUpdateReturnsToPendingAndInvalidatesQr()
    {
        // Approve, materially change energy, and assert approval and QR eligibility are reset.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            slot,
            "update-approved-create-001");
        ReservationApprovalResult approved = await _fixture.Reservations.ApproveReservationAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice.ToString(),
            created.Reservation.Id,
            new ApproveReservationRequestDto { ExpectedVersion = created.Reservation.Version },
            "update-approved-approve-001",
            CancellationToken.None);

        ReservationUpdateResult updated = await _fixture.Reservations.UpdateReservationAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer.ToString(),
            created.Reservation.Id,
            new UpdateReservationRequestDto
            {
                SlotId = slot.Id,
                RequestedEnergyKwh = 6m,
                ExpectedVersion = approved.Reservation.Version
            },
            "update-approved-material-001",
            CancellationToken.None);
        EnergyReservation stored = await _fixture.LoadReservationAsync(created.Reservation.Id);

        Assert.Equal(ReservationStatus.Pending.ToString(), updated.Reservation.Status);
        Assert.False(updated.Reservation.QrEligible);
        Assert.Null(stored.ApprovedAtUtc);
        Assert.Null(stored.ApprovedByActorNic);
        Assert.Equal(3, stored.Version);
    }

    private Task<ReservationUpdateResult> UpdateAsync(
        ReservationCreationResult created,
        EnergyBookingSlot destinationSlot,
        decimal energyKwh,
        string idempotencyKey)
    {
        // Invoke the owner update workflow with the current created version.
        return _fixture.Reservations.UpdateReservationAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer.ToString(),
            created.Reservation.Id,
            new UpdateReservationRequestDto
            {
                SlotId = destinationSlot.Id,
                RequestedEnergyKwh = energyKwh,
                ExpectedVersion = created.Reservation.Version
            },
            idempotencyKey,
            CancellationToken.None);
    }
}
