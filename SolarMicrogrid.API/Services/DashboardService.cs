/*
 * DashboardService.cs
 * -----------------------------------------------------------------------------
 * Purpose : Produces Member 4 dashboard counts, summaries, history, and search
 *           directly from MongoDB under the existing user/reservation policies.
 * Ownership: Reuses Member 3 view/status definitions and performs no reservation
 *            mutation, scheduling, capacity, approval, or cancellation logic.
 * -----------------------------------------------------------------------------
 */

using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Dashboard;
using SolarMicrogrid.API.Models.DTOs.Reservations;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Services;

public sealed class DashboardService : IDashboardService
{
    private const int MaximumHistoryPageSize = 100;
    private const int MaximumRecentLimit = 20;
    private const int MaximumSearchLength = 100;

    private static readonly Regex NicPattern = new(
        "^([0-9]{9}[VX]|[0-9]{12})$",
        RegexOptions.CultureInvariant);

    private static readonly SortDefinition<EnergyReservation> ScheduledAscendingSort =
        Builders<EnergyReservation>.Sort
            .Ascending(reservation => reservation.ScheduledStartTimeUtc)
            .Ascending(reservation => reservation.Id);

    private static readonly SortDefinition<EnergyReservation> ScheduledDescendingSort =
        Builders<EnergyReservation>.Sort
            .Descending(reservation => reservation.ScheduledStartTimeUtc)
            .Descending(reservation => reservation.Id);

    private static readonly SortDefinition<EnergyReservation> UpdatedDescendingSort =
        Builders<EnergyReservation>.Sort
            .Descending(reservation => reservation.UpdatedAtUtc)
            .Descending(reservation => reservation.Id);

    private static readonly SortDefinition<EnergyReservation> CompletedDescendingSort =
        Builders<EnergyReservation>.Sort
            .Descending(reservation => reservation.CompletedAtUtc)
            .Descending(reservation => reservation.Id);

    private readonly MongoDbContext _context;
    private readonly TimeProvider _timeProvider;

