/*
 * Program.cs
 * -----------------------------------------------------------------------------
 * Purpose : Runs dependency-free focused checks for Component 3 reservation read
 *           authorization, views, filtering, paging, and refreshed action state.
 * -----------------------------------------------------------------------------
 */

using SolarMicrogrid.API.Models.DTOs.Reservations;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

var tests = new (string Name, Action Run)[]
{
    ("Prosumer ownership isolation", TestProsumerOwnershipIsolation),
    ("Grid Operator station isolation", TestGridOperatorStationIsolation),
    ("MongoDB scope and view translation", TestMongoFilterTranslation),
    ("Empty result paging", TestEmptyResultPaging),
    ("Combined station, status, and view filters", TestCombinedFilters),
    ("Stable newest-first pagination", TestStableNewestFirstPagination),
    ("Status refresh after mutation", TestStatusRefreshAfterMutation),
    ("Final records remain in history", TestFinalRecordsRemainInHistory)
};

foreach ((string name, Action run) in tests)
{
    run();
    Console.WriteLine($"PASS: {name}");
}

Console.WriteLine($"PASS: {tests.Length} reservation read policy checks completed.");

static void TestProsumerOwnershipIsolation()
{
    // Compile the same expression translated by MongoDB and reject another owner's record.
    User actor = BuildUser("200012345678", UserRole.Prosumer);
    Func<EnergyReservation, bool> predicate =
        ReservationReadPolicy.BuildScopePredicate(actor).Compile();

    Assert(predicate(BuildReservation("200012345678", "000000000000000000000001")),
        "The owner should see their reservation.");
    Assert(!predicate(BuildReservation("199912345678", "000000000000000000000001")),
        "A Prosumer must not see another owner's reservation.");
}

static void TestGridOperatorStationIsolation()
{
    // Verify station assignment is part of the staff scope predicate.
    const string assignedStationId = "000000000000000000000001";
    User actor = BuildUser("199912345678", UserRole.GridOperator, assignedStationId);
    Func<EnergyReservation, bool> predicate =
        ReservationReadPolicy.BuildScopePredicate(actor).Compile();

    Assert(predicate(BuildReservation("200012345678", assignedStationId)),
        "The assigned station should be visible.");
    Assert(!predicate(BuildReservation("200012345678", "000000000000000000000002")),
        "Another station must not be visible.");
}

static void TestMongoFilterTranslation()
{
    // Render the production predicates to prove MongoDB applies scope and view server-side.
    DateTime nowUtc = new(2026, 9, 24, 6, 0, 0, DateTimeKind.Utc);
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

    Assert(scope.Contains("prosumer_nic", StringComparison.Ordinal),
        "The rendered owner scope should filter Prosumer NIC in MongoDB.");
    Assert(view.Contains("status", StringComparison.Ordinal) &&
           view.Contains("scheduled_start_time", StringComparison.Ordinal),
        "The rendered future view should filter status and start time in MongoDB.");
}

static void TestEmptyResultPaging()
{
    // Keep empty results successful and expose zero total pages.
    List<EnergyReservation> reservations = [];
    Assert(reservations.Count == 0, "The fixture should be empty.");
    Assert(ReservationReadPolicy.CalculateTotalPages(reservations.Count, 20) == 0,
        "Empty results should report zero pages.");
}

static void TestCombinedFilters()
{
    // Combine the production scope/view definitions with status and station filters.
    DateTime nowUtc = new(2026, 9, 24, 6, 0, 0, DateTimeKind.Utc);
    const string stationId = "000000000000000000000001";
    User actor = BuildUser("199912345678", UserRole.Backoffice);
    Func<EnergyReservation, bool> scope =
        ReservationReadPolicy.BuildScopePredicate(actor).Compile();
    Func<EnergyReservation, bool> view =
        ReservationReadPolicy.BuildViewPredicate(ReservationListView.ApprovedFuture, nowUtc)
            .Compile();
    List<EnergyReservation> reservations =
    [
        BuildReservation("200012345678", stationId, ReservationStatus.Approved,
            nowUtc.AddHours(2), nowUtc.AddHours(3)),
        BuildReservation("200012345678", stationId, ReservationStatus.Pending,
            nowUtc.AddHours(2), nowUtc.AddHours(3)),
        BuildReservation("200012345678", "000000000000000000000002",
            ReservationStatus.Approved, nowUtc.AddHours(2), nowUtc.AddHours(3))
    ];

    List<EnergyReservation> result = reservations
        .Where(scope)
        .Where(view)
        .Where(item => item.Status == ReservationStatus.Approved)
        .Where(item => item.StationId == stationId)
        .ToList();
    Assert(result.Count == 1, "Combined filters should return only the matching record.");
}

