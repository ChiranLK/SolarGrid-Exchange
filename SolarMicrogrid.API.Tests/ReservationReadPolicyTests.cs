/*
 * ReservationReadPolicyTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Preserves focused coverage for Component 3 reservation read scope,
 *           views, combined filters, stable paging, and refreshed actions.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using SolarMicrogrid.API.Models.DTOs.Reservations;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using Xunit;

namespace SolarMicrogrid.API.Tests;

public sealed class ReservationReadPolicyTests
{
    [Fact]
    public void ProsumerScopeIncludesOnlyOwnedReservations()
    {
        // Compile the production expression and reject another Prosumer's record.
        User actor = BuildUser("200012345678", UserRole.Prosumer);
        Func<EnergyReservation, bool> predicate =
            ReservationReadPolicy.BuildScopePredicate(actor).Compile();

        Assert.True(predicate(BuildReservation(actor.Nic, StationOneId)));
        Assert.False(predicate(BuildReservation("199912345678", StationOneId)));
    }

    [Fact]
    public void GridOperatorScopeIncludesOnlyAssignedStation()
    {
        // Verify that the current user's station assignment constrains staff reads.
        User actor = BuildUser("199912345678", UserRole.GridOperator, StationOneId);
        Func<EnergyReservation, bool> predicate =
            ReservationReadPolicy.BuildScopePredicate(actor).Compile();

        Assert.True(predicate(BuildReservation("200012345678", StationOneId)));
        Assert.False(predicate(BuildReservation("200012345678", StationTwoId)));
    }

    [Fact]
    public void ScopeAndViewPredicatesTranslateToMongoDbFilters()
    {
        // Render production predicates to prove authorization and views execute server-side.
        DateTime nowUtc = FixedNowUtc;
        User actor = BuildUser("200012345678", UserRole.Prosumer);
        var renderArgs = new RenderArgs<EnergyReservation>(
            BsonSerializer.SerializerRegistry.GetSerializer<EnergyReservation>(),
            BsonSerializer.SerializerRegistry);
        string scope = Builders<EnergyReservation>.Filter
            .Where(ReservationReadPolicy.BuildScopePredicate(actor))
            .Render(renderArgs)
            .ToString();
        string view = Builders<EnergyReservation>.Filter
            .Where(ReservationReadPolicy.BuildViewPredicate(
                ReservationListView.ApprovedFuture,
                nowUtc))
            .Render(renderArgs)
            .ToString();

        Assert.Contains("prosumer_nic", scope, StringComparison.Ordinal);
        Assert.Contains("status", view, StringComparison.Ordinal);
        Assert.Contains("scheduled_start_time", view, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyResultsReportZeroPages()
    {
        // Keep an empty authorized result successful rather than inventing a page.
        Assert.Equal(0, ReservationReadPolicy.CalculateTotalPages(0, 20));
    }

    [Fact]
    public void CombinedViewStatusAndStationFiltersReturnOnlyMatchingRecord()
    {
        // Combine the production scope/view definitions with status and station filters.
        User actor = BuildUser("199912345678", UserRole.Backoffice);
        Func<EnergyReservation, bool> scope =
            ReservationReadPolicy.BuildScopePredicate(actor).Compile();
        Func<EnergyReservation, bool> view =
            ReservationReadPolicy.BuildViewPredicate(
                ReservationListView.ApprovedFuture,
                FixedNowUtc)
            .Compile();
        List<EnergyReservation> reservations =
        [
            BuildReservation("200012345678", StationOneId, ReservationStatus.Approved,
                FixedNowUtc.AddHours(2)),
            BuildReservation("200012345678", StationOneId, ReservationStatus.Pending,
                FixedNowUtc.AddHours(2)),
            BuildReservation("200012345678", StationTwoId, ReservationStatus.Approved,
                FixedNowUtc.AddHours(2))
        ];

        List<EnergyReservation> result = reservations
            .Where(scope)
            .Where(view)
            .Where(item => item.Status == ReservationStatus.Approved)
            .Where(item => item.StationId == StationOneId)
            .ToList();

        Assert.Single(result);
    }

    [Fact]
    public void NewestFirstPaginationUsesStableIdTieBreak()
    {
        // Mirror the API's CreatedAtUtc/Id descending order across page boundaries.
        List<EnergyReservation> reservations = Enumerable.Range(1, 5)
            .Select(index =>
            {
                EnergyReservation reservation = BuildReservation(
                    "200012345678",
                    StationOneId);
                reservation.Id = index.ToString("x24");
                reservation.CreatedAtUtc = FixedNowUtc.AddMinutes(index / 2);
                return reservation;
            })
            .ToList();

        List<string> pageTwo = reservations
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id, StringComparer.Ordinal)
            .Skip(2)
            .Take(2)
            .Select(item => item.Id)
            .ToList();

        Assert.Equal(
            ["000000000000000000000003", "000000000000000000000002"],
            pageTwo);
        Assert.Equal(3, ReservationReadPolicy.CalculateTotalPages(5, 2));
    }

    [Fact]
    public void AllowedActionsRefreshAfterStatusMutation()
    {
        // Recalculate actions after mutation instead of retaining a stale Pending projection.
        User owner = BuildUser("200012345678", UserRole.Prosumer);
        EnergyReservation reservation = BuildReservation(
            owner.Nic,
            StationOneId,
            ReservationStatus.Pending,
            FixedNowUtc.AddDays(1));
        ReservationAllowedActionsDto before = ReservationReadPolicy.BuildAllowedActions(
            reservation,
            owner,
            FixedNowUtc,
            minimumChangeNoticeHours: 12);

        reservation.Status = ReservationStatus.Cancelled;
        ReservationAllowedActionsDto after = ReservationReadPolicy.BuildAllowedActions(
            reservation,
            owner,
            FixedNowUtc,
            minimumChangeNoticeHours: 12);

        Assert.True(before.CanCancel);
        Assert.False(after.CanCancel);
        Assert.Contains("Cancelled", after.CancelUnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalRecordsRemainInHistory()
    {
        // Confirm every final lifecycle status remains visible in the history definition.
        Func<EnergyReservation, bool> history =
            ReservationReadPolicy.BuildViewPredicate(
                ReservationListView.History,
                FixedNowUtc)
            .Compile();

        foreach (ReservationStatus status in new[]
                 {
                     ReservationStatus.Cancelled,
                     ReservationStatus.Rejected,
                     ReservationStatus.Completed
                 })
        {
            Assert.True(history(BuildReservation(
                "200012345678",
                StationOneId,
                status,
                FixedNowUtc.AddDays(1))));
        }
    }

    private static User BuildUser(
        string nic,
        UserRole role,
        string? assignedStationId = null)
    {
        // Create the minimal current-user state consumed by read policy tests.
        return new User
        {
            Nic = nic,
            Role = role,
            Status = UserStatus.Active,
            AssignedStationId = assignedStationId
        };
    }

    private static EnergyReservation BuildReservation(
        string prosumerNic,
        string stationId,
        ReservationStatus status = ReservationStatus.Pending,
        DateTime? startUtc = null)
    {
        // Create the reservation fields used by scope, view, ordering, and action checks.
        DateTime start = startUtc ?? FixedNowUtc.AddDays(1);
        return new EnergyReservation
        {
            Id = "000000000000000000000010",
            ProsumerNic = prosumerNic,
            StationId = stationId,
            SlotId = "000000000000000000000020",
            Status = status,
            ScheduledStartTimeUtc = start,
            ScheduledEndTimeUtc = start.AddHours(1),
            CreatedAtUtc = FixedNowUtc.AddDays(-1),
            UpdatedAtUtc = FixedNowUtc.AddDays(-1)
        };
    }

    private const string StationOneId = "000000000000000000000001";
    private const string StationTwoId = "000000000000000000000002";
    private static readonly DateTime FixedNowUtc =
        new(2026, 9, 24, 6, 0, 0, DateTimeKind.Utc);
}