    public DashboardService(MongoDbContext context, TimeProvider timeProvider)
    {
        // Reuse the application's shared MongoDB context and injectable authoritative clock.
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<DashboardResponseDto> GetDashboardAsync(
        string actorNic,
        string actorRoleClaim,
        DashboardQueryDto query,
        CancellationToken cancellationToken)
    {
        // Validate the persisted actor before issuing role-scoped count and summary queries.
        ArgumentNullException.ThrowIfNull(query);
        ValidateRecentLimit(query.RecentLimit);
        User actor = await LoadAndValidateActorAsync(
            actorNic,
            actorRoleClaim,
            cancellationToken);
        DateTime serverNowUtc = GetUtcNow();
        FilterDefinition<EnergyReservation> scopeFilter = BuildScopeFilter(actor);
        FilterDefinition<EnergyReservation> currentFilter = Combine(
            scopeFilter,
            BuildViewFilter(ReservationListView.Current, serverNowUtc));
        FilterDefinition<EnergyReservation> pendingFilter = Combine(
            scopeFilter,
            BuildViewFilter(ReservationListView.Pending, serverNowUtc));
        FilterDefinition<EnergyReservation> approvedFutureFilter = Combine(
            scopeFilter,
            BuildViewFilter(ReservationListView.ApprovedFuture, serverNowUtc));
        FilterDefinition<EnergyReservation> historyFilter = Combine(
            scopeFilter,
            BuildViewFilter(ReservationListView.History, serverNowUtc));
        bool staffDashboard = actor.Role is UserRole.GridOperator or UserRole.Backoffice;

        Task<Dictionary<ReservationStatus, long>> statusCountsTask = LoadStatusCountsAsync(
            scopeFilter,
            cancellationToken);
        Task<long> currentCountTask = CountAsync(currentFilter, cancellationToken);
        Task<long> pendingCountTask = CountAsync(pendingFilter, cancellationToken);
        Task<long> approvedFutureCountTask = CountAsync(approvedFutureFilter, cancellationToken);
        Task<long> historyCountTask = CountAsync(historyFilter, cancellationToken);
        Task<List<EnergyReservation>> currentTask = LoadReservationsAsync(
            currentFilter,
            ScheduledAscendingSort,
            query.RecentLimit,
            cancellationToken);
        Task<List<EnergyReservation>> pendingTask = LoadReservationsAsync(
            pendingFilter,
            ScheduledAscendingSort,
            query.RecentLimit,
            cancellationToken);
        Task<List<EnergyReservation>> historyTask = LoadReservationsAsync(
            historyFilter,
            ScheduledDescendingSort,
            query.RecentLimit,
            cancellationToken);
        Task<List<EnergyReservation>> recentTransfersTask = staffDashboard
            ? LoadReservationsAsync(
                Combine(
                    scopeFilter,
                    Builders<EnergyReservation>.Filter.In(
                        reservation => reservation.Status,
                        [ReservationStatus.Approved, ReservationStatus.Completed])),
                UpdatedDescendingSort,
                query.RecentLimit,
                cancellationToken)
            : Task.FromResult(new List<EnergyReservation>());
        Task<List<EnergyReservation>> completedTransfersTask = staffDashboard
            ? LoadReservationsAsync(
                Combine(
                    scopeFilter,
                    Builders<EnergyReservation>.Filter.Eq(
                        reservation => reservation.Status,
                        ReservationStatus.Completed)),
                CompletedDescendingSort,
                query.RecentLimit,
                cancellationToken)
            : Task.FromResult(new List<EnergyReservation>());

        await Task.WhenAll(
            statusCountsTask,
            currentCountTask,
            pendingCountTask,
            approvedFutureCountTask,
            historyCountTask,
            currentTask,
            pendingTask,
            historyTask,
            recentTransfersTask,
            completedTransfersTask);

        List<EnergyReservation> allSummaries = currentTask.Result
            .Concat(pendingTask.Result)
            .Concat(historyTask.Result)
            .Concat(recentTransfersTask.Result)
            .Concat(completedTransfersTask.Result)
            .GroupBy(reservation => reservation.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        DashboardDisplayReferences references = await LoadDisplayReferencesAsync(
            allSummaries,
            cancellationToken);
        Dictionary<ReservationStatus, long> statusCounts = statusCountsTask.Result;

        return new DashboardResponseDto
        {
            ServerNowUtc = serverNowUtc,
            Role = actor.Role.ToString(),
            Scope = actor.Role switch
            {
                UserRole.Prosumer => "OwnReservations",
                UserRole.GridOperator => "AssignedStation",
                UserRole.Backoffice => "Global",
                _ => throw new ForbiddenException("This user role cannot access dashboards.")
            },
            StationId = actor.Role == UserRole.GridOperator ? actor.AssignedStationId : null,
            StatusSummary = new DashboardStatusSummaryDto
            {
                PendingTotal = GetStatusCount(statusCounts, ReservationStatus.Pending),
                ApprovedTotal = GetStatusCount(statusCounts, ReservationStatus.Approved),
                RejectedTotal = GetStatusCount(statusCounts, ReservationStatus.Rejected),
                CancelledTotal = GetStatusCount(statusCounts, ReservationStatus.Cancelled),
                CompletedTotal = GetStatusCount(statusCounts, ReservationStatus.Completed),
                CurrentCount = currentCountTask.Result,
                PendingCount = pendingCountTask.Result,
                ApprovedFutureCount = approvedFutureCountTask.Result,
                HistoryCount = historyCountTask.Result
            },
            CurrentReservations = actor.Role == UserRole.Prosumer
                ? MapSummaries(currentTask.Result, references)
                : [],
            PendingReservations = MapSummaries(pendingTask.Result, references),
            RecentHistory = MapSummaries(historyTask.Result, references),
            RecentTransfers = staffDashboard
                ? MapSummaries(recentTransfersTask.Result, references)
                : [],
            ActiveTransfers = staffDashboard
                ? MapSummaries(currentTask.Result, references)
                : [],
            CompletedTransfers = staffDashboard
                ? MapSummaries(completedTransfersTask.Result, references)
                : []
        };
    }

    public async Task<PagedBookingHistoryResponseDto> GetBookingHistoryAsync(
        string actorNic,
        string actorRoleClaim,
        BookingHistoryQueryDto query,
        CancellationToken cancellationToken)
    {
        // Validate paging/time inputs, scope in MongoDB, then filter and sort before materializing a page.
        ArgumentNullException.ThrowIfNull(query);
        ValidateHistoryPaging(query.Page, query.PageSize);
        User actor = await LoadAndValidateActorAsync(
            actorNic,
            actorRoleClaim,
            cancellationToken);
        DateTime serverNowUtc = GetUtcNow();
        DateTime? fromUtc = query.FromUtc.HasValue
            ? NormalizeUtc(query.FromUtc.Value, nameof(query.FromUtc))
            : null;
        DateTime? toUtc = query.ToUtc.HasValue
            ? NormalizeUtc(query.ToUtc.Value, nameof(query.ToUtc))
            : null;
        if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
        {
            throw new ArgumentException("FromUtc cannot be later than ToUtc.", nameof(query));
        }

        var filters = new List<FilterDefinition<EnergyReservation>>
        {
            BuildScopeFilter(actor),
            BuildViewFilter(ReservationListView.History, serverNowUtc)
        };
        if (query.Status.HasValue)
        {
            filters.Add(Builders<EnergyReservation>.Filter.Eq(
                reservation => reservation.Status,
                query.Status.Value));
        }

        if (!string.IsNullOrWhiteSpace(query.StationId))
        {
            string stationId = NormalizeObjectId(query.StationId, nameof(query.StationId));
            if (actor.Role == UserRole.GridOperator &&
                !string.Equals(actor.AssignedStationId, stationId, StringComparison.Ordinal))
            {
                throw new ForbiddenException(
                    "Grid Operators may filter history only for their assigned station.");
            }

            filters.Add(Builders<EnergyReservation>.Filter.Eq(
                reservation => reservation.StationId,
                stationId));
        }

        if (fromUtc.HasValue)
        {
            filters.Add(Builders<EnergyReservation>.Filter.Gte(
                reservation => reservation.ScheduledStartTimeUtc,
                fromUtc.Value));
        }

        if (toUtc.HasValue)
        {
            filters.Add(Builders<EnergyReservation>.Filter.Lte(
                reservation => reservation.ScheduledStartTimeUtc,
                toUtc.Value));
        }

        string? search = NormalizeOptionalSearch(query.Search);
        if (search is not null)
        {
            filters.Add(await BuildSearchFilterAsync(search, cancellationToken));
        }

        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(filters);
        long totalCount = await _context.Reservations.CountDocumentsAsync(
            filter,
            cancellationToken: cancellationToken);
        long skip = (long)(query.Page - 1) * query.PageSize;
        List<EnergyReservation> reservations = skip > int.MaxValue
            ? []
            : await _context.Reservations
                .Find(filter)
                .Sort(ScheduledDescendingSort)
                .Skip((int)skip)
                .Limit(query.PageSize)
                .ToListAsync(cancellationToken);
        DashboardDisplayReferences references = await LoadDisplayReferencesAsync(
            reservations,
            cancellationToken);

        return new PagedBookingHistoryResponseDto
        {
            ServerNowUtc = serverNowUtc,
            Items = MapSummaries(reservations, references),
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalPages = ReservationReadPolicy.CalculateTotalPages(totalCount, query.PageSize)
        };
    }

    private async Task<User> LoadAndValidateActorAsync(
        string actorNic,
        string actorRoleClaim,
        CancellationToken cancellationToken)
    {
        // Match exact JWT claims to one active persisted user before building any data scope.
        string normalizedNic = NormalizeNic(actorNic, nameof(actorNic));
        User? actor = await _context.Users
            .Find(user => user.Nic == normalizedNic)
            .FirstOrDefaultAsync(cancellationToken);
        if (actor is null)
        {
            throw new UnauthorizedException("The authenticated user no longer exists.");
        }

        if (!Enum.TryParse(actorRoleClaim, ignoreCase: false, out UserRole claimedRole) ||
            actor.Role != claimedRole)
        {
            throw new ForbiddenException("The authenticated role does not match the current user record.");
        }

        if (actor.Status != UserStatus.Active)
        {
            throw new ForbiddenException("Only active users may access dashboard data.");
        }

        if (actor.Role == UserRole.GridOperator &&
            !ObjectId.TryParse(actor.AssignedStationId, out _))
        {
            throw new ForbiddenException(
                "The Grid Operator does not have a valid assigned station.");
        }

        return actor;
    }

    private static FilterDefinition<EnergyReservation> BuildScopeFilter(User actor)
    {
        // Translate the existing reservation access policy into a server-side MongoDB predicate.
        return Builders<EnergyReservation>.Filter.Where(
            ReservationReadPolicy.BuildScopePredicate(actor));
    }

    private static FilterDefinition<EnergyReservation> BuildViewFilter(
        ReservationListView view,
        DateTime serverNowUtc)
    {
        // Reuse Member 3's canonical current, pending, future, and history definitions.
        return Builders<EnergyReservation>.Filter.Where(
            ReservationReadPolicy.BuildViewPredicate(view, serverNowUtc));
    }

    private static FilterDefinition<EnergyReservation> Combine(
        params FilterDefinition<EnergyReservation>[] filters)
    {
        // Combine scope and data predicates without allowing callers to replace authorization scope.
        return Builders<EnergyReservation>.Filter.And(filters);
    }

    private async Task<Dictionary<ReservationStatus, long>> LoadStatusCountsAsync(
        FilterDefinition<EnergyReservation> scopeFilter,
        CancellationToken cancellationToken)
    {
        // Aggregate exact persisted status totals in MongoDB instead of counting client-side records.
        List<DashboardStatusAggregation> counts = await _context.Reservations
            .Aggregate()
            .Match(scopeFilter)
            .Group(
                reservation => reservation.Status,
                group => new DashboardStatusAggregation
                {
                    Status = group.Key,
                    Count = group.Count()
                })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(item => item.Status, item => (long)item.Count);
    }

    private Task<long> CountAsync(
        FilterDefinition<EnergyReservation> filter,
        CancellationToken cancellationToken)
    {
        // Execute each semantic view count directly in MongoDB under the actor's scope.
        return _context.Reservations.CountDocumentsAsync(
            filter,
            cancellationToken: cancellationToken);
    }

    private Task<List<EnergyReservation>> LoadReservationsAsync(
        FilterDefinition<EnergyReservation> filter,
        SortDefinition<EnergyReservation> sort,
        int limit,
        CancellationToken cancellationToken)
    {
        // Return only the bounded, deterministically sorted records required by a dashboard section.
        return _context.Reservations
            .Find(filter)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    private async Task<FilterDefinition<EnergyReservation>> BuildSearchFilterAsync(
        string search,
        CancellationToken cancellationToken)
    {
        // Escape user text and resolve case-insensitive display/reference matches without widening scope.
        var pattern = new BsonRegularExpression(Regex.Escape(search), "i");
        Task<List<string>> stationTask = _context.Stations
            .Find(Builders<SolarStationInfo>.Filter.Or(
                Builders<SolarStationInfo>.Filter.Regex(station => station.Name, pattern),
                Builders<SolarStationInfo>.Filter.Regex(station => station.Address, pattern)))
            .Project(station => station.Id)
            .ToListAsync(cancellationToken);
        Task<List<string>> prosumerTask = _context.Users
            .Find(Builders<User>.Filter.Or(
                Builders<User>.Filter.Regex(user => user.Nic, pattern),
                Builders<User>.Filter.Regex(user => user.FullName, pattern)))
            .Project(user => user.Nic)
            .ToListAsync(cancellationToken);
        await Task.WhenAll(stationTask, prosumerTask);

        var searchFilters = new List<FilterDefinition<EnergyReservation>>
        {
            Builders<EnergyReservation>.Filter.Regex(
                reservation => reservation.ProsumerNic,
                pattern)
        };
        if (stationTask.Result.Count > 0)
        {
            searchFilters.Add(Builders<EnergyReservation>.Filter.In(
                reservation => reservation.StationId,
                stationTask.Result));
        }

        if (prosumerTask.Result.Count > 0)
        {
            searchFilters.Add(Builders<EnergyReservation>.Filter.In(
                reservation => reservation.ProsumerNic,
                prosumerTask.Result));
        }

        string referenceTerm = search.StartsWith("RES-", StringComparison.OrdinalIgnoreCase)
            ? search[4..]
            : search;
        if (referenceTerm.Length > 0 && referenceTerm.All(Uri.IsHexDigit))
        {
            searchFilters.Add(new BsonDocument(
                "$expr",
                new BsonDocument(
                    "$regexMatch",
                    new BsonDocument
                    {
                        { "input", new BsonDocument("$toString", "$_id") },
                        { "regex", Regex.Escape(referenceTerm) },
                        { "options", "i" }
                    })));
        }

        return Builders<EnergyReservation>.Filter.Or(searchFilters);
    }

    private async Task<DashboardDisplayReferences> LoadDisplayReferencesAsync(
        IReadOnlyCollection<EnergyReservation> reservations,
        CancellationToken cancellationToken)
    {
        // Batch station and Prosumer display lookups to avoid per-row database requests.
        if (reservations.Count == 0)
        {
            return new DashboardDisplayReferences(
                new Dictionary<string, SolarStationInfo>(),
                new Dictionary<string, User>());
        }

        string[] stationIds = reservations
            .Select(reservation => reservation.StationId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] prosumerNics = reservations
            .Select(reservation => reservation.ProsumerNic)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Task<List<SolarStationInfo>> stationTask = _context.Stations
            .Find(Builders<SolarStationInfo>.Filter.In(station => station.Id, stationIds))
            .ToListAsync(cancellationToken);
        Task<List<User>> prosumerTask = _context.Users
            .Find(Builders<User>.Filter.In(user => user.Nic, prosumerNics))
            .ToListAsync(cancellationToken);
        await Task.WhenAll(stationTask, prosumerTask);

        return new DashboardDisplayReferences(
            stationTask.Result.ToDictionary(station => station.Id, StringComparer.Ordinal),
            prosumerTask.Result.ToDictionary(user => user.Nic, StringComparer.Ordinal));
    }

    private static IReadOnlyList<DashboardReservationSummaryDto> MapSummaries(
        IEnumerable<EnergyReservation> reservations,
        DashboardDisplayReferences references)
    {
        // Project only stable display fields and omit all internal capacity/idempotency state.
        return reservations.Select(reservation =>
        {
            references.Stations.TryGetValue(reservation.StationId, out SolarStationInfo? station);
            references.Prosumers.TryGetValue(reservation.ProsumerNic, out User? prosumer);
            return new DashboardReservationSummaryDto
            {
                ReservationId = reservation.Id,
                Reference = BuildReference(reservation.Id),
                ProsumerNic = reservation.ProsumerNic,
                ProsumerFullName = prosumer?.FullName,
                StationId = reservation.StationId,
                StationName = station?.Name,
                StationAddress = station?.Address,
                SlotId = reservation.SlotId,
                ScheduledStartTimeUtc = reservation.ScheduledStartTimeUtc,
                ScheduledEndTimeUtc = reservation.ScheduledEndTimeUtc,
                RequestedEnergyKwh = reservation.RequestedEnergyKwh,
                Status = reservation.Status.ToString(),
                Version = reservation.Version,
                CreatedAtUtc = reservation.CreatedAtUtc,
                UpdatedAtUtc = reservation.UpdatedAtUtc,
                CompletedAtUtc = reservation.CompletedAtUtc
            };
        }).ToList();
    }

    private static string BuildReference(string reservationId)
    {
        // Match the established client-facing RES plus final-eight-hex reference format.
        string suffix = reservationId.Length <= 8 ? reservationId : reservationId[^8..];
        return $"RES-{suffix.ToUpperInvariant()}";
    }

    private static long GetStatusCount(
        IReadOnlyDictionary<ReservationStatus, long> counts,
        ReservationStatus status)
    {
        // Return zero for an absent MongoDB aggregation bucket rather than inventing data.
        return counts.TryGetValue(status, out long count) ? count : 0;
    }

    private static string NormalizeNic(string? value, string parameterName)
    {
        // Normalize and validate the repository's exact Sri Lankan NIC identity format.
        string normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!NicPattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "NIC must be 9 digits followed by V or X, or 12 digits.",
                parameterName);
        }

        return normalized;
    }

    private static string NormalizeObjectId(string? value, string parameterName)
    {
        // Normalize a station identifier before adding it to the MongoDB query.
        if (!ObjectId.TryParse(value?.Trim(), out ObjectId objectId))
        {
            throw new ArgumentException("A valid MongoDB ObjectId is required.", parameterName);
        }

        return objectId.ToString();
    }

    private static string? NormalizeOptionalSearch(string? value)
    {
        // Trim optional text and enforce the public bound again for direct service callers.
        string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > MaximumSearchLength)
        {
            throw new ArgumentException(
                $"Search cannot exceed {MaximumSearchLength} characters.",
                nameof(value));
        }

        return normalized;
    }