static void TestStableNewestFirstPagination()
{
    // Mirror the API's CreatedAtUtc/Id descending tie-break order across page boundaries.
    DateTime createdAtUtc = new(2026, 9, 24, 6, 0, 0, DateTimeKind.Utc);
    List<EnergyReservation> reservations = Enumerable.Range(1, 5)
        .Select(index =>
        {
            EnergyReservation reservation = BuildReservation(
                "200012345678",
                "000000000000000000000001");
            reservation.Id = index.ToString("x24");
            reservation.CreatedAtUtc = createdAtUtc.AddMinutes(index / 2);
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

    Assert(pageTwo.SequenceEqual(["000000000000000000000003", "000000000000000000000002"]),
        "The stable second page did not contain the expected records.");
    Assert(ReservationReadPolicy.CalculateTotalPages(reservations.Count, 2) == 3,
        "Five records at two per page should report three pages.");
}

static void TestStatusRefreshAfterMutation()
{
    // Recalculate actions after a mutation instead of retaining a cached Pending projection.
    DateTime nowUtc = new(2026, 9, 24, 6, 0, 0, DateTimeKind.Utc);
    User owner = BuildUser("200012345678", UserRole.Prosumer);
    EnergyReservation reservation = BuildReservation(
        owner.Nic,
        "000000000000000000000001",
        ReservationStatus.Pending,
        nowUtc.AddDays(1),
        nowUtc.AddDays(1).AddHours(1));
    ReservationAllowedActionsDto before = ReservationReadPolicy.BuildAllowedActions(
        reservation,
        owner,
        nowUtc,
        minimumChangeNoticeHours: 12);

    reservation.Status = ReservationStatus.Cancelled;
    ReservationAllowedActionsDto after = ReservationReadPolicy.BuildAllowedActions(
        reservation,
        owner,
        nowUtc,
        minimumChangeNoticeHours: 12);

    Assert(before.CanCancel, "The future Pending reservation should initially be cancellable.");
    Assert(!after.CanCancel, "The refreshed Cancelled reservation must not be cancellable.");
    Assert(after.CancelUnavailableReason?.Contains("Cancelled", StringComparison.Ordinal) == true,
        "The refreshed result should explain the final status.");
}

static void TestFinalRecordsRemainInHistory()
{
    // Confirm every final lifecycle status remains visible through the history definition.
    DateTime nowUtc = new(2026, 9, 24, 6, 0, 0, DateTimeKind.Utc);
    Func<EnergyReservation, bool> history =
        ReservationReadPolicy.BuildViewPredicate(ReservationListView.History, nowUtc).Compile();

    foreach (ReservationStatus status in new[]
             {
                 ReservationStatus.Cancelled,
                 ReservationStatus.Rejected,
                 ReservationStatus.Completed
             })
    {
        Assert(history(BuildReservation(
                "200012345678",
                "000000000000000000000001",
                status,
                nowUtc.AddDays(1),
                nowUtc.AddDays(1).AddHours(1))),
            $"{status} should remain visible in history.");
    }
}

static User BuildUser(string nic, UserRole role, string? assignedStationId = null)
{
    // Create the minimal current user state consumed by read policy tests.
    return new User
    {
        Nic = nic,
        Role = role,
        Status = UserStatus.Active,
        AssignedStationId = assignedStationId
    };
}

static EnergyReservation BuildReservation(
    string prosumerNic,
    string stationId,
    ReservationStatus status = ReservationStatus.Pending,
    DateTime? startUtc = null,
    DateTime? endUtc = null)
{
    // Create a complete enough reservation for scope, view, ordering, and action checks.
    DateTime start = startUtc ?? new DateTime(2026, 9, 25, 6, 0, 0, DateTimeKind.Utc);
    return new EnergyReservation
    {
        Id = "000000000000000000000010",
        ProsumerNic = prosumerNic,
        StationId = stationId,
        SlotId = "000000000000000000000020",
        Status = status,
        ScheduledStartTimeUtc = start,
        ScheduledEndTimeUtc = endUtc ?? start.AddHours(1),
        CreatedAtUtc = new DateTime(2026, 9, 23, 6, 0, 0, DateTimeKind.Utc),
        UpdatedAtUtc = new DateTime(2026, 9, 23, 6, 0, 0, DateTimeKind.Utc)
    };
}

static void Assert(bool condition, string message)
{
    // Fail the process so CI and local callers receive a non-zero exit code.
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
