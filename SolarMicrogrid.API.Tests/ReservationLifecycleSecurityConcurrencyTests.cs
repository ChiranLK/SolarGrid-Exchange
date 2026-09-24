/*
 * ReservationLifecycleSecurityConcurrencyTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Verifies final transitions, exact-once capacity release, object-level
 *           authorization, target validation, and competing lifecycle mutations.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Bson;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Reservations;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using SolarMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SolarMicrogrid.API.Tests;

[Collection(Component3MongoCollection.Name)]
public sealed class ReservationLifecycleSecurityConcurrencyTests : IAsyncLifetime
{
    private readonly Component3MongoFixture _fixture;

    public ReservationLifecycleSecurityConcurrencyTests(Component3MongoFixture fixture)
    {
        // Share one real replica set while isolating database state before every fact.
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        // Reset the unique database and fixed server clock for deterministic transitions.
        return _fixture.ResetAsync();
    }

    public Task DisposeAsync()
    {
        // The collection fixture owns database cleanup.
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ValidCancellationReleasesCapacityExactlyOnce()
    {
        // Cancel once, replay the same idempotency key, and inspect the real slot ledger.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            slot,
            "cancel-create-001");

        ReservationCancellationResult cancelled = await CancelAsync(
            created.Reservation.Id,
            created.Reservation.Version,
            "cancel-once-001");
        ReservationCancellationResult replay = await CancelAsync(
            created.Reservation.Id,
            created.Reservation.Version,
            "cancel-once-001");
        EnergyBookingSlot storedSlot = await _fixture.LoadSlotAsync(slot.Id);

        Assert.Equal(ReservationStatus.Cancelled.ToString(), cancelled.Reservation.Status);
        Assert.True(replay.IdempotencyReplayed);
        Assert.Equal(slot.TotalCapacityKwh, storedSlot.AvailableCapacityKwh);
        Assert.Empty(storedSlot.CapacityAllocations);
    }

    [Fact]
    public async Task RejectionReleasesCapacityExactlyOnce()
    {
        // Reject a Pending reservation and replay the decision without double restoration.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            slot,
            "reject-create-001");

        ReservationRejectionResult rejected = await RejectAsync(
            created.Reservation.Id,
            created.Reservation.Version,
            "reject-once-001");
        ReservationRejectionResult replay = await RejectAsync(
            created.Reservation.Id,
            created.Reservation.Version,
            "reject-once-001");
        EnergyBookingSlot storedSlot = await _fixture.LoadSlotAsync(slot.Id);

        Assert.Equal(ReservationStatus.Rejected.ToString(), rejected.Reservation.Status);
        Assert.True(replay.IdempotencyReplayed);
        Assert.Equal(slot.TotalCapacityKwh, storedSlot.AvailableCapacityKwh);
        Assert.Empty(storedSlot.CapacityAllocations);
    }

    [Fact]
    public async Task CancelledAndRejectedReservationsCannotBeApprovedOrCompleted()
    {
        // Exercise both final states against approval and the documented completion CAS.
        EnergyBookingSlot cancelledSlot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        EnergyBookingSlot rejectedSlot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(2));
        ReservationCreationResult cancelledCreation = await _fixture.CreateOwnAsync(
            cancelledSlot,
            "final-cancel-create-001");
        ReservationCreationResult rejectedCreation = await _fixture.CreateOwnAsync(
            rejectedSlot,
            "final-reject-create-001");
        ReservationCancellationResult cancelled = await CancelAsync(
            cancelledCreation.Reservation.Id,
            cancelledCreation.Reservation.Version,
            "final-cancel-001");
        ReservationRejectionResult rejected = await RejectAsync(
            rejectedCreation.Reservation.Id,
            rejectedCreation.Reservation.Version,
            "final-reject-001");

        await Assert.ThrowsAsync<ConflictException>(() => ApproveAsync(
            cancelled.Reservation.Id,
            cancelled.Reservation.Version,
            "final-cancel-approve-001"));
        await Assert.ThrowsAsync<ConflictException>(() => ApproveAsync(
            rejected.Reservation.Id,
            rejected.Reservation.Version,
            "final-reject-approve-001"));
        Assert.False(await _fixture.TryCompleteWithCompareAndSwapAsync(
            cancelled.Reservation.Id,
            cancelled.Reservation.Version,
            "verification-cancelled"));
        Assert.False(await _fixture.TryCompleteWithCompareAndSwapAsync(
            rejected.Reservation.Id,
            rejected.Reservation.Version,
            "verification-rejected"));
    }

    [Fact]
    public async Task OneProsumerCannotReadOrMutateAnotherBooking()
    {
        // Use another active Prosumer's valid claims against direct read and cancellation.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            slot,
            "security-owner-create-001");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.Reservations.GetReservationByIdAsync(
                Component3MongoFixture.ProsumerTwoNic,
                UserRole.Prosumer.ToString(),
                created.Reservation.Id,
                CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.Reservations.CancelReservationAsync(
                Component3MongoFixture.ProsumerTwoNic,
                UserRole.Prosumer.ToString(),
                created.Reservation.Id,
                new CancelReservationRequestDto
                {
                    ExpectedVersion = created.Reservation.Version
                },
                "security-cross-owner-cancel-001",
                CancellationToken.None));
        Assert.Equal(ReservationStatus.Pending,
            (await _fixture.LoadReservationAsync(created.Reservation.Id)).Status);
    }

    [Fact]
    public async Task UnauthorizedRolesCannotApproveOrReject()
    {
        // Prove role checks use current stored users rather than request-selected authority.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            slot,
            "security-decision-create-001");

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _fixture.Reservations.ApproveReservationAsync(
                Component3MongoFixture.ProsumerOneNic,
                UserRole.Prosumer.ToString(),
                created.Reservation.Id,
                new ApproveReservationRequestDto
                {
                    ExpectedVersion = created.Reservation.Version
                },
                "security-prosumer-approve-001",
                CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _fixture.Reservations.RejectReservationAsync(
                Component3MongoFixture.GridOperatorNic,
                UserRole.GridOperator.ToString(),
                created.Reservation.Id,
                new RejectReservationRequestDto
                {
                    ExpectedVersion = created.Reservation.Version,
                    Reason = "Grid Operators cannot reject."
                },
                "security-grid-reject-001",
                CancellationToken.None));
    }

    [Fact]
    public async Task StaffCreationValidatesTargetProsumer()
    {
        // Reject a missing target and a deactivated target before any reservation is persisted.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));

        await Assert.ThrowsAsync<NotFoundException>(() => CreateForTargetAsync(
            slot,
            "200312345678",
            "staff-target-missing-001"));
        await Assert.ThrowsAsync<ConflictException>(() => CreateForTargetAsync(
            slot,
            Component3MongoFixture.InactiveProsumerNic,
            "staff-target-inactive-001"));
    }

    [Fact]
    public async Task StaffCreatedReservationAppearsInTargetProsumerList()
    {
        // Create as Backoffice, then read through the same owner-scoped API projection Android uses.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        ReservationCreationResult created = await CreateForTargetAsync(
            slot,
            Component3MongoFixture.ProsumerOneNic,
            "staff-target-owner-list-001");

        PagedReservationResponseDto ownerPage =
            await _fixture.Reservations.GetReservationsAsync(
                Component3MongoFixture.ProsumerOneNic,
                UserRole.Prosumer.ToString(),
                new ReservationListQueryDto(),
                CancellationToken.None);

        Assert.Contains(ownerPage.Items, item => item.Id == created.Reservation.Id);
        Assert.All(ownerPage.Items, item =>
            Assert.Equal(Component3MongoFixture.ProsumerOneNic, item.ProsumerNic));
    }

    [Fact]
    public async Task ConcurrentUpdateAndCancelHaveOneWinner()
    {
        // Race two production workflows sharing the MongoDB scheduling lease and version CAS.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            slot,
            "race-update-cancel-create-001");
        Task<Exception?> update = CaptureAsync(async () =>
        {
            await _fixture.Reservations.UpdateReservationAsync(
                Component3MongoFixture.ProsumerOneNic,
                UserRole.Prosumer.ToString(),
                created.Reservation.Id,
                new UpdateReservationRequestDto
                {
                    SlotId = slot.Id,
                    RequestedEnergyKwh = 6m,
                    ExpectedVersion = created.Reservation.Version
                },
                "race-update-001",
                CancellationToken.None);
        });
        Task<Exception?> cancel = CaptureAsync(async () =>
        {
            await CancelAsync(
                created.Reservation.Id,
                created.Reservation.Version,
                "race-cancel-001");
        });

        Exception?[] outcomes = await Task.WhenAll(update, cancel);
        EnergyReservation stored = await _fixture.LoadReservationAsync(created.Reservation.Id);

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is ConflictException);
        Assert.Contains(stored.Status, new[]
        {
            ReservationStatus.Pending,
            ReservationStatus.Cancelled
        });
        Assert.Equal(2, stored.Version);
    }

    [Fact]
    public async Task ConcurrentApproveAndRejectHaveOneWinner()
    {
        // Race staff decisions against the same Pending/version/held compare-and-swap.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            slot,
            "race-decisions-create-001");
        Task<Exception?> approve = CaptureAsync(async () =>
        {
            await ApproveAsync(
                created.Reservation.Id,
                created.Reservation.Version,
                "race-approve-001");
        });
        Task<Exception?> reject = CaptureAsync(async () =>
        {
            await RejectAsync(
                created.Reservation.Id,
                created.Reservation.Version,
                "race-reject-001");
        });

        Exception?[] outcomes = await Task.WhenAll(approve, reject);
        EnergyReservation stored = await _fixture.LoadReservationAsync(created.Reservation.Id);

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is ConflictException);
        Assert.Contains(stored.Status, new[]
        {
            ReservationStatus.Approved,
            ReservationStatus.Rejected
        });
        Assert.Equal(2, stored.Version);
    }

    [Fact]
    public async Task ConcurrentCancelAndCompletionHaveOneWinner()
    {
        // Race cancellation with Member 4's documented completion CAS on the same version.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            slot,
            "race-completion-create-001");
        ReservationApprovalResult approved = await ApproveAsync(
            created.Reservation.Id,
            created.Reservation.Version,
            "race-completion-approve-001");
        Task<Exception?> cancel = CaptureAsync(async () =>
        {
            await CancelAsync(
                approved.Reservation.Id,
                approved.Reservation.Version,
                "race-completion-cancel-001");
        });
        Task<bool> completion = _fixture.TryCompleteWithCompareAndSwapAsync(
            approved.Reservation.Id,
            approved.Reservation.Version,
            "verification-race-001");

        Exception? cancellationError = await cancel;
        bool completionWon = await completion;
        EnergyReservation stored = await _fixture.LoadReservationAsync(created.Reservation.Id);

        Assert.True((cancellationError is null) ^ completionWon);
        if (completionWon)
        {
            Assert.IsType<ConflictException>(cancellationError);
            Assert.Equal(ReservationStatus.Completed, stored.Status);
            Assert.Equal(ReservationCapacityState.Consumed, stored.CapacityState);
        }
        else
        {
            Assert.Null(cancellationError);
            Assert.Equal(ReservationStatus.Cancelled, stored.Status);
            Assert.Equal(ReservationCapacityState.Released, stored.CapacityState);
        }
    }

    private Task<ReservationCancellationResult> CancelAsync(
        string reservationId,
        long expectedVersion,
        string idempotencyKey)
    {
        // Invoke owner cancellation with an explicit optimistic version.
        return _fixture.Reservations.CancelReservationAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer.ToString(),
            reservationId,
            new CancelReservationRequestDto { ExpectedVersion = expectedVersion },
            idempotencyKey,
            CancellationToken.None);
    }

    private Task<ReservationApprovalResult> ApproveAsync(
        string reservationId,
        long expectedVersion,
        string idempotencyKey)
    {
        // Invoke the production Backoffice approval workflow.
        return _fixture.Reservations.ApproveReservationAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice.ToString(),
            reservationId,
            new ApproveReservationRequestDto { ExpectedVersion = expectedVersion },
            idempotencyKey,
            CancellationToken.None);
    }

    private Task<ReservationRejectionResult> RejectAsync(
        string reservationId,
        long expectedVersion,
        string idempotencyKey)
    {
        // Invoke the production Backoffice rejection workflow with a bounded audit reason.
        return _fixture.Reservations.RejectReservationAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice.ToString(),
            reservationId,
            new RejectReservationRequestDto
            {
                ExpectedVersion = expectedVersion,
                Reason = "Integration test rejection."
            },
            idempotencyKey,
            CancellationToken.None);
    }

    private Task<ReservationCreationResult> CreateForTargetAsync(
        EnergyBookingSlot slot,
        string targetProsumerNic,
        string idempotencyKey)
    {
        // Invoke staff creation using current Backoffice identity and an explicit target.
        return _fixture.Reservations.CreateReservationForProsumerAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice.ToString(),
            new StaffCreateReservationRequestDto
            {
                TargetProsumerNic = targetProsumerNic,
                SlotId = slot.Id,
                RequestedEnergyKwh = 5m
            },
            idempotencyKey,
            CancellationToken.None);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        // Capture concurrent success/failure without allowing WhenAll to hide the other outcome.
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