    private static DateTime NormalizeUtc(DateTime value, string parameterName)
    {
        // Require an explicit time-zone kind and compare all inclusive boundaries in UTC.
        if (value == default || value.Kind == DateTimeKind.Unspecified)
        {
            throw new ArgumentException(
                "A timestamp with an explicit UTC kind is required.",
                parameterName);
        }

        return value.ToUniversalTime();
    }

    private static void ValidateRecentLimit(int recentLimit)
    {
        // Keep every dashboard list bounded independently of model-binding validation.
        if (recentLimit is < 1 or > MaximumRecentLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recentLimit),
                $"RecentLimit must be between 1 and {MaximumRecentLimit}.");
        }
    }

    private static void ValidateHistoryPaging(int page, int pageSize)
    {
        // Reject rather than clamp invalid pages so API clients receive an explicit 400 response.
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "Page must be at least 1.");
        }

        if (pageSize is < 1 or > MaximumHistoryPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                $"PageSize must be between 1 and {MaximumHistoryPageSize}.");
        }
    }

    private DateTime GetUtcNow()
    {
        // Capture authoritative server time through the injectable clock for deterministic tests.
        return _timeProvider.GetUtcNow().UtcDateTime;
    }

    private sealed class DashboardStatusAggregation
    {
        public ReservationStatus Status { get; set; }

        public int Count { get; set; }
    }

    private sealed record DashboardDisplayReferences(
        IReadOnlyDictionary<string, SolarStationInfo> Stations,
        IReadOnlyDictionary<string, User> Prosumers);
}
