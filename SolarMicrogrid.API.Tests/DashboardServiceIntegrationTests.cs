/*
 * DashboardServiceIntegrationTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Verifies Member 4 dashboard counts, role isolation, history filters,
 *           deterministic paging, input validation, and MongoDB projections.
 * Infrastructure: Reuses the Component 3 replica-set fixture and fixed UTC clock.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Bson;
using MongoDB.Driver;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Dashboard;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using SolarMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SolarMicrogrid.API.Tests;

[Collection(Component3MongoCollection.Name)]
public sealed class DashboardServiceIntegrationTests : IAsyncLifetime
{
    private readonly Component3MongoFixture _fixture;
    private DashboardService _dashboard = null!;

    public DashboardServiceIntegrationTests(Component3MongoFixture fixture)
    {
        // Reuse the isolated real MongoDB database and deterministic server clock.
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Restore authoritative users/stations and create the production dashboard service.
        await _fixture.ResetAsync();
        _dashboard = new DashboardService(_fixture.Context, _fixture.Clock);
    }

    public Task DisposeAsync()
    {
        // Let the shared fixture reset records and remove its isolated database.
        return Task.CompletedTask;
    }

    [Fact]
    public async Task EmptyProsumerDashboardReturnsZeroCountsAndEmptySections()
    {
        // Keep a valid empty scope successful without hard-coded placeholder data.
        DashboardResponseDto result = await GetDashboardAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer);

        Assert.Equal("OwnReservations", result.Scope);
        Assert.Equal(Component3MongoFixture.FixedNowUtc, result.ServerNowUtc);
        Assert.Equal(0, result.StatusSummary.PendingTotal);
        Assert.Equal(0, result.StatusSummary.ApprovedFutureCount);
        Assert.Empty(result.CurrentReservations);
        Assert.Empty(result.PendingReservations);
        Assert.Empty(result.RecentHistory);
        Assert.Empty(result.RecentTransfers);
    }

    [Fact]
    public async Task ProsumerDashboardCountsEachViewAndExcludesAnotherProsumer()
    {
        // Seed every required view plus an out-of-scope record and assert owner-only summaries.
        EnergyReservation ownPending = BuildReservation(
            Component3MongoFixture.ProsumerOneNic,
            _fixture.StationOneId,
            ReservationStatus.Pending,
            Component3MongoFixture.FixedNowUtc.AddHours(2));
        EnergyReservation ownCurrent = BuildReservation(
            Component3MongoFixture.ProsumerOneNic,
            _fixture.StationOneId,
            ReservationStatus.Approved,
            Component3MongoFixture.FixedNowUtc.AddMinutes(-30),
            endUtc: Component3MongoFixture.FixedNowUtc.AddMinutes(30));
        EnergyReservation ownFuture = BuildReservation(
            Component3MongoFixture.ProsumerOneNic,
            _fixture.StationOneId,
            ReservationStatus.Approved,
            Component3MongoFixture.FixedNowUtc.AddHours(4));
        EnergyReservation ownCompleted = BuildReservation(
            Component3MongoFixture.ProsumerOneNic,
            _fixture.StationOneId,
            ReservationStatus.Completed,
            Component3MongoFixture.FixedNowUtc.AddHours(-3),
            completedAtUtc: Component3MongoFixture.FixedNowUtc.AddHours(-2));
        EnergyReservation otherCompleted = BuildReservation(
            Component3MongoFixture.ProsumerTwoNic,
            _fixture.StationOneId,
            ReservationStatus.Completed,
            Component3MongoFixture.FixedNowUtc.AddHours(-4),
            completedAtUtc: Component3MongoFixture.FixedNowUtc.AddHours(-3));
        await _fixture.Context.Reservations.InsertManyAsync(
            [ownPending, ownCurrent, ownFuture, ownCompleted, otherCompleted]);

        DashboardResponseDto result = await GetDashboardAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer,
            recentLimit: 10);

        Assert.Equal(1, result.StatusSummary.PendingTotal);
        Assert.Equal(2, result.StatusSummary.ApprovedTotal);
        Assert.Equal(1, result.StatusSummary.CompletedTotal);
        Assert.Equal(1, result.StatusSummary.PendingCount);
        Assert.Equal(1, result.StatusSummary.CurrentCount);
        Assert.Equal(1, result.StatusSummary.ApprovedFutureCount);
        Assert.Equal(1, result.StatusSummary.HistoryCount);
        Assert.Equal(ownCurrent.Id, Assert.Single(result.CurrentReservations).ReservationId);
        Assert.Equal(ownPending.Id, Assert.Single(result.PendingReservations).ReservationId);
        Assert.Equal(ownCompleted.Id, Assert.Single(result.RecentHistory).ReservationId);
        Assert.All(
            result.CurrentReservations
                .Concat(result.PendingReservations)
                .Concat(result.RecentHistory),
            item => Assert.Equal(Component3MongoFixture.ProsumerOneNic, item.ProsumerNic));
    }

    [Fact]
    public async Task GridOperatorDashboardIsStationScopedAndReturnsOperationalSections()
    {
        // Prove assigned-station scope applies to totals, pending work, active, recent, and completed transfers.
        EnergyReservation pending = BuildReservation(
            Component3MongoFixture.ProsumerOneNic,
            _fixture.StationOneId,
            ReservationStatus.Pending,
            Component3MongoFixture.FixedNowUtc.AddHours(2));
        EnergyReservation active = BuildReservation(
            Component3MongoFixture.ProsumerTwoNic,
            _fixture.StationOneId,
            ReservationStatus.Approved,
            Component3MongoFixture.FixedNowUtc.AddMinutes(-15),
            endUtc: Component3MongoFixture.FixedNowUtc.AddMinutes(45));
        EnergyReservation completed = BuildReservation(
            Component3MongoFixture.ProsumerOneNic,
            _fixture.StationOneId,
            ReservationStatus.Completed,
            Component3MongoFixture.FixedNowUtc.AddHours(-2),
            completedAtUtc: Component3MongoFixture.FixedNowUtc.AddHours(-1));
        EnergyReservation otherStation = BuildReservation(
            Component3MongoFixture.ProsumerOneNic,
            _fixture.StationTwoId,
            ReservationStatus.Completed,
            Component3MongoFixture.FixedNowUtc.AddHours(-2),
            completedAtUtc: Component3MongoFixture.FixedNowUtc.AddMinutes(-30));
        await _fixture.Context.Reservations.InsertManyAsync(
            [pending, active, completed, otherStation]);

        DashboardResponseDto result = await GetDashboardAsync(
            Component3MongoFixture.GridOperatorNic,
            UserRole.GridOperator,
            recentLimit: 10);

        Assert.Equal("AssignedStation", result.Scope);
        Assert.Equal(_fixture.StationOneId, result.StationId);
        Assert.Equal(1, result.StatusSummary.PendingTotal);
        Assert.Equal(1, result.StatusSummary.ApprovedTotal);
        Assert.Equal(1, result.StatusSummary.CompletedTotal);
        Assert.Equal(pending.Id, Assert.Single(result.PendingReservations).ReservationId);
        Assert.Equal(active.Id, Assert.Single(result.ActiveTransfers).ReservationId);
        Assert.Equal(completed.Id, Assert.Single(result.CompletedTransfers).ReservationId);
        Assert.Equal(2, result.RecentTransfers.Count);
        Assert.All(
            result.PendingReservations
                .Concat(result.ActiveTransfers)
                .Concat(result.CompletedTransfers)
                .Concat(result.RecentTransfers),
            item => Assert.Equal(_fixture.StationOneId, item.StationId));
    }

    [Fact]
    public async Task HistorySupportsStatusStationInclusiveDateAndSafeTextFilters()
    {
        // Exercise each server-side filter, including case-insensitive station and RES reference matching.
        EnergyReservation completed = BuildReservation(
            Component3MongoFixture.ProsumerOneNic,
            _fixture.StationOneId,
            ReservationStatus.Completed,
            Component3MongoFixture.FixedNowUtc.AddDays(-4),
            id: "0000000000000000a1b2c3d4",
            completedAtUtc: Component3MongoFixture.FixedNowUtc.AddDays(-4).AddHours(1));
        EnergyReservation cancelled = BuildReservation(
            Component3MongoFixture.ProsumerTwoNic,
            _fixture.StationTwoId,
            ReservationStatus.Cancelled,
            Component3MongoFixture.FixedNowUtc.AddDays(-3),
            id: "0000000000000000b1c2d3e4");
        EnergyReservation expiredPending = BuildReservation(
            Component3MongoFixture.ProsumerOneNic,
            _fixture.StationOneId,
            ReservationStatus.Pending,
            Component3MongoFixture.FixedNowUtc.AddDays(-2),
            id: "0000000000000000c1d2e3f4");
        EnergyReservation futureApproved = BuildReservation(
            Component3MongoFixture.ProsumerOneNic,
            _fixture.StationOneId,
            ReservationStatus.Approved,
            Component3MongoFixture.FixedNowUtc.AddDays(1));
        await _fixture.Context.Reservations.InsertManyAsync(
            [completed, cancelled, expiredPending, futureApproved]);

        PagedBookingHistoryResponseDto byStatus = await GetHistoryAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice,
            new BookingHistoryQueryDto { Status = ReservationStatus.Completed });
        PagedBookingHistoryResponseDto byStation = await GetHistoryAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice,
            new BookingHistoryQueryDto { StationId = _fixture.StationTwoId });
        PagedBookingHistoryResponseDto byInclusiveDate = await GetHistoryAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice,
            new BookingHistoryQueryDto
            {
                FromUtc = expiredPending.ScheduledStartTimeUtc,
                ToUtc = expiredPending.ScheduledStartTimeUtc
            });
        PagedBookingHistoryResponseDto byStationText = await GetHistoryAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice,
            new BookingHistoryQueryDto
            {
                Search = $"  station {_fixture.StationOneId[^4..].ToLowerInvariant()}  "
            });
        PagedBookingHistoryResponseDto byReference = await GetHistoryAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice,
            new BookingHistoryQueryDto { Search = "res-A1B2C3D4" });

        Assert.Equal(completed.Id, Assert.Single(byStatus.Items).ReservationId);
        Assert.Equal(cancelled.Id, Assert.Single(byStation.Items).ReservationId);
        Assert.Equal(expiredPending.Id, Assert.Single(byInclusiveDate.Items).ReservationId);
        Assert.Equal(2, byStationText.Items.Count);
        Assert.Equal(completed.Id, Assert.Single(byReference.Items).ReservationId);
        Assert.DoesNotContain(futureApproved.Id, byStationText.Items.Select(item => item.ReservationId));
    }

    [Fact]
    public async Task ProsumerHistorySearchNeverReturnsAnotherOwnersRecord()
    {
        // Search a shared station label and ensure the scope predicate remains part of the MongoDB query.
        EnergyReservation own = BuildReservation(
            Component3MongoFixture.ProsumerOneNic,
            _fixture.StationOneId,
            ReservationStatus.Completed,
            Component3MongoFixture.FixedNowUtc.AddDays(-2));
        EnergyReservation other = BuildReservation(
            Component3MongoFixture.ProsumerTwoNic,
            _fixture.StationOneId,
            ReservationStatus.Completed,
            Component3MongoFixture.FixedNowUtc.AddDays(-1));
        await _fixture.Context.Reservations.InsertManyAsync([own, other]);

        PagedBookingHistoryResponseDto result = await GetHistoryAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer,
            new BookingHistoryQueryDto { Search = "integration TEST address" });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(own.Id, Assert.Single(result.Items).ReservationId);
        Assert.DoesNotContain(result.Items, item => item.ReservationId == other.Id);
    }

    [Fact]
    public async Task HistoryPaginationUsesStableDescendingScheduleAndIdOrdering()
    {
        // Give every record the same transfer time and prove ObjectId breaks ties across pages.
        DateTime sharedStart = Component3MongoFixture.FixedNowUtc.AddDays(-2);
        List<EnergyReservation> reservations = Enumerable.Range(1, 5)
            .Select(index => BuildReservation(
                Component3MongoFixture.ProsumerOneNic,
                _fixture.StationOneId,
                ReservationStatus.Completed,
                sharedStart,
                id: index.ToString("x24"),
                completedAtUtc: sharedStart.AddHours(1)))
            .ToList();
        await _fixture.Context.Reservations.InsertManyAsync(reservations);

        PagedBookingHistoryResponseDto first = await GetHistoryAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer,
            new BookingHistoryQueryDto { Page = 1, PageSize = 2 });
        PagedBookingHistoryResponseDto second = await GetHistoryAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer,
            new BookingHistoryQueryDto { Page = 2, PageSize = 2 });
        PagedBookingHistoryResponseDto beyond = await GetHistoryAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer,
            new BookingHistoryQueryDto { Page = 4, PageSize = 2 });

        Assert.Equal(5, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(
            ["000000000000000000000005", "000000000000000000000004"],
            first.Items.Select(item => item.ReservationId));
        Assert.Equal(
            ["000000000000000000000003", "000000000000000000000002"],
            second.Items.Select(item => item.ReservationId));
        Assert.Empty(beyond.Items);
        Assert.Equal(4, beyond.Page);
    }

    [Fact]
    public async Task InvalidFiltersPagingScopeAndClaimsAreRejected()
    {
        // Verify direct service callers receive explicit failures instead of clamped or broadened queries.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => GetHistoryAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer,
            new BookingHistoryQueryDto { Page = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => GetHistoryAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer,
            new BookingHistoryQueryDto { PageSize = 101 }));
        await Assert.ThrowsAsync<ArgumentException>(() => GetHistoryAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer,
            new BookingHistoryQueryDto
            {
                FromUtc = Component3MongoFixture.FixedNowUtc,
                ToUtc = Component3MongoFixture.FixedNowUtc.AddMinutes(-1)
            }));
        await Assert.ThrowsAsync<ArgumentException>(() => GetHistoryAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer,
            new BookingHistoryQueryDto
            {
                FromUtc = DateTime.SpecifyKind(
                    Component3MongoFixture.FixedNowUtc,
                    DateTimeKind.Unspecified)
            }));
        await Assert.ThrowsAsync<ArgumentException>(() => GetHistoryAsync(
            Component3MongoFixture.BackofficeNic,
            UserRole.Backoffice,
            new BookingHistoryQueryDto { StationId = "not-an-object-id" }));
        await Assert.ThrowsAsync<ForbiddenException>(() => GetHistoryAsync(
            Component3MongoFixture.GridOperatorNic,
            UserRole.GridOperator,
            new BookingHistoryQueryDto { StationId = _fixture.StationTwoId }));
        await Assert.ThrowsAsync<ForbiddenException>(() => _dashboard.GetDashboardAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Backoffice.ToString(),
            new DashboardQueryDto(),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _dashboard.GetDashboardAsync(
            Component3MongoFixture.ProsumerOneNic,
            UserRole.Prosumer.ToString(),
            new DashboardQueryDto { RecentLimit = 0 },
            CancellationToken.None));
    }

    private Task<DashboardResponseDto> GetDashboardAsync(
        string actorNic,
        UserRole role,
        int recentLimit = 5)
    {
        // Call the production dashboard contract with explicit authenticated identity claims.
        return _dashboard.GetDashboardAsync(
            actorNic,
            role.ToString(),
            new DashboardQueryDto { RecentLimit = recentLimit },
            CancellationToken.None);
    }

    private Task<PagedBookingHistoryResponseDto> GetHistoryAsync(
        string actorNic,
        UserRole role,
        BookingHistoryQueryDto query)
    {
        // Call the production history contract under the requested persisted role scope.
        return _dashboard.GetBookingHistoryAsync(
            actorNic,
            role.ToString(),
            query,
            CancellationToken.None);
    }

    private static EnergyReservation BuildReservation(
        string prosumerNic,
        string stationId,
        ReservationStatus status,
        DateTime startUtc,
        DateTime? endUtc = null,
        string? id = null,
        DateTime? completedAtUtc = null)
    {
        // Build only authoritative persistence fields needed by dashboard/history projections.
        DateTime normalizedStart = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        DateTime normalizedEnd = DateTime.SpecifyKind(
            endUtc ?? startUtc.AddHours(1),
            DateTimeKind.Utc);
        return new EnergyReservation
        {
            Id = id ?? ObjectId.GenerateNewId().ToString(),
            ProsumerNic = prosumerNic,
            StationId = stationId,
            SlotId = ObjectId.GenerateNewId().ToString(),
            ScheduledStartTimeUtc = normalizedStart,
            ScheduledEndTimeUtc = normalizedEnd,
            RequestedEnergyKwh = 5m,
            Status = status,
            Version = 1,
            CapacityState = status == ReservationStatus.Completed
                ? ReservationCapacityState.Consumed
                : ReservationCapacityState.Held,
            CreatedAtUtc = normalizedStart.AddDays(-1),
            UpdatedAtUtc = completedAtUtc ?? normalizedStart,
            CompletedAtUtc = completedAtUtc,
            CompletedByActorNic = completedAtUtc.HasValue
                ? Component3MongoFixture.GridOperatorNic
                : null
        };
    }
}
