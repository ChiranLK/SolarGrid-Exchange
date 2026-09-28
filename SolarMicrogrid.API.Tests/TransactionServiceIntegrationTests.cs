/*
 * TransactionServiceIntegrationTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Verifies secure QR issue/verify/complete authorization, expiry,
 *           hashed persistence, replay rejection, and one-winner concurrency.
 * Infrastructure: Reuses the real replica-set fixture and deterministic clock.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Driver;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Dashboard;
using SolarMicrogrid.API.Models.DTOs.Reservations;
using SolarMicrogrid.API.Models.DTOs.Transactions;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using SolarMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SolarMicrogrid.API.Tests;

[Collection(Component3MongoCollection.Name)]
public sealed class TransactionServiceIntegrationTests : IAsyncLifetime
{
    private readonly Component3MongoFixture _fixture;

    public TransactionServiceIntegrationTests(Component3MongoFixture fixture)
    {
        // Reuse the isolated MongoDB database and production transaction service.
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Reset all shared records before each security or concurrency scenario.
        await _fixture.ResetAsync();
    }

    public Task DisposeAsync()
    {
        // Leave collection lifecycle cleanup to the shared fixture.
        return Task.CompletedTask;
    }

    [Fact]
    public async Task IssueRequiresActiveOwnerAndPersistsOnlyHashes()
    {
        // Prove owner-only issuance and absence of PII/reservation data in the QR bearer value.
        ReservationResponseDto approved = await CreateApprovedReservationAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Transactions.IssueAsync(
            Component3MongoFixture.ProsumerTwoNic,
            UserRole.Prosumer.ToString(),
            approved.Id,
            CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Transactions.IssueAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice.ToString(),
            approved.Id,
            CancellationToken.None));

        IssueQrTransactionResponseDto issued = await IssueAsync(approved.Id);
        QrTransaction stored = await _fixture.Context.QrTransactions
            .Find(item => item.ReservationId == approved.Id)
            .SingleAsync();

        Assert.Equal(43, issued.QrToken.Length);
        Assert.DoesNotContain(Component3MongoFixture.ProsumerOneNic, issued.QrToken);
        Assert.DoesNotContain(approved.Id, issued.QrToken, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(approved.StationId, issued.QrToken, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(issued.QrToken, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.Equal(64, stored.OwnerReferenceHash.Length);
        Assert.DoesNotContain(Component3MongoFixture.ProsumerOneNic, stored.OwnerReferenceHash);
    }

    [Fact]
    public async Task VerifyRejectsInvalidExpiredWrongRoleAndWrongStationTokens()
    {
        // Exercise safe invalid/expiry/role/station outcomes without accepting client station input.
        ReservationResponseDto approved = await CreateApprovedReservationAsync();
        IssueQrTransactionResponseDto issued = await IssueAsync(approved.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Transactions.VerifyAsync(
            Component3MongoFixture.GridOperatorNic,
            UserRole.GridOperator.ToString(),
            new VerifyQrTransactionRequestDto { Token = new string('A', 43) },
            CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Transactions.VerifyAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer.ToString(),
            new VerifyQrTransactionRequestDto { Token = issued.QrToken },
            CancellationToken.None));

        _fixture.Clock.SetUtcNow(Component3MongoFixture.FixedNowUtc.AddMinutes(6));
        await Assert.ThrowsAsync<ConflictException>(() => VerifyAsync(issued.QrToken));
        QrTransaction expired = await _fixture.Context.QrTransactions
            .Find(item => item.ReservationId == approved.Id)
            .SingleAsync();
        Assert.Equal(QrTransactionState.Expired, expired.State);

        await _fixture.ResetAsync();
        ReservationResponseDto otherStation = await CreateApprovedReservationAsync(
            _fixture.StationTwoId);
        IssueQrTransactionResponseDto otherStationIssue = await IssueAsync(otherStation.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => VerifyAsync(otherStationIssue.QrToken));
    }

    [Fact]
    public async Task VerifyRejectsReservationThatChangedAfterIssue()
    {
        // Invalidate an issued token when the authoritative reservation is no longer Approved.
        ReservationResponseDto approved = await CreateApprovedReservationAsync();
        IssueQrTransactionResponseDto issued = await IssueAsync(approved.Id);
        await _fixture.Context.Reservations.UpdateOneAsync(
            item => item.Id == approved.Id,
            Builders<EnergyReservation>.Update
                .Set(item => item.Status, ReservationStatus.Cancelled)
                .Set(item => item.Version, approved.Version + 1));

        await Assert.ThrowsAsync<ConflictException>(() => VerifyAsync(issued.QrToken));
        QrTransaction revoked = await _fixture.Context.QrTransactions
            .Find(item => item.ReservationId == approved.Id)
            .SingleAsync();
        Assert.Equal(QrTransactionState.Revoked, revoked.State);
    }

    [Fact]
    public async Task ApprovalGatesQrAndCancellationRejectionOrScheduleExpiryInvalidateIt()
    {
        // Cross the real Member 3 lifecycle boundary and keep every QR decision server-authoritative.
        ReservationResponseDto pending = await CreatePendingReservationAsync();
        await Assert.ThrowsAsync<ConflictException>(() => IssueAsync(pending.Id));

        ReservationApprovalResult approval = await _fixture.Reservations.ApproveReservationAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice.ToString(),
            pending.Id,
            new ApproveReservationRequestDto { ExpectedVersion = pending.Version },
            $"integration-approve-{Guid.NewGuid():N}",
            CancellationToken.None);
        IssueQrTransactionResponseDto issued = await IssueAsync(approval.Reservation.Id);
        ReservationCancellationResult cancellation =
            await _fixture.Reservations.CancelReservationAsync(
                Component3MongoFixture.ProsumerOneNic,
                UserRole.Prosumer.ToString(),
                approval.Reservation.Id,
                new CancelReservationRequestDto
                {
                    ExpectedVersion = approval.Reservation.Version,
                    Reason = "Integration cancellation."
                },
                $"integration-cancel-{Guid.NewGuid():N}",
                CancellationToken.None);

        Assert.Equal(ReservationStatus.Cancelled.ToString(), cancellation.Reservation.Status);
        await Assert.ThrowsAsync<ConflictException>(() => VerifyAsync(issued.QrToken));
        QrTransaction revoked = await _fixture.Context.QrTransactions
            .Find(item => item.ReservationId == approval.Reservation.Id)
            .SingleAsync();
        Assert.Equal(QrTransactionState.Revoked, revoked.State);

        await _fixture.ResetAsync();
        pending = await CreatePendingReservationAsync();
        ReservationRejectionResult rejection =
            await _fixture.Reservations.RejectReservationAsync(
                Component3MongoFixture.BackofficeNic,
                UserRole.Backoffice.ToString(),
                pending.Id,
                new RejectReservationRequestDto
                {
                    ExpectedVersion = pending.Version,
                    Reason = "Integration rejection."
                },
                $"integration-reject-{Guid.NewGuid():N}",
                CancellationToken.None);
        Assert.Equal(ReservationStatus.Rejected.ToString(), rejection.Reservation.Status);
        await Assert.ThrowsAsync<ConflictException>(() => IssueAsync(rejection.Reservation.Id));

        await _fixture.ResetAsync();
        ReservationResponseDto approved = await CreateApprovedReservationAsync();
        _fixture.Clock.SetUtcNow(approved.ScheduledEndTimeUtc);
        await Assert.ThrowsAsync<ConflictException>(() => IssueAsync(approved.Id));
    }

    [Fact]
    public async Task VerifyReturnsSafeConfirmationAndRejectsRepeatScan()
    {
        // Consume one QR token once and expose only bounded confirmation fields plus a new receipt.
        ReservationResponseDto approved = await CreateApprovedReservationAsync();
        IssueQrTransactionResponseDto issued = await IssueAsync(approved.Id);

        VerifyQrTransactionResponseDto verified = await VerifyAsync(issued.QrToken);
        QrTransaction stored = await _fixture.Context.QrTransactions
            .Find(item => item.ReservationId == approved.Id)
            .SingleAsync();

        Assert.Equal(approved.Id, verified.ReservationId);
        Assert.Equal(approved.Version, verified.ReservationVersion);
        Assert.Equal(ReservationStatus.Approved.ToString(), verified.Status);
        Assert.StartsWith("PRO-", verified.ProsumerReference, StringComparison.Ordinal);
        Assert.DoesNotContain(Component3MongoFixture.ProsumerOneNic, verified.ProsumerReference);
        Assert.Equal(QrTransactionState.Verified, stored.State);
        Assert.NotEqual(verified.VerificationId, stored.VerificationHash);
        Assert.Equal(64, stored.VerificationHash?.Length);
        await Assert.ThrowsAsync<ConflictException>(() => VerifyAsync(issued.QrToken));
    }

    [Fact]
    public async Task CompletionRejectsExpiredReceiptAndChangedReservation()
    {
        // Re-read receipt and reservation state so neither expiry nor post-scan changes can complete.
        ReservationResponseDto approved = await CreateApprovedReservationAsync();
        IssueQrTransactionResponseDto issued = await IssueAsync(approved.Id);
        VerifyQrTransactionResponseDto verified = await VerifyAsync(issued.QrToken);
        _fixture.Clock.SetUtcNow(Component3MongoFixture.FixedNowUtc.AddMinutes(6));

        await Assert.ThrowsAsync<ConflictException>(() => CompleteAsync(verified));

        await _fixture.ResetAsync();
        approved = await CreateApprovedReservationAsync();
        issued = await IssueAsync(approved.Id);
        verified = await VerifyAsync(issued.QrToken);
        await _fixture.Context.Reservations.UpdateOneAsync(
            item => item.Id == approved.Id,
            Builders<EnergyReservation>.Update
                .Set(item => item.Status, ReservationStatus.Cancelled)
                .Set(item => item.Version, approved.Version + 1));

        await Assert.ThrowsAsync<ConflictException>(() => CompleteAsync(verified));
    }

    [Fact]
    public async Task CompletionTransitionsReservationAndTransactionExactlyOnce()
    {
        // Complete the approved reservation, retain consumed capacity, and reject a receipt replay.
        ReservationResponseDto approved = await CreateApprovedReservationAsync();
        IssueQrTransactionResponseDto issued = await IssueAsync(approved.Id);
        VerifyQrTransactionResponseDto verified = await VerifyAsync(issued.QrToken);

        CompleteQrTransactionResponseDto completed = await CompleteAsync(verified);
        EnergyReservation storedReservation = await _fixture.LoadReservationAsync(approved.Id);
        QrTransaction storedTransaction = await _fixture.Context.QrTransactions
            .Find(item => item.ReservationId == approved.Id)
            .SingleAsync();

        Assert.Equal(ReservationStatus.Completed.ToString(), completed.Status);
        Assert.Equal(approved.Version + 1, completed.Version);
        Assert.Equal(ReservationStatus.Completed, storedReservation.Status);
        Assert.Equal(ReservationCapacityState.Consumed, storedReservation.CapacityState);
        Assert.Equal(Component3MongoFixture.GridOperatorNic, storedReservation.CompletedByActorNic);
        Assert.Equal(storedTransaction.Id, storedReservation.CompletedVerificationId);
        Assert.Equal(QrTransactionState.Completed, storedTransaction.State);
        Assert.Equal(
            ReservationStatus.Completed,
            Assert.Single(
                storedReservation.StatusHistory,
                item => item.ToStatus == ReservationStatus.Completed).ToStatus);
        await Assert.ThrowsAsync<ConflictException>(() => VerifyAsync(issued.QrToken));
        await Assert.ThrowsAsync<ConflictException>(() => CompleteAsync(verified));
    }

    [Fact]
    public async Task CompletionImmediatelyRefreshesDashboardCountsHistoryAndStationName()
    {
        // Read before and after the legal completion so no stale client-side count can mask persistence.
        var dashboard = new DashboardService(_fixture.Context, _fixture.Clock);
        ReservationResponseDto approved = await CreateApprovedReservationAsync();
        DashboardResponseDto before = await dashboard.GetDashboardAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer.ToString(),
            new DashboardQueryDto { RecentLimit = 10 },
            CancellationToken.None);

        IssueQrTransactionResponseDto issued = await IssueAsync(approved.Id);
        VerifyQrTransactionResponseDto verified = await VerifyAsync(issued.QrToken);
        await CompleteAsync(verified);

        DashboardResponseDto prosumerAfter = await dashboard.GetDashboardAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer.ToString(),
            new DashboardQueryDto { RecentLimit = 10 },
            CancellationToken.None);
        DashboardResponseDto operatorAfter = await dashboard.GetDashboardAsync(
            Component3MongoFixture.GridOperatorNic,
            UserRole.GridOperator.ToString(),
            new DashboardQueryDto { RecentLimit = 10 },
            CancellationToken.None);
        SolarStationInfo station = await _fixture.Context.Stations
            .Find(item => item.Id == approved.StationId)
            .SingleAsync();

        Assert.Equal(1, before.StatusSummary.ApprovedTotal);
        Assert.Equal(1, before.StatusSummary.ApprovedFutureCount);
        Assert.Equal(0, before.StatusSummary.CompletedTotal);
        Assert.Equal(0, prosumerAfter.StatusSummary.ApprovedTotal);
        Assert.Equal(0, prosumerAfter.StatusSummary.ApprovedFutureCount);
        Assert.Equal(1, prosumerAfter.StatusSummary.CompletedTotal);
        Assert.Equal(1, prosumerAfter.StatusSummary.HistoryCount);
        DashboardReservationSummaryDto history = Assert.Single(prosumerAfter.RecentHistory);
        Assert.Equal(ReservationStatus.Completed.ToString(), history.Status);
        Assert.Equal(station.Name, history.StationName);
        Assert.Equal(1, operatorAfter.StatusSummary.CompletedTotal);
        Assert.Equal(approved.Id, Assert.Single(operatorAfter.CompletedTransfers).ReservationId);
    }

    [Fact]
    public async Task SimultaneousCompletionAllowsExactlyOneSuccess()
    {
        // Race the same verified receipt and prove reservation/transaction CAS has one winner.
        ReservationResponseDto approved = await CreateApprovedReservationAsync();
        IssueQrTransactionResponseDto issued = await IssueAsync(approved.Id);
        VerifyQrTransactionResponseDto verified = await VerifyAsync(issued.QrToken);

        Task<(CompleteQrTransactionResponseDto? Response, Exception? Error)> first =
            CaptureCompletionAsync(verified);
        Task<(CompleteQrTransactionResponseDto? Response, Exception? Error)> second =
            CaptureCompletionAsync(verified);
        var outcomes = await Task.WhenAll(first, second);
        EnergyReservation stored = await _fixture.LoadReservationAsync(approved.Id);

        Assert.Single(outcomes, outcome => outcome.Response is not null);
        Assert.Single(outcomes, outcome => outcome.Error is ConflictException);
        Assert.Equal(ReservationStatus.Completed, stored.Status);
        Assert.Equal(approved.Version + 1, stored.Version);
        Assert.Single(
            stored.StatusHistory,
            item => item.ToStatus == ReservationStatus.Completed);
    }

    private async Task<ReservationResponseDto> CreateApprovedReservationAsync(
        string? stationId = null)
    {
        // Use Member 3 creation/approval services so QR fixtures obey real lifecycle/capacity rules.
        ReservationResponseDto pending = await CreatePendingReservationAsync(stationId);
        ReservationApprovalResult approved = await _fixture.Reservations.ApproveReservationAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice.ToString(),
            pending.Id,
            new ApproveReservationRequestDto
            {
                ExpectedVersion = pending.Version
            },
            $"qr-approve-{Guid.NewGuid():N}",
            CancellationToken.None);
        return approved.Reservation;
    }

    private async Task<ReservationResponseDto> CreatePendingReservationAsync(
        string? stationId = null)
    {
        // Create through Member 3 so tests inherit its station, schedule, and capacity invariants.
        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1),
            stationId: stationId);
        ReservationCreationResult created = await _fixture.CreateOwnAsync(
            slot,
            $"qr-create-{Guid.NewGuid():N}");
        return created.Reservation;
    }

    private Task<IssueQrTransactionResponseDto> IssueAsync(string reservationId)
    {
        // Issue through the production service using the owning active Prosumer claims.
        return _fixture.Transactions.IssueAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer.ToString(),
            reservationId,
            CancellationToken.None);
    }

    private Task<VerifyQrTransactionResponseDto> VerifyAsync(string token)
    {
        // Verify through the production service using the assigned active Grid Operator claims.
        return _fixture.Transactions.VerifyAsync(
            Component3MongoFixture.GridOperatorNic,
            UserRole.GridOperator.ToString(),
            new VerifyQrTransactionRequestDto { Token = token },
            CancellationToken.None);
    }

    private Task<CompleteQrTransactionResponseDto> CompleteAsync(
        VerifyQrTransactionResponseDto verified)
    {
        // Complete through the explicit second-call contract with its bound version and receipt.
        return _fixture.Transactions.CompleteAsync(
            Component3MongoFixture.GridOperatorNic,
            UserRole.GridOperator.ToString(),
            verified.ReservationId,
            new CompleteQrTransactionRequestDto
            {
                VerificationId = verified.VerificationId,
                ExpectedVersion = verified.ReservationVersion
            },
            CancellationToken.None);
    }

    private async Task<(CompleteQrTransactionResponseDto? Response, Exception? Error)>
        CaptureCompletionAsync(VerifyQrTransactionResponseDto verified)
    {
        // Capture both race outcomes so the test can assert exactly one successful completion.
        try
        {
            return (await CompleteAsync(verified), null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }
}
