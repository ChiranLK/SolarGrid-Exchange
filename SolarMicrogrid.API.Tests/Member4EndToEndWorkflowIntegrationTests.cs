/*
 * Member4EndToEndWorkflowIntegrationTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Exercises the authenticated Member 4 happy path across real login,
 *           reservation, dashboard, QR verification, completion, and history
 *           services against one isolated MongoDB replica-set database.
 * Boundary: Client rendering/camera behavior remains in web/Android tests; this
 *           test proves the shared server workflow and persisted refresh state.
 * -----------------------------------------------------------------------------
 */

using Microsoft.Extensions.Options;
using MongoDB.Driver;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Helpers;
using SolarMicrogrid.API.Models.DTOs;
using SolarMicrogrid.API.Models.DTOs.Dashboard;
using SolarMicrogrid.API.Models.DTOs.Reservations;
using SolarMicrogrid.API.Models.DTOs.Transactions;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using SolarMicrogrid.API.Settings;
using SolarMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SolarMicrogrid.API.Tests;

[Collection(Component3MongoCollection.Name)]
public sealed class Member4EndToEndWorkflowIntegrationTests : IAsyncLifetime
{
    private const string TestPassword = "member4-test-password-only";
    private const string TestJwtKey = "member4-test-jwt-key-at-least-32-characters";

    private readonly Component3MongoFixture _fixture;

    public Member4EndToEndWorkflowIntegrationTests(Component3MongoFixture fixture)
    {
        // Reuse the same isolated real MongoDB fixture as reservation and transaction tests.
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Reset records before the combined workflow so evidence never depends on another test.
        await _fixture.ResetAsync();
    }

    public Task DisposeAsync()
    {
        // Let the collection fixture own database cleanup after the serialized suite.
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AuthenticatedApprovedQrCompletionRefreshAndReplayWorkflowSucceeds()
    {
        // Run the production services in the same order used by Prosumer and operator clients.
        string passwordHash = PasswordHasher.Hash(TestPassword);
        await _fixture.Context.Users.UpdateManyAsync(
            user => user.Nic == Component3MongoFixture.ProsumerOneNic ||
                user.Nic == Component3MongoFixture.GridOperatorNic,
            Builders<User>.Update.Set(user => user.PasswordHash, passwordHash));
        var auth = new AuthService(
            _fixture.Context,
            new JwtHelper(Options.Create(new JwtSettings
            {
                Key = TestJwtKey,
                Issuer = "member4-tests",
                Audience = "member4-test-clients",
                ExpirationMinutes = 15
            })));
        LoginResponseDto prosumer = await auth.LoginAsync(new LoginRequestDto
        {
            Email = $"{Component3MongoFixture.ProsumerOneNic}@example.test",
            Password = TestPassword
        });
        LoginResponseDto gridOperator = await auth.LoginAsync(new LoginRequestDto
        {
            Email = $"{Component3MongoFixture.GridOperatorNic}@example.test",
            Password = TestPassword
        });

        EnergyBookingSlot slot = await _fixture.AddSlotAsync(
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        ReservationCreationResult creation = await _fixture.CreateOwnAsync(
            slot,
            $"e2e-create-{Guid.NewGuid():N}",
            prosumerNic: prosumer.Nic);
        ReservationApprovalResult approval = await _fixture.Reservations.ApproveReservationAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice.ToString(),
            creation.Reservation.Id,
            new ApproveReservationRequestDto
            {
                ExpectedVersion = creation.Reservation.Version
            },
            $"e2e-approve-{Guid.NewGuid():N}",
            CancellationToken.None);
        var dashboard = new DashboardService(_fixture.Context, _fixture.Clock);
        DashboardResponseDto before = await dashboard.GetDashboardAsync(
            prosumer.Nic,
            prosumer.Role,
            new DashboardQueryDto { RecentLimit = 10 },
            CancellationToken.None);
        PagedReservationResponseDto approvedFuture =
            await _fixture.Reservations.GetReservationsAsync(
                prosumer.Nic,
                prosumer.Role,
                new ReservationListQueryDto
                {
                    View = ReservationListView.ApprovedFuture,
                    Page = 1,
                    PageSize = 20
                },
                CancellationToken.None);

        IssueQrTransactionResponseDto issued = await _fixture.Transactions.IssueAsync(
            prosumer.Nic,
            prosumer.Role,
            approval.Reservation.Id,
            CancellationToken.None);
        VerifyQrTransactionResponseDto verified = await _fixture.Transactions.VerifyAsync(
            gridOperator.Nic,
            gridOperator.Role,
            new VerifyQrTransactionRequestDto { Token = issued.QrToken },
            CancellationToken.None);
        CompleteQrTransactionResponseDto completed = await _fixture.Transactions.CompleteAsync(
            gridOperator.Nic,
            gridOperator.Role,
            verified.ReservationId,
            new CompleteQrTransactionRequestDto
            {
                VerificationId = verified.VerificationId,
                ExpectedVersion = verified.ReservationVersion
            },
            CancellationToken.None);

        DashboardResponseDto prosumerAfter = await dashboard.GetDashboardAsync(
            prosumer.Nic,
            prosumer.Role,
            new DashboardQueryDto { RecentLimit = 10 },
            CancellationToken.None);
        DashboardResponseDto operatorAfter = await dashboard.GetDashboardAsync(
            gridOperator.Nic,
            gridOperator.Role,
            new DashboardQueryDto { RecentLimit = 10 },
            CancellationToken.None);
        PagedBookingHistoryResponseDto history = await dashboard.GetBookingHistoryAsync(
            prosumer.Nic,
            prosumer.Role,
            new BookingHistoryQueryDto { Page = 1, PageSize = 20 },
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(prosumer.Token));
        Assert.False(string.IsNullOrWhiteSpace(gridOperator.Token));
        Assert.Equal(nameof(UserRole.Prosumer), prosumer.Role);
        Assert.Equal(nameof(UserRole.GridOperator), gridOperator.Role);
        Assert.Equal(1, before.StatusSummary.ApprovedFutureCount);
        Assert.Equal(approval.Reservation.Id, Assert.Single(approvedFuture.Items).Id);
        Assert.Equal(43, issued.QrToken.Length);
        Assert.DoesNotContain(prosumer.Nic, issued.QrToken, StringComparison.Ordinal);
        Assert.Equal(nameof(ReservationStatus.Approved), verified.Status);
        Assert.Equal(nameof(ReservationStatus.Completed), completed.Status);
        Assert.Equal(0, prosumerAfter.StatusSummary.ApprovedFutureCount);
        Assert.Equal(1, prosumerAfter.StatusSummary.CompletedTotal);
        Assert.Equal(1, operatorAfter.StatusSummary.CompletedTotal);
        Assert.Equal(completed.ReservationId, Assert.Single(history.Items).ReservationId);
        Assert.Equal(nameof(ReservationStatus.Completed), history.Items[0].Status);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _fixture.Transactions.CompleteAsync(
                gridOperator.Nic,
                gridOperator.Role,
                verified.ReservationId,
                new CompleteQrTransactionRequestDto
                {
                    VerificationId = verified.VerificationId,
                    ExpectedVersion = verified.ReservationVersion
                },
                CancellationToken.None));
    }
}
