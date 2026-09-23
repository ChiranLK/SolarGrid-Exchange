/*
 * ReservationService.cs
 * -----------------------------------------------------------------------------
 * Purpose : Provides the Component 3 reservation domain foundation: reference
 *           loading, active-status rules, duplicate/overlap validation, lifecycle
 *           transitions, optimistic versions, capacity orchestration, and repair.
 * Consistency: Multi-document callers use MongoTransactionRunner; standalone
 *              fallback uses idempotent slot claims and explicit reconciliation.
 * -----------------------------------------------------------------------------
 */

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Reservations;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Settings;

namespace SolarMicrogrid.API.Services;

public sealed record ReservationReferenceSet(
    EnergyReservation Reservation,
    SolarStationInfo Station,
    EnergyBookingSlot Slot);

public sealed record CapacityReconciliationResult(
    string ReservationId,
    ReservationCapacityState CapacityState,
    int ReleasedStaleClaims,
    bool IsConsistent);

public sealed record ReservationCreationResult(
    ReservationResponseDto Reservation,
    bool IdempotencyReplayed);

public sealed record ReservationUpdateResult(
    ReservationResponseDto Reservation,
    bool IdempotencyReplayed);

public sealed record ReservationCancellationResult(
    ReservationResponseDto Reservation,
    bool IdempotencyReplayed);

public sealed record ReservationApprovalResult(
    ReservationResponseDto Reservation,
    bool IdempotencyReplayed);

public sealed record ReservationRejectionResult(
    ReservationResponseDto Reservation,
    bool IdempotencyReplayed);

internal sealed record ReservationDisplayReferences(
    IReadOnlyDictionary<string, SolarStationInfo> Stations,
    IReadOnlyDictionary<string, EnergyBookingSlot> Slots);

internal sealed record ReservationCreationEntityResult(
    EnergyReservation Reservation,
    bool IdempotencyReplayed);

internal sealed record ReservationDecisionEntityResult(
    EnergyReservation Reservation,
    User Actor,
    bool IdempotencyReplayed);

public sealed class ReservationService
{
    private const int MaximumPageSize = 100;
    private const string OperatingScheduleTimeZoneId = "Asia/Colombo";
    private const int MaximumIdempotencyKeyLength = 200;
    private const int MinimumIdempotencyKeyLength = 8;
    private const int MaximumAuditReasonLength = 500;

    private static readonly ReservationStatus[] CapacityHoldingStatuses =
        [ReservationStatus.Pending, ReservationStatus.Approved];

    private static readonly TimeZoneInfo OperatingScheduleTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(OperatingScheduleTimeZoneId);

    private static readonly Regex NicPattern = new(
        "^([0-9]{9}[VX]|[0-9]{12})$",
        RegexOptions.CultureInvariant);

    private readonly MongoDbContext _context;
    private readonly ReservationCapacityService _capacityService;
    private readonly MongoTransactionRunner _transactionRunner;
    private readonly ReservationSchedulingGuardService _schedulingGuardService;
    private readonly TimeProvider _timeProvider;
    private readonly int _maximumBookingDaysAhead;
    private readonly int _minimumChangeNoticeHours;

    public ReservationService(
        MongoDbContext context,
        ReservationCapacityService capacityService,
        MongoTransactionRunner transactionRunner,
        ReservationSchedulingGuardService schedulingGuardService,
        IOptions<BusinessRules> businessRulesOptions,
        TimeProvider timeProvider)
    {
        // Reuse shared persistence and consistency services through dependency injection.
        _context = context;
        _capacityService = capacityService;
        _transactionRunner = transactionRunner;
        _schedulingGuardService = schedulingGuardService;
        _timeProvider = timeProvider;
        BusinessRules rules = businessRulesOptions.Value;
        _maximumBookingDaysAhead = rules.MaxBookingDaysAhead;
        _minimumChangeNoticeHours = rules.MinChangeNoticeHours;
    }

    public async Task<PagedReservationResponseDto> GetReservationsAsync(
        string actorNic,
        string actorRoleClaim,
        ReservationListQueryDto query,
        CancellationToken cancellationToken)
    {
        // Build one server-side role scope and combine it with every requested list filter.
        ArgumentNullException.ThrowIfNull(query);
        User actor = await LoadAndValidateActorAsync(
            actorNic,
            actorRoleClaim,
            [UserRole.Prosumer, UserRole.Backoffice, UserRole.GridOperator],
            session: null,
            cancellationToken);
        DateTime serverNowUtc = GetUtcNow();
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var filters = new List<FilterDefinition<EnergyReservation>>
        {
            BuildReadScopeFilter(actor),
            BuildViewFilter(query.View, serverNowUtc)
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
                    "Grid Operators may filter reservations only for their assigned station.");
            }

            filters.Add(Builders<EnergyReservation>.Filter.Eq(
                reservation => reservation.StationId,
                stationId));
        }

        if (!string.IsNullOrWhiteSpace(query.ProsumerNic))
        {
            if (actor.Role != UserRole.Backoffice)
            {
                throw new ForbiddenException(
                    "Only Backoffice users may filter reservations by Prosumer NIC.");
            }

            string prosumerNic = NormalizeNic(query.ProsumerNic, nameof(query.ProsumerNic));
            filters.Add(Builders<EnergyReservation>.Filter.Eq(
                reservation => reservation.ProsumerNic,
                prosumerNic));
        }

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
            if (actor.Role != UserRole.Backoffice)
            {
                throw new ForbiddenException("Only Backoffice users may search reservations.");
            }

            filters.Add(await BuildSearchFilterAsync(search, cancellationToken));
        }

        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(filters);
        long totalCount = await _context.Reservations.CountDocumentsAsync(
            filter,
            cancellationToken: cancellationToken);
        long skip = (long)(page - 1) * pageSize;
        List<EnergyReservation> reservations = skip > int.MaxValue
            ? []
            : await _context.Reservations
                .Find(filter)
                .SortByDescending(reservation => reservation.CreatedAtUtc)
                .ThenByDescending(reservation => reservation.Id)
                .Skip((int)skip)
                .Limit(pageSize)
                .ToListAsync(cancellationToken);
        ReservationDisplayReferences references = await LoadDisplayReferencesAsync(
            reservations,
            cancellationToken);

        return new PagedReservationResponseDto
        {
            Items = reservations
                .Select(reservation => MapToListItem(
                    reservation,
                    actor,
                    serverNowUtc,
                    references))
                .ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = ReservationReadPolicy.CalculateTotalPages(totalCount, pageSize)
        };
    }

    public async Task<ReservationResponseDto> GetReservationByIdAsync(
        string actorNic,
        string actorRoleClaim,
        string reservationId,
        CancellationToken cancellationToken)
    {
        // Include ownership or assigned-station scope in the direct lookup to enforce object access.
        string normalizedReservationId = NormalizeObjectId(reservationId, nameof(reservationId));
        User actor = await LoadAndValidateActorAsync(
            actorNic,
            actorRoleClaim,
            [UserRole.Prosumer, UserRole.Backoffice, UserRole.GridOperator],
            session: null,
            cancellationToken);
        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(
                reservation => reservation.Id,
                normalizedReservationId),
            BuildReadScopeFilter(actor));
        EnergyReservation? reservation = await _context.Reservations
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);
        if (reservation is null)
        {
            throw new NotFoundException("The reservation does not exist.");
        }

        ReservationDisplayReferences references = await LoadDisplayReferencesAsync(
            [reservation],
            cancellationToken);
        references.Stations.TryGetValue(reservation.StationId, out SolarStationInfo? station);
        references.Slots.TryGetValue(reservation.SlotId, out EnergyBookingSlot? slot);
        return MapToResponse(reservation, actor, GetUtcNow(), station, slot);
    }

    public async Task<ReservationCreationResult> CreateOwnReservationAsync(
        string actorNic,
        string actorRoleClaim,
        CreateReservationRequestDto request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Derive ownership exclusively from the authenticated Prosumer claims.
        ArgumentNullException.ThrowIfNull(request);
        User actor = await LoadAndValidateActorAsync(
            actorNic,
            actorRoleClaim,
            [UserRole.Prosumer],
            session: null,
            cancellationToken);

        return await CreateReservationAsync(
            actor,
            actor.Nic,
            request.SlotId,
            request.RequestedEnergyKwh,
            idempotencyKey,
            "POST:/api/reservations",
            cancellationToken);
    }

    public async Task<ReservationCreationResult> CreateReservationForProsumerAsync(
        string actorNic,
        string actorRoleClaim,
        StaffCreateReservationRequestDto request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Permit only active staff to identify an eligible target Prosumer explicitly.
        ArgumentNullException.ThrowIfNull(request);
        User actor = await LoadAndValidateActorAsync(
            actorNic,
            actorRoleClaim,
            [UserRole.Backoffice, UserRole.GridOperator],
            session: null,
            cancellationToken);
        string targetProsumerNic = NormalizeNic(request.TargetProsumerNic, nameof(request.TargetProsumerNic));
        await LoadAndValidateTargetProsumerAsync(
            targetProsumerNic,
            session: null,
            cancellationToken);

        return await CreateReservationAsync(
            actor,
            targetProsumerNic,
            request.SlotId,
            request.RequestedEnergyKwh,
            idempotencyKey,
            "POST:/api/reservations/staff",
            cancellationToken);
    }

    public async Task<ReservationUpdateResult> UpdateReservationAsync(
        string actorNic,
        string actorRoleClaim,
        string reservationId,
        UpdateReservationRequestDto request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Update one authoritative reservation while serializing this Prosumer's schedule mutations.
        ArgumentNullException.ThrowIfNull(request);
        string normalizedReservationId = NormalizeObjectId(reservationId, nameof(reservationId));
        string normalizedSlotId = NormalizeObjectId(request.SlotId, nameof(request.SlotId));
        ValidatePositiveEnergy(request.RequestedEnergyKwh, nameof(request.RequestedEnergyKwh));
        string normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        User initialActor = await LoadAndValidateActorAsync(
            actorNic,
            actorRoleClaim,
            [UserRole.Prosumer, UserRole.Backoffice, UserRole.GridOperator],
            session: null,
            cancellationToken);
        EnergyReservation initialReservation = await LoadReservationAsync(
            normalizedReservationId,
            session: null,
            cancellationToken);
        EnsureUpdatePermission(initialActor, initialReservation);

        string requestIdHash = ComputeHash(
            $"{initialActor.Nic}|PUT|PUT:/api/reservations/{normalizedReservationId}|{normalizedKey}");
        string fingerprintHash = ComputeHash(
            $"{normalizedReservationId}|{normalizedSlotId}|" +
            request.RequestedEnergyKwh.ToString("G29", CultureInfo.InvariantCulture) +
            $"|{request.ExpectedVersion}");
        ReservationSchedulingLease lease = await _schedulingGuardService.AcquireAsync(
            initialReservation.ProsumerNic,
            cancellationToken);

        try
        {
            DateTime serverNowUtc = GetUtcNow();
            EnergyReservation originalReservation = await LoadReservationAsync(
                normalizedReservationId,
                session: null,
                cancellationToken);
            User actor = await LoadAndValidateActorAsync(
                initialActor.Nic,
                initialActor.Role.ToString(),
                [UserRole.Prosumer, UserRole.Backoffice, UserRole.GridOperator],
                session: null,
                cancellationToken);
            EnsureUpdatePermission(actor, originalReservation);

            if (string.Equals(
                    originalReservation.LastUpdateRequestIdHash,
                    requestIdHash,
                    StringComparison.Ordinal))
            {
                ValidateUpdateFingerprint(originalReservation, fingerprintHash);
                CapacityReconciliationResult reconciliation = await ReconcileCapacityAsync(
                    originalReservation.Id,
                    cancellationToken);
                if (!reconciliation.IsConsistent)
                {
                    throw new ConflictException(
                        "The reservation capacity state requires operator reconciliation.");
                }

                originalReservation = await LoadReservationAsync(
                    originalReservation.Id,
                    session: null,
                    cancellationToken);
                return new ReservationUpdateResult(
                    MapToResponse(originalReservation, actor, serverNowUtc),
                    IdempotencyReplayed: true);
            }

            EnsureExpectedVersion(originalReservation, request.ExpectedVersion);
            if (!StatusHoldsCapacity(originalReservation.Status))
            {
                throw new ConflictException(
                    "Only pending or approved reservations may be updated.");
            }

            if (originalReservation.CapacityState != ReservationCapacityState.Held)
            {
                throw new ConflictException(
                    "The reservation capacity state must be reconciled before it can be updated.");
            }

            EnsureMinimumUpdateNotice(originalReservation, serverNowUtc);
            bool sameSlot = string.Equals(
                originalReservation.SlotId,
                normalizedSlotId,
                StringComparison.Ordinal);
            bool materialChange = !sameSlot ||
                originalReservation.RequestedEnergyKwh != request.RequestedEnergyKwh;
            if (!materialChange)
            {
                return new ReservationUpdateResult(
                    MapToResponse(originalReservation, actor, serverNowUtc),
                    IdempotencyReplayed: false);
            }

            await LoadAndValidateTargetProsumerAsync(
                originalReservation.ProsumerNic,
                session: null,
                cancellationToken);
            EnergyBookingSlot targetSlot = await LoadSlotAsync(
                normalizedSlotId,
                session: null,
                cancellationToken);
            SolarStationInfo targetStation = await LoadStationAsync(
                targetSlot.StationId,
                session: null,
                cancellationToken);
            ValidateUpdateTarget(
                actor,
                originalReservation,
                targetStation,
                targetSlot,
                request.RequestedEnergyKwh,
                serverNowUtc,
                sameSlot);
            await ValidateNoDuplicateOrOverlapAsync(
                originalReservation.ProsumerNic,
                targetSlot.Id,
                targetSlot.StartTimeUtc,
                targetSlot.EndTimeUtc,
                originalReservation.Id,
                session: null,
                cancellationToken);

            EnergyReservation proposedReservation = BuildUpdatedReservation(
                originalReservation,
                actor,
                targetStation,
                targetSlot,
                request.RequestedEnergyKwh,
                requestIdHash,
                fingerprintHash,
                serverNowUtc);
            ConsistencyExecutionResult<EnergyReservation> execution =
                await RescheduleCapacitySafelyAsync(
                    originalReservation,
                    proposedReservation,
                    (session, token) => ReplaceReservationCompareAndSwapAsync(
                        originalReservation,
                        proposedReservation,
                        session,
                        token),
                    cancellationToken);

            return new ReservationUpdateResult(
                MapToResponse(execution.Value, actor, serverNowUtc),
                IdempotencyReplayed: false);
        }
        finally
        {
            await _schedulingGuardService.ReleaseAsync(lease, CancellationToken.None);
        }
    }

    public async Task<ReservationCancellationResult> CancelReservationAsync(
        string actorNic,
        string actorRoleClaim,
        string reservationId,
        CancelReservationRequestDto request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Cancel one authoritative reservation while serializing this Prosumer's schedule mutations.
        ArgumentNullException.ThrowIfNull(request);
        string normalizedReservationId = NormalizeObjectId(reservationId, nameof(reservationId));
        string normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        string? normalizedReason = NormalizeOptionalCancellationReason(request.Reason);
        User initialActor = await LoadAndValidateActorAsync(
            actorNic,
            actorRoleClaim,
            [UserRole.Prosumer, UserRole.Backoffice, UserRole.GridOperator],
            session: null,
            cancellationToken);
        EnergyReservation initialReservation = await LoadReservationAsync(
            normalizedReservationId,
            session: null,
            cancellationToken);
        EnsureCancellationPermission(initialActor, initialReservation);

        string requestIdHash = ComputeHash(
            $"{initialActor.Nic}|POST|POST:/api/reservations/{normalizedReservationId}/cancel|" +
            normalizedKey);
        string fingerprintHash = ComputeHash(
            $"{normalizedReservationId}|{request.ExpectedVersion}|{normalizedReason ?? string.Empty}");
        ReservationSchedulingLease lease = await _schedulingGuardService.AcquireAsync(
            initialReservation.ProsumerNic,
            cancellationToken);

        try
        {
            DateTime serverNowUtc = GetUtcNow();
            EnergyReservation originalReservation = await LoadReservationAsync(
                normalizedReservationId,
                session: null,
                cancellationToken);
            User actor = await LoadAndValidateActorAsync(
                initialActor.Nic,
                initialActor.Role.ToString(),
                [UserRole.Prosumer, UserRole.Backoffice, UserRole.GridOperator],
                session: null,
                cancellationToken);
            EnsureCancellationPermission(actor, originalReservation);

            if (string.Equals(
                    originalReservation.CancellationRequestIdHash,
                    requestIdHash,
                    StringComparison.Ordinal))
            {
                ValidateCancellationReplay(originalReservation, fingerprintHash);
                CapacityReconciliationResult reconciliation = await ReconcileCapacityAsync(
                    originalReservation.Id,
                    cancellationToken);
                if (!reconciliation.IsConsistent ||
                    reconciliation.CapacityState != ReservationCapacityState.Released)
                {
                    throw new ConflictException(
                        "The cancelled reservation capacity requires operator reconciliation.");
                }

                originalReservation = await LoadReservationAsync(
                    originalReservation.Id,
                    session: null,
                    cancellationToken);
                return new ReservationCancellationResult(
                    MapToResponse(originalReservation, actor, serverNowUtc),
                    IdempotencyReplayed: true);
            }

            EnsureExpectedVersion(originalReservation, request.ExpectedVersion);
            if (!StatusHoldsCapacity(originalReservation.Status))
            {
                throw new ConflictException(
                    "Only pending or approved reservations may be cancelled.");
            }

            if (originalReservation.CapacityState != ReservationCapacityState.Held)
            {
                throw new ConflictException(
                    "The reservation capacity state must be reconciled before it can be cancelled.");
            }

            EnsureMinimumCancellationNotice(originalReservation, serverNowUtc);
            EnsureTransitionAllowed(originalReservation.Status, ReservationStatus.Cancelled);
            EnergyReservation proposedReservation = BuildCancelledReservation(
                originalReservation,
                actor,
                normalizedReason,
                requestIdHash,
                fingerprintHash,
                serverNowUtc);
            ConsistencyExecutionResult<EnergyReservation> execution =
                await ReleaseCapacityForFinalTransitionSafelyAsync(
                    originalReservation,
                    proposedReservation,
                    (session, token) => PersistCancellationCompareAndSwapAsync(
                        originalReservation,
                        proposedReservation,
                        session,
                        token),
                    MarkCancellationReleasedAsync,
                    cancellationToken);

            return new ReservationCancellationResult(
                MapToResponse(execution.Value, actor, serverNowUtc),
                IdempotencyReplayed: false);
        }
        finally
        {
            await _schedulingGuardService.ReleaseAsync(lease, CancellationToken.None);
        }
    }

    public async Task<ReservationApprovalResult> ApproveReservationAsync(
        string actorNic,
        string actorRoleClaim,
        string reservationId,
        ApproveReservationRequestDto request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Approve one Pending reservation after transaction-scoped reference and held-claim validation.
        ArgumentNullException.ThrowIfNull(request);
        string normalizedReservationId = NormalizeObjectId(reservationId, nameof(reservationId));
        string normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        User initialActor = await LoadAndValidateActorAsync(
            actorNic,
            actorRoleClaim,
            [UserRole.Backoffice, UserRole.GridOperator],
            session: null,
            cancellationToken);
        EnergyReservation initialReservation = await LoadReservationAsync(
            normalizedReservationId,
            session: null,
            cancellationToken);
        EnsureApprovalPermission(initialActor, initialReservation);

        string requestIdHash = ComputeHash(
            $"{initialActor.Nic}|POST|POST:/api/reservations/{normalizedReservationId}/approve|" +
            normalizedKey);
        string fingerprintHash = ComputeHash(
            $"{normalizedReservationId}|{request.ExpectedVersion}");
        ReservationSchedulingLease lease = await _schedulingGuardService.AcquireAsync(
            initialReservation.ProsumerNic,
            cancellationToken);

        try
        {
            DateTime serverNowUtc = GetUtcNow();
            ConsistencyExecutionResult<ReservationDecisionEntityResult> execution =
                await _transactionRunner.ExecuteAsync(
                    (session, token) => ApproveReservationCoreAsync(
                        initialActor.Nic,
                        initialActor.Role,
                        normalizedReservationId,
                        request.ExpectedVersion,
                        requestIdHash,
                        fingerprintHash,
                        serverNowUtc,
                        session,
                        token),
                    token => ApproveReservationCoreAsync(
                        initialActor.Nic,
                        initialActor.Role,
                        normalizedReservationId,
                        request.ExpectedVersion,
                        requestIdHash,
                        fingerprintHash,
                        serverNowUtc,
                        session: null,
                        token),
                    cancellationToken);

            return new ReservationApprovalResult(
                MapToResponse(
                    execution.Value.Reservation,
                    execution.Value.Actor,
                    serverNowUtc),
                execution.Value.IdempotencyReplayed);
        }
        finally
        {
            await _schedulingGuardService.ReleaseAsync(lease, CancellationToken.None);
        }
    }

    public async Task<ReservationRejectionResult> RejectReservationAsync(
        string actorNic,
        string actorRoleClaim,
        string reservationId,
        RejectReservationRequestDto request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Reject one Pending reservation while serializing the exact capacity release and audit write.
        ArgumentNullException.ThrowIfNull(request);
        string normalizedReservationId = NormalizeObjectId(reservationId, nameof(reservationId));
        string normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        string normalizedReason = NormalizeRequiredRejectionReason(request.Reason);
        User initialActor = await LoadAndValidateActorAsync(
            actorNic,
            actorRoleClaim,
            [UserRole.Backoffice],
            session: null,
            cancellationToken);
        EnergyReservation initialReservation = await LoadReservationAsync(
            normalizedReservationId,
            session: null,
            cancellationToken);
        EnsureRejectionPermission(initialActor);

        string requestIdHash = ComputeHash(
            $"{initialActor.Nic}|POST|POST:/api/reservations/{normalizedReservationId}/reject|" +
            normalizedKey);
        string fingerprintHash = ComputeHash(
            $"{normalizedReservationId}|{request.ExpectedVersion}|{normalizedReason}");
        ReservationSchedulingLease lease = await _schedulingGuardService.AcquireAsync(
            initialReservation.ProsumerNic,
            cancellationToken);

        try
        {
            DateTime serverNowUtc = GetUtcNow();
            EnergyReservation originalReservation = await LoadReservationAsync(
                normalizedReservationId,
                session: null,
                cancellationToken);
            User actor = await LoadAndValidateActorAsync(
                initialActor.Nic,
                initialActor.Role.ToString(),
                [UserRole.Backoffice],
                session: null,
                cancellationToken);
            EnsureRejectionPermission(actor);

            if (string.Equals(
                    originalReservation.RejectionRequestIdHash,
                    requestIdHash,
                    StringComparison.Ordinal))
            {
                ValidateRejectionReplay(originalReservation, fingerprintHash);
                CapacityReconciliationResult reconciliation = await ReconcileCapacityAsync(
                    originalReservation.Id,
                    cancellationToken);
                if (!reconciliation.IsConsistent ||
                    reconciliation.CapacityState != ReservationCapacityState.Released)
                {
                    throw new ConflictException(
                        "The rejected reservation capacity requires operator reconciliation.");
                }

                originalReservation = await LoadReservationAsync(
                    originalReservation.Id,
                    session: null,
                    cancellationToken);
                return new ReservationRejectionResult(
                    MapToResponse(originalReservation, actor, serverNowUtc),
                    IdempotencyReplayed: true);
            }

            EnsureExpectedVersion(originalReservation, request.ExpectedVersion);
            EnsurePendingDecisionState(originalReservation, "rejected");
            EnsureDecisionTimeInFuture(originalReservation, serverNowUtc);
            EnsureTransitionAllowed(originalReservation.Status, ReservationStatus.Rejected);
            EnergyReservation proposedReservation = BuildRejectedReservation(
                originalReservation,
                actor,
                normalizedReason,
                requestIdHash,
                fingerprintHash,
                serverNowUtc);
            ConsistencyExecutionResult<EnergyReservation> execution =
                await ReleaseCapacityForFinalTransitionSafelyAsync(
                    originalReservation,
                    proposedReservation,
                    (session, token) => PersistRejectionCompareAndSwapAsync(
                        originalReservation,
                        proposedReservation,
                        session,
                        token),
                    MarkRejectionReleasedAsync,
                    cancellationToken);

            return new ReservationRejectionResult(
                MapToResponse(execution.Value, actor, serverNowUtc),
                IdempotencyReplayed: false);
        }
        finally
        {
            await _schedulingGuardService.ReleaseAsync(lease, CancellationToken.None);
        }
    }

    private async Task<ReservationDecisionEntityResult> ApproveReservationCoreAsync(
        string actorNic,
        UserRole actorRole,
        string reservationId,
        long expectedVersion,
        string requestIdHash,
        string fingerprintHash,
        DateTime serverNowUtc,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Re-read every approval dependency in the same transaction snapshot when transactions are available.
        User actor = await LoadAndValidateActorAsync(
            actorNic,
            actorRole.ToString(),
            [UserRole.Backoffice, UserRole.GridOperator],
            session,
            cancellationToken);
        EnergyReservation reservation = await LoadReservationAsync(
            reservationId,
            session,
            cancellationToken);
        EnsureApprovalPermission(actor, reservation);

        if (string.Equals(
                reservation.ApprovalRequestIdHash,
                requestIdHash,
                StringComparison.Ordinal))
        {
            ValidateApprovalReplay(reservation, fingerprintHash);
            return new ReservationDecisionEntityResult(
                reservation,
                actor,
                IdempotencyReplayed: true);
        }

        EnsureExpectedVersion(reservation, expectedVersion);
        EnsurePendingDecisionState(reservation, "approved");
        EnsureDecisionTimeInFuture(reservation, serverNowUtc);
        await ValidateApprovalReferencesAndCapacityAsync(
            reservation,
            serverNowUtc,
            session,
            cancellationToken);
        EnsureTransitionAllowed(reservation.Status, ReservationStatus.Approved);
        EnergyReservation approved = await PersistApprovalCompareAndSwapAsync(
            reservation,
            actor,
            requestIdHash,
            fingerprintHash,
            serverNowUtc,
            session,
            cancellationToken);
        return new ReservationDecisionEntityResult(
            approved,
            actor,
            IdempotencyReplayed: false);
    }

    private static void EnsureApprovalPermission(User actor, EnergyReservation reservation)
    {
        // Allow Backoffice globally and Grid Operators only for their assigned reservation station.
        if (actor.Role == UserRole.Backoffice)
        {
            return;
        }

        if (actor.Role == UserRole.GridOperator &&
            string.Equals(actor.AssignedStationId, reservation.StationId, StringComparison.Ordinal))
        {
            return;
        }

        throw new ForbiddenException("This staff user is not authorized to approve the reservation.");
    }

    private static void EnsureRejectionPermission(User actor)
    {
        // Keep rejection within the contract's Backoffice-only decision scope.
        if (actor.Role != UserRole.Backoffice)
        {
            throw new ForbiddenException("Only Backoffice users may reject reservations.");
        }
    }

    private static void EnsurePendingDecisionState(
        EnergyReservation reservation,
        string action)
    {
        // Require one Pending reservation with an intact held allocation before a staff decision.
        if (reservation.Status != ReservationStatus.Pending)
        {
            throw new ConflictException($"Only pending reservations may be {action}.");
        }

        if (reservation.CapacityState != ReservationCapacityState.Held)
        {
            throw new ConflictException(
                $"The reservation capacity state must be reconciled before it can be {action}.");
        }
    }

    private static void EnsureDecisionTimeInFuture(
        EnergyReservation reservation,
        DateTime serverNowUtc)
    {
        // Prevent approval or rejection once the stored reservation start has arrived.
        if (reservation.ScheduledStartTimeUtc <= serverNowUtc)
        {
            throw new ConflictException(
                "Reservations cannot be approved or rejected after their scheduled start.");
        }
    }

    private async Task ValidateApprovalReferencesAndCapacityAsync(
        EnergyReservation reservation,
        DateTime serverNowUtc,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Confirm active ownership, unchanged references, schedule validity, and one exact held claim.
        await LoadAndValidateTargetProsumerAsync(
            reservation.ProsumerNic,
            session,
            cancellationToken);
        ReservationReferenceSet references = await LoadAndValidateReferencesAsync(
            reservation,
            requireActiveStation: true,
            requireBookableSlot: false,
            session,
            cancellationToken);
        ValidateSlotFitsOperatingSchedule(
            references.Station,
            references.Slot.StartTimeUtc,
            references.Slot.EndTimeUtc);
        if (references.Slot.StartTimeUtc <= serverNowUtc)
        {
            throw new ConflictException("Only future reservations may be approved.");
        }

        if (references.Slot.StartTimeUtc > serverNowUtc.AddDays(_maximumBookingDaysAhead))
        {
            throw new ConflictException(
                $"Reservations cannot be approved more than {_maximumBookingDaysAhead} days ahead.");
        }

        if (references.Slot.AvailableCapacityKwh < 0 ||
            references.Slot.AvailableCapacityKwh > references.Slot.TotalCapacityKwh)
        {
            throw new ConflictException("The slot capacity ledger is invalid.");
        }

        IReadOnlyList<SlotCapacityClaimSnapshot> claims = await _capacityService.FindClaimsAsync(
            reservation.Id,
            session,
            cancellationToken);
        long expectedClaimVersion = reservation.CapacityClaimVersion > 0
            ? reservation.CapacityClaimVersion
            : reservation.Version;
        bool exactClaimHeld = claims.Count == 1 &&
            string.Equals(claims[0].SlotId, reservation.SlotId, StringComparison.Ordinal) &&
            claims[0].EnergyKwh == reservation.RequestedEnergyKwh &&
            claims[0].ReservationVersion == expectedClaimVersion;
        if (!exactClaimHeld)
        {
            throw new ConflictException(
                "The reservation does not have one valid held-capacity claim for approval.");
        }

        await ValidateNoDuplicateOrOverlapAsync(
            reservation.ProsumerNic,
            reservation.SlotId,
            reservation.ScheduledStartTimeUtc,
            reservation.ScheduledEndTimeUtc,
            reservation.Id,
            session,
            cancellationToken);
    }

    private async Task<EnergyReservation> PersistApprovalCompareAndSwapAsync(
        EnergyReservation originalReservation,
        User actor,
        string requestIdHash,
        string fingerprintHash,
        DateTime serverNowUtc,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Approve only the exact Pending version that still owns its held allocation.
        long resultingVersion = checked(originalReservation.Version + 1);
        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(item => item.Id, originalReservation.Id),
            Builders<EnergyReservation>.Filter.Eq(item => item.Version, originalReservation.Version),
            Builders<EnergyReservation>.Filter.Eq(item => item.Status, ReservationStatus.Pending),
            Builders<EnergyReservation>.Filter.Eq(item => item.SlotId, originalReservation.SlotId),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.RequestedEnergyKwh,
                originalReservation.RequestedEnergyKwh),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.CapacityState,
                ReservationCapacityState.Held));
        var transition = new ReservationStatusHistoryEntry
        {
            FromStatus = ReservationStatus.Pending,
            ToStatus = ReservationStatus.Approved,
            ChangedAtUtc = serverNowUtc,
            ActorNic = actor.Nic,
            ActorRole = actor.Role,
            Version = resultingVersion
        };
        UpdateDefinition<EnergyReservation> update = Builders<EnergyReservation>.Update.Combine(
            Builders<EnergyReservation>.Update.Set(item => item.Status, ReservationStatus.Approved),
            Builders<EnergyReservation>.Update.Set(item => item.Version, resultingVersion),
            Builders<EnergyReservation>.Update.Set(item => item.UpdatedAtUtc, serverNowUtc),
            Builders<EnergyReservation>.Update.Set(item => item.UpdatedByActorNic, actor.Nic),
            Builders<EnergyReservation>.Update.Set(item => item.ApprovedAtUtc, serverNowUtc),
            Builders<EnergyReservation>.Update.Set(item => item.ApprovedByActorNic, actor.Nic),
            Builders<EnergyReservation>.Update.Set(item => item.ApprovalRequestIdHash, requestIdHash),
            Builders<EnergyReservation>.Update.Set(
                item => item.ApprovalRequestFingerprintHash,
                fingerprintHash),
            Builders<EnergyReservation>.Update.Push(item => item.StatusHistory, transition));
        var options = new FindOneAndUpdateOptions<EnergyReservation>
        {
            ReturnDocument = ReturnDocument.After
        };
        EnergyReservation? approved = session is null
            ? await _context.Reservations.FindOneAndUpdateAsync(
                filter,
                update,
                options,
                cancellationToken)
            : await _context.Reservations.FindOneAndUpdateAsync(
                session,
                filter,
                update,
                options,
                cancellationToken);

        return approved ?? throw new ConflictException(
            "The reservation changed while it was being approved. Reload it and try again.");
    }

    private static EnergyReservation BuildRejectedReservation(
        EnergyReservation originalReservation,
        User actor,
        string reason,
        string requestIdHash,
        string fingerprintHash,
        DateTime serverNowUtc)
    {
        // Preserve immutable data and create one audited Pending-to-Rejected proposal.
        long resultingVersion = checked(originalReservation.Version + 1);
        var proposed = new EnergyReservation
        {
            Id = originalReservation.Id,
            ProsumerNic = originalReservation.ProsumerNic,
            StationId = originalReservation.StationId,
            SlotId = originalReservation.SlotId,
            ScheduledStartTimeUtc = originalReservation.ScheduledStartTimeUtc,
            ScheduledEndTimeUtc = originalReservation.ScheduledEndTimeUtc,
            RequestedEnergyKwh = originalReservation.RequestedEnergyKwh,
            Status = ReservationStatus.Rejected,
            Version = resultingVersion,
            CapacityState = ReservationCapacityState.ReleasePending,
            CapacityClaimVersion = originalReservation.CapacityClaimVersion,
            CreationRequestIdHash = originalReservation.CreationRequestIdHash,
            CreationRequestFingerprintHash = originalReservation.CreationRequestFingerprintHash,
            LastUpdateRequestIdHash = originalReservation.LastUpdateRequestIdHash,
            LastUpdateRequestFingerprintHash = originalReservation.LastUpdateRequestFingerprintHash,
            CancellationRequestIdHash = originalReservation.CancellationRequestIdHash,
            CancellationRequestFingerprintHash =
                originalReservation.CancellationRequestFingerprintHash,
            ApprovalRequestIdHash = originalReservation.ApprovalRequestIdHash,
            ApprovalRequestFingerprintHash = originalReservation.ApprovalRequestFingerprintHash,
            RejectionRequestIdHash = requestIdHash,
            RejectionRequestFingerprintHash = fingerprintHash,
            CreatedAtUtc = originalReservation.CreatedAtUtc,
            CreatedByActorNic = originalReservation.CreatedByActorNic,
            UpdatedAtUtc = serverNowUtc,
            UpdatedByActorNic = actor.Nic,
            ApprovedAtUtc = originalReservation.ApprovedAtUtc,
            ApprovedByActorNic = originalReservation.ApprovedByActorNic,
            RejectedAtUtc = serverNowUtc,
            RejectedByActorNic = actor.Nic,
            RejectionReason = reason,
            CancelledAtUtc = originalReservation.CancelledAtUtc,
            CancelledByActorNic = originalReservation.CancelledByActorNic,
            CancellationReason = originalReservation.CancellationReason,
            CompletedAtUtc = originalReservation.CompletedAtUtc,
            CompletedByActorNic = originalReservation.CompletedByActorNic,
            CompletedVerificationId = originalReservation.CompletedVerificationId,
            StatusHistory = originalReservation.StatusHistory
                .Select(CloneStatusHistoryEntry)
                .ToList()
        };
        proposed.StatusHistory.Add(new ReservationStatusHistoryEntry
        {
            FromStatus = ReservationStatus.Pending,
            ToStatus = ReservationStatus.Rejected,
            ChangedAtUtc = serverNowUtc,
            ActorNic = actor.Nic,
            ActorRole = actor.Role,
            Version = resultingVersion,
            Reason = reason
        });
        return proposed;
    }

    private static void EnsureCancellationPermission(User actor, EnergyReservation reservation)
    {
        // Enforce hidden owner scope plus global Backoffice and assigned-station Grid Operator scope.
        if (actor.Role == UserRole.Prosumer)
        {
            if (!string.Equals(actor.Nic, reservation.ProsumerNic, StringComparison.Ordinal))
            {
                throw new NotFoundException("The reservation does not exist.");
            }

            return;
        }

        if (actor.Role == UserRole.Backoffice)
        {
            return;
        }

        if (actor.Role == UserRole.GridOperator &&
            string.Equals(actor.AssignedStationId, reservation.StationId, StringComparison.Ordinal))
        {
            return;
        }

        throw new ForbiddenException(
            "This staff user is not authorized to cancel the reservation.");
    }

    private void EnsureMinimumCancellationNotice(
        EnergyReservation reservation,
        DateTime serverNowUtc)
    {
        // Treat the exact configured notice boundary as valid against the stored start time.
        TimeSpan remaining = reservation.ScheduledStartTimeUtc - serverNowUtc;
        if (remaining < TimeSpan.FromHours(_minimumChangeNoticeHours))
        {
            throw new ConflictException(
                $"Reservations require at least {_minimumChangeNoticeHours} hours' notice to cancel.");
        }
    }

    private static EnergyReservation BuildCancelledReservation(
        EnergyReservation originalReservation,
        User actor,
        string? reason,
        string requestIdHash,
        string fingerprintHash,
        DateTime serverNowUtc)
    {
        // Preserve immutable reservation data and create one server-owned cancellation transition.
        long resultingVersion = checked(originalReservation.Version + 1);
        var proposed = new EnergyReservation
        {
            Id = originalReservation.Id,
            ProsumerNic = originalReservation.ProsumerNic,
            StationId = originalReservation.StationId,
            SlotId = originalReservation.SlotId,
            ScheduledStartTimeUtc = originalReservation.ScheduledStartTimeUtc,
            ScheduledEndTimeUtc = originalReservation.ScheduledEndTimeUtc,
            RequestedEnergyKwh = originalReservation.RequestedEnergyKwh,
            Status = ReservationStatus.Cancelled,
            Version = resultingVersion,
            CapacityState = ReservationCapacityState.ReleasePending,
            CapacityClaimVersion = originalReservation.CapacityClaimVersion,
            CreationRequestIdHash = originalReservation.CreationRequestIdHash,
            CreationRequestFingerprintHash = originalReservation.CreationRequestFingerprintHash,
            LastUpdateRequestIdHash = originalReservation.LastUpdateRequestIdHash,
            LastUpdateRequestFingerprintHash = originalReservation.LastUpdateRequestFingerprintHash,
            CancellationRequestIdHash = requestIdHash,
            CancellationRequestFingerprintHash = fingerprintHash,
            ApprovalRequestIdHash = originalReservation.ApprovalRequestIdHash,
            ApprovalRequestFingerprintHash = originalReservation.ApprovalRequestFingerprintHash,
            RejectionRequestIdHash = originalReservation.RejectionRequestIdHash,
            RejectionRequestFingerprintHash = originalReservation.RejectionRequestFingerprintHash,
            CreatedAtUtc = originalReservation.CreatedAtUtc,
            CreatedByActorNic = originalReservation.CreatedByActorNic,
            UpdatedAtUtc = serverNowUtc,
            UpdatedByActorNic = actor.Nic,
            ApprovedAtUtc = originalReservation.ApprovedAtUtc,
            ApprovedByActorNic = originalReservation.ApprovedByActorNic,
            RejectedAtUtc = originalReservation.RejectedAtUtc,
            RejectedByActorNic = originalReservation.RejectedByActorNic,
            RejectionReason = originalReservation.RejectionReason,
            CancelledAtUtc = serverNowUtc,
            CancelledByActorNic = actor.Nic,
            CancellationReason = reason,
            CompletedAtUtc = originalReservation.CompletedAtUtc,
            CompletedByActorNic = originalReservation.CompletedByActorNic,
            CompletedVerificationId = originalReservation.CompletedVerificationId,
            StatusHistory = originalReservation.StatusHistory
                .Select(CloneStatusHistoryEntry)
                .ToList()
        };
        proposed.StatusHistory.Add(new ReservationStatusHistoryEntry
        {
            FromStatus = originalReservation.Status,
            ToStatus = ReservationStatus.Cancelled,
            ChangedAtUtc = serverNowUtc,
            ActorNic = actor.Nic,
            ActorRole = actor.Role,
            Version = resultingVersion,
            Reason = reason
        });
        return proposed;
    }

    private static void EnsureUpdatePermission(User actor, EnergyReservation reservation)
    {
        // Enforce owner visibility and the explicit Backoffice/assigned-Grid-Operator staff scopes.
        if (actor.Role == UserRole.Prosumer)
        {
            if (!string.Equals(actor.Nic, reservation.ProsumerNic, StringComparison.Ordinal))
            {
                throw new NotFoundException("The reservation does not exist.");
            }

            return;
        }

        if (actor.Role == UserRole.Backoffice)
        {
            return;
        }

        if (actor.Role == UserRole.GridOperator &&
            string.Equals(actor.AssignedStationId, reservation.StationId, StringComparison.Ordinal))
        {
            return;
        }

        throw new ForbiddenException(
            "This staff user is not authorized to update the reservation.");
    }

    private void EnsureMinimumUpdateNotice(
        EnergyReservation reservation,
        DateTime serverNowUtc)
    {
        // Measure the inclusive notice boundary against the existing start, never the proposed start.
        TimeSpan remaining = reservation.ScheduledStartTimeUtc - serverNowUtc;
        if (remaining < TimeSpan.FromHours(_minimumChangeNoticeHours))
        {
            throw new ConflictException(
                $"Reservations require at least {_minimumChangeNoticeHours} hours' notice to update.");
        }
    }

    private void ValidateUpdateTarget(
        User actor,
        EnergyReservation originalReservation,
        SolarStationInfo targetStation,
        EnergyBookingSlot targetSlot,
        decimal requestedEnergyKwh,
        DateTime serverNowUtc,
        bool sameSlot)
    {
        // Validate the destination snapshot and require availability only for new or increased capacity.
        if (!targetStation.IsActive)
        {
            throw new ConflictException("Reservations cannot be moved to an inactive station.");
        }

        if (!string.Equals(targetSlot.StationId, targetStation.Id, StringComparison.Ordinal))
        {
            throw new ConflictException("The selected slot does not belong to the selected station.");
        }

        ValidateStaffStationScope(actor, targetStation.Id);
        if (targetSlot.StartTimeUtc <= serverNowUtc)
        {
            throw new ConflictException("The selected slot must start in the future.");
        }

        if (targetSlot.StartTimeUtc > serverNowUtc.AddDays(_maximumBookingDaysAhead))
        {
            throw new ConflictException(
                $"Reservations cannot be scheduled more than {_maximumBookingDaysAhead} days ahead.");
        }

        if (requestedEnergyKwh > targetSlot.TotalCapacityKwh)
        {
            throw new ArgumentException(
                "Requested energy cannot exceed the slot's total energy capacity.",
                nameof(requestedEnergyKwh));
        }

        ValidateSlotFitsOperatingSchedule(
            targetStation,
            targetSlot.StartTimeUtc,
            targetSlot.EndTimeUtc);
        decimal requiredIncrease = sameSlot
            ? requestedEnergyKwh - originalReservation.RequestedEnergyKwh
            : requestedEnergyKwh;
        if (requiredIncrease > 0 && targetSlot.AvailabilityStatus != SlotAvailabilityStatus.Available)
        {
            throw new ConflictException(
                "The selected slot is not available for the requested reservation update.");
        }

        if (requiredIncrease > targetSlot.AvailableCapacityKwh)
        {
            throw new ConflictException("The selected slot does not have enough available energy.");
        }
    }

    private static EnergyReservation BuildUpdatedReservation(
        EnergyReservation originalReservation,
        User actor,
        SolarStationInfo targetStation,
        EnergyBookingSlot targetSlot,
        decimal requestedEnergyKwh,
        string requestIdHash,
        string fingerprintHash,
        DateTime serverNowUtc)
    {
        // Copy authoritative protected fields and apply only the requested slot and quantity changes.
        ReservationStatus resultingStatus = originalReservation.Status == ReservationStatus.Approved
            ? ReservationStatus.Pending
            : originalReservation.Status;
        long resultingVersion = checked(originalReservation.Version + 1);
        var proposed = new EnergyReservation
        {
            Id = originalReservation.Id,
            ProsumerNic = originalReservation.ProsumerNic,
            StationId = targetStation.Id,
            SlotId = targetSlot.Id,
            ScheduledStartTimeUtc = targetSlot.StartTimeUtc,
            ScheduledEndTimeUtc = targetSlot.EndTimeUtc,
            RequestedEnergyKwh = requestedEnergyKwh,
            Status = resultingStatus,
            Version = resultingVersion,
            CapacityState = ReservationCapacityState.Held,
            CapacityClaimVersion = resultingVersion,
            CreationRequestIdHash = originalReservation.CreationRequestIdHash,
            CreationRequestFingerprintHash = originalReservation.CreationRequestFingerprintHash,
            LastUpdateRequestIdHash = requestIdHash,
            LastUpdateRequestFingerprintHash = fingerprintHash,
            CancellationRequestIdHash = originalReservation.CancellationRequestIdHash,
            CancellationRequestFingerprintHash =
                originalReservation.CancellationRequestFingerprintHash,
            ApprovalRequestIdHash = originalReservation.ApprovalRequestIdHash,
            ApprovalRequestFingerprintHash = originalReservation.ApprovalRequestFingerprintHash,
            RejectionRequestIdHash = originalReservation.RejectionRequestIdHash,
            RejectionRequestFingerprintHash = originalReservation.RejectionRequestFingerprintHash,
            CreatedAtUtc = originalReservation.CreatedAtUtc,
            CreatedByActorNic = originalReservation.CreatedByActorNic,
            UpdatedAtUtc = serverNowUtc,
            UpdatedByActorNic = actor.Nic,
            ApprovedAtUtc = resultingStatus == ReservationStatus.Approved
                ? originalReservation.ApprovedAtUtc
                : null,
            ApprovedByActorNic = resultingStatus == ReservationStatus.Approved
                ? originalReservation.ApprovedByActorNic
                : null,
            RejectedAtUtc = originalReservation.RejectedAtUtc,
            RejectedByActorNic = originalReservation.RejectedByActorNic,
            RejectionReason = originalReservation.RejectionReason,
            CancelledAtUtc = originalReservation.CancelledAtUtc,
            CancelledByActorNic = originalReservation.CancelledByActorNic,
            CancellationReason = originalReservation.CancellationReason,
            CompletedAtUtc = originalReservation.CompletedAtUtc,
            CompletedByActorNic = originalReservation.CompletedByActorNic,
            CompletedVerificationId = originalReservation.CompletedVerificationId,
            StatusHistory = originalReservation.StatusHistory
                .Select(CloneStatusHistoryEntry)
                .ToList()
        };

        if (originalReservation.Status == ReservationStatus.Approved)
        {
            proposed.StatusHistory.Add(new ReservationStatusHistoryEntry
            {
                FromStatus = ReservationStatus.Approved,
                ToStatus = ReservationStatus.Pending,
                ChangedAtUtc = serverNowUtc,
                ActorNic = actor.Nic,
                ActorRole = actor.Role,
                Version = resultingVersion,
                Reason = "Material reservation update requires approval again."
            });
        }

        return proposed;
    }

    private static ReservationStatusHistoryEntry CloneStatusHistoryEntry(
        ReservationStatusHistoryEntry entry)
    {
        // Copy embedded audit entries so proposal construction cannot mutate the loaded document.
        return new ReservationStatusHistoryEntry
        {
            FromStatus = entry.FromStatus,
            ToStatus = entry.ToStatus,
            ChangedAtUtc = entry.ChangedAtUtc,
            ActorNic = entry.ActorNic,
            ActorRole = entry.ActorRole,
            Version = entry.Version,
            Reason = entry.Reason
        };
    }

    private async Task<EnergyReservation> ReplaceReservationCompareAndSwapAsync(
        EnergyReservation originalReservation,
        EnergyReservation proposedReservation,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Persist only server-derived mutable fields behind status/version/allocation compare-and-swap guards.
        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(item => item.Id, originalReservation.Id),
            Builders<EnergyReservation>.Filter.Eq(item => item.Version, originalReservation.Version),
            Builders<EnergyReservation>.Filter.Eq(item => item.Status, originalReservation.Status),
            Builders<EnergyReservation>.Filter.Eq(item => item.SlotId, originalReservation.SlotId),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.RequestedEnergyKwh,
                originalReservation.RequestedEnergyKwh),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.CapacityState,
                ReservationCapacityState.Held));
        var updates = new List<UpdateDefinition<EnergyReservation>>
        {
            Builders<EnergyReservation>.Update.Set(item => item.StationId, proposedReservation.StationId),
            Builders<EnergyReservation>.Update.Set(item => item.SlotId, proposedReservation.SlotId),
            Builders<EnergyReservation>.Update.Set(
                item => item.ScheduledStartTimeUtc,
                proposedReservation.ScheduledStartTimeUtc),
            Builders<EnergyReservation>.Update.Set(
                item => item.ScheduledEndTimeUtc,
                proposedReservation.ScheduledEndTimeUtc),
            Builders<EnergyReservation>.Update.Set(
                item => item.RequestedEnergyKwh,
                proposedReservation.RequestedEnergyKwh),
            Builders<EnergyReservation>.Update.Set(item => item.Status, proposedReservation.Status),
            Builders<EnergyReservation>.Update.Set(item => item.Version, proposedReservation.Version),
            Builders<EnergyReservation>.Update.Set(
                item => item.CapacityState,
                proposedReservation.CapacityState),
            Builders<EnergyReservation>.Update.Set(
                item => item.CapacityClaimVersion,
                proposedReservation.CapacityClaimVersion),
            Builders<EnergyReservation>.Update.Set(
                item => item.LastUpdateRequestIdHash,
                proposedReservation.LastUpdateRequestIdHash),
            Builders<EnergyReservation>.Update.Set(
                item => item.LastUpdateRequestFingerprintHash,
                proposedReservation.LastUpdateRequestFingerprintHash),
            Builders<EnergyReservation>.Update.Set(
                item => item.UpdatedAtUtc,
                proposedReservation.UpdatedAtUtc),
            Builders<EnergyReservation>.Update.Set(
                item => item.UpdatedByActorNic,
                proposedReservation.UpdatedByActorNic)
        };

        if (originalReservation.Status == ReservationStatus.Approved)
        {
            ReservationStatusHistoryEntry transition = proposedReservation.StatusHistory[^1];
            updates.Add(Builders<EnergyReservation>.Update.Unset(item => item.ApprovedAtUtc));
            updates.Add(Builders<EnergyReservation>.Update.Unset(item => item.ApprovedByActorNic));
            updates.Add(Builders<EnergyReservation>.Update.Push(item => item.StatusHistory, transition));
        }

        UpdateDefinition<EnergyReservation> update =
            Builders<EnergyReservation>.Update.Combine(updates);
        var options = new FindOneAndUpdateOptions<EnergyReservation>
        {
            ReturnDocument = ReturnDocument.After
        };
        EnergyReservation? updated = session is null
            ? await _context.Reservations.FindOneAndUpdateAsync(
                filter,
                update,
                options,
                cancellationToken)
            : await _context.Reservations.FindOneAndUpdateAsync(
                session,
                filter,
                update,
                options,
                cancellationToken);

        return updated ?? throw new ConflictException(
            "The reservation changed while it was being updated. Reload it and try again.");
    }

    private async Task<EnergyReservation> PersistCancellationCompareAndSwapAsync(
        EnergyReservation originalReservation,
        EnergyReservation proposedReservation,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Win cancellation only while the exact active version still owns its held allocation.
        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(item => item.Id, originalReservation.Id),
            Builders<EnergyReservation>.Filter.Eq(item => item.Version, originalReservation.Version),
            Builders<EnergyReservation>.Filter.Eq(item => item.Status, originalReservation.Status),
            Builders<EnergyReservation>.Filter.Eq(item => item.SlotId, originalReservation.SlotId),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.RequestedEnergyKwh,
                originalReservation.RequestedEnergyKwh),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.CapacityState,
                ReservationCapacityState.Held));
        ReservationStatusHistoryEntry transition = proposedReservation.StatusHistory[^1];
        UpdateDefinition<EnergyReservation> update = Builders<EnergyReservation>.Update.Combine(
            Builders<EnergyReservation>.Update.Set(
                item => item.Status,
                ReservationStatus.Cancelled),
            Builders<EnergyReservation>.Update.Set(item => item.Version, proposedReservation.Version),
            Builders<EnergyReservation>.Update.Set(
                item => item.CapacityState,
                ReservationCapacityState.ReleasePending),
            Builders<EnergyReservation>.Update.Set(
                item => item.CancellationRequestIdHash,
                proposedReservation.CancellationRequestIdHash),
            Builders<EnergyReservation>.Update.Set(
                item => item.CancellationRequestFingerprintHash,
                proposedReservation.CancellationRequestFingerprintHash),
            Builders<EnergyReservation>.Update.Set(
                item => item.UpdatedAtUtc,
                proposedReservation.UpdatedAtUtc),
            Builders<EnergyReservation>.Update.Set(
                item => item.UpdatedByActorNic,
                proposedReservation.UpdatedByActorNic),
            Builders<EnergyReservation>.Update.Set(
                item => item.CancelledAtUtc,
                proposedReservation.CancelledAtUtc),
            Builders<EnergyReservation>.Update.Set(
                item => item.CancelledByActorNic,
                proposedReservation.CancelledByActorNic),
            proposedReservation.CancellationReason is null
                ? Builders<EnergyReservation>.Update.Unset(item => item.CancellationReason)
                : Builders<EnergyReservation>.Update.Set(
                    item => item.CancellationReason,
                    proposedReservation.CancellationReason),
            Builders<EnergyReservation>.Update.Push(item => item.StatusHistory, transition));
        var options = new FindOneAndUpdateOptions<EnergyReservation>
        {
            ReturnDocument = ReturnDocument.After
        };
        EnergyReservation? cancelled = session is null
            ? await _context.Reservations.FindOneAndUpdateAsync(
                filter,
                update,
                options,
                cancellationToken)
            : await _context.Reservations.FindOneAndUpdateAsync(
                session,
                filter,
                update,
                options,
                cancellationToken);

        return cancelled ?? throw new ConflictException(
            "The reservation changed while it was being cancelled. Reload it and try again.");
    }

    private async Task<EnergyReservation> MarkCancellationReleasedAsync(
        EnergyReservation cancelledReservation,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Finalize release metadata only for the cancellation version that removed the claim.
        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(item => item.Id, cancelledReservation.Id),
            Builders<EnergyReservation>.Filter.Eq(item => item.Version, cancelledReservation.Version),
            Builders<EnergyReservation>.Filter.Eq(item => item.Status, ReservationStatus.Cancelled),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.CapacityState,
                ReservationCapacityState.ReleasePending),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.CancellationRequestIdHash,
                cancelledReservation.CancellationRequestIdHash));
        UpdateDefinition<EnergyReservation> update = Builders<EnergyReservation>.Update
            .Set(item => item.CapacityState, ReservationCapacityState.Released)
            .Set(item => item.CapacityClaimVersion, 0);
        var options = new FindOneAndUpdateOptions<EnergyReservation>
        {
            ReturnDocument = ReturnDocument.After
        };
        EnergyReservation? finalized = session is null
            ? await _context.Reservations.FindOneAndUpdateAsync(
                filter,
                update,
                options,
                cancellationToken)
            : await _context.Reservations.FindOneAndUpdateAsync(
                session,
                filter,
                update,
                options,
                cancellationToken);

        if (finalized is not null)
        {
            return finalized;
        }

        EnergyReservation current = await LoadReservationAsync(
            cancelledReservation.Id,
            session,
            cancellationToken);
        if (current.Status == ReservationStatus.Cancelled &&
            current.Version == cancelledReservation.Version &&
            current.CapacityState == ReservationCapacityState.Released &&
            string.Equals(
                current.CancellationRequestIdHash,
                cancelledReservation.CancellationRequestIdHash,
                StringComparison.Ordinal))
        {
            return current;
        }

        throw new ConflictException(
            "The reservation changed while capacity release was being finalized.");
    }

    private async Task<EnergyReservation> PersistRejectionCompareAndSwapAsync(
        EnergyReservation originalReservation,
        EnergyReservation proposedReservation,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Win rejection only while the exact Pending version still owns its held allocation.
        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(item => item.Id, originalReservation.Id),
            Builders<EnergyReservation>.Filter.Eq(item => item.Version, originalReservation.Version),
            Builders<EnergyReservation>.Filter.Eq(item => item.Status, ReservationStatus.Pending),
            Builders<EnergyReservation>.Filter.Eq(item => item.SlotId, originalReservation.SlotId),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.RequestedEnergyKwh,
                originalReservation.RequestedEnergyKwh),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.CapacityState,
                ReservationCapacityState.Held));
        ReservationStatusHistoryEntry transition = proposedReservation.StatusHistory[^1];
        UpdateDefinition<EnergyReservation> update = Builders<EnergyReservation>.Update.Combine(
            Builders<EnergyReservation>.Update.Set(item => item.Status, ReservationStatus.Rejected),
            Builders<EnergyReservation>.Update.Set(item => item.Version, proposedReservation.Version),
            Builders<EnergyReservation>.Update.Set(
                item => item.CapacityState,
                ReservationCapacityState.ReleasePending),
            Builders<EnergyReservation>.Update.Set(
                item => item.RejectionRequestIdHash,
                proposedReservation.RejectionRequestIdHash),
            Builders<EnergyReservation>.Update.Set(
                item => item.RejectionRequestFingerprintHash,
                proposedReservation.RejectionRequestFingerprintHash),
            Builders<EnergyReservation>.Update.Set(
                item => item.UpdatedAtUtc,
                proposedReservation.UpdatedAtUtc),
            Builders<EnergyReservation>.Update.Set(
                item => item.UpdatedByActorNic,
                proposedReservation.UpdatedByActorNic),
            Builders<EnergyReservation>.Update.Set(
                item => item.RejectedAtUtc,
                proposedReservation.RejectedAtUtc),
            Builders<EnergyReservation>.Update.Set(
                item => item.RejectedByActorNic,
                proposedReservation.RejectedByActorNic),
            Builders<EnergyReservation>.Update.Set(
                item => item.RejectionReason,
                proposedReservation.RejectionReason),
            Builders<EnergyReservation>.Update.Push(item => item.StatusHistory, transition));
        var options = new FindOneAndUpdateOptions<EnergyReservation>
        {
            ReturnDocument = ReturnDocument.After
        };
        EnergyReservation? rejected = session is null
            ? await _context.Reservations.FindOneAndUpdateAsync(
                filter,
                update,
                options,
                cancellationToken)
            : await _context.Reservations.FindOneAndUpdateAsync(
                session,
                filter,
                update,
                options,
                cancellationToken);

        return rejected ?? throw new ConflictException(
            "The reservation changed while it was being rejected. Reload it and try again.");
    }

    private async Task<EnergyReservation> MarkRejectionReleasedAsync(
        EnergyReservation rejectedReservation,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Finalize released capacity only for the rejection version that removed the exact claim.
        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(item => item.Id, rejectedReservation.Id),
            Builders<EnergyReservation>.Filter.Eq(item => item.Version, rejectedReservation.Version),
            Builders<EnergyReservation>.Filter.Eq(item => item.Status, ReservationStatus.Rejected),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.CapacityState,
                ReservationCapacityState.ReleasePending),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.RejectionRequestIdHash,
                rejectedReservation.RejectionRequestIdHash));
        UpdateDefinition<EnergyReservation> update = Builders<EnergyReservation>.Update
            .Set(item => item.CapacityState, ReservationCapacityState.Released)
            .Set(item => item.CapacityClaimVersion, 0);
        var options = new FindOneAndUpdateOptions<EnergyReservation>
        {
            ReturnDocument = ReturnDocument.After
        };
        EnergyReservation? finalized = session is null
            ? await _context.Reservations.FindOneAndUpdateAsync(
                filter,
                update,
                options,
                cancellationToken)
            : await _context.Reservations.FindOneAndUpdateAsync(
                session,
                filter,
                update,
                options,
                cancellationToken);

        if (finalized is not null)
        {
            return finalized;
        }

        EnergyReservation current = await LoadReservationAsync(
            rejectedReservation.Id,
            session,
            cancellationToken);
        if (current.Status == ReservationStatus.Rejected &&
            current.Version == rejectedReservation.Version &&
            current.CapacityState == ReservationCapacityState.Released &&
            string.Equals(
                current.RejectionRequestIdHash,
                rejectedReservation.RejectionRequestIdHash,
                StringComparison.Ordinal))
        {
            return current;
        }

        throw new ConflictException(
            "The reservation changed while rejected capacity release was being finalized.");
    }

    private static void ValidateUpdateFingerprint(
        EnergyReservation reservation,
        string fingerprintHash)
    {
        // Reject reuse of one update idempotency key for different slot, quantity, or version input.
        if (!string.Equals(
                reservation.LastUpdateRequestFingerprintHash,
                fingerprintHash,
                StringComparison.Ordinal))
        {
            throw new ConflictException(
                "The Idempotency-Key was already used with a different reservation update.");
        }
    }

    private static void ValidateCancellationReplay(
        EnergyReservation reservation,
        string fingerprintHash)
    {
        // Return only the matching completed cancellation when one retry key is reused.
        if (reservation.Status != ReservationStatus.Cancelled ||
            !string.Equals(
                reservation.CancellationRequestFingerprintHash,
                fingerprintHash,
                StringComparison.Ordinal))
        {
            throw new ConflictException(
                "The Idempotency-Key was already used with a different cancellation request.");
        }
    }

    private static void ValidateApprovalReplay(
        EnergyReservation reservation,
        string fingerprintHash)
    {
        // Return only the matching Approved result for a repeated approval key.
        if (reservation.Status != ReservationStatus.Approved ||
            !string.Equals(
                reservation.ApprovalRequestFingerprintHash,
                fingerprintHash,
                StringComparison.Ordinal))
        {
            throw new ConflictException(
                "The Idempotency-Key was already used with a different approval request.");
        }
    }

    private static void ValidateRejectionReplay(
        EnergyReservation reservation,
        string fingerprintHash)
    {
        // Return only the matching Rejected result for a repeated rejection key.
        if (reservation.Status != ReservationStatus.Rejected ||
            !string.Equals(
                reservation.RejectionRequestFingerprintHash,
                fingerprintHash,
                StringComparison.Ordinal))
        {
            throw new ConflictException(
                "The Idempotency-Key was already used with a different rejection request.");
        }
    }

    private async Task<ReservationCreationResult> CreateReservationAsync(
        User initialActor,
        string targetProsumerNic,
        string slotId,
        decimal requestedEnergyKwh,
        string idempotencyKey,
        string canonicalRoute,
        CancellationToken cancellationToken)
    {
        // Serialize this Prosumer's schedule while transaction or compensation creates the booking.
        string normalizedSlotId = NormalizeObjectId(slotId, nameof(slotId));
        ValidatePositiveEnergy(requestedEnergyKwh, nameof(requestedEnergyKwh));
        string normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        string requestIdHash = ComputeHash(
            $"{initialActor.Nic}|POST|{canonicalRoute}|{normalizedKey}");
        string fingerprintHash = ComputeHash(
            $"{targetProsumerNic}|{normalizedSlotId}|" +
            requestedEnergyKwh.ToString("G29", CultureInfo.InvariantCulture));
        ReservationSchedulingLease lease = await _schedulingGuardService.AcquireAsync(
            targetProsumerNic,
            cancellationToken);

        try
        {
            DateTime serverNowUtc = GetUtcNow();
            ConsistencyExecutionResult<ReservationCreationEntityResult> execution;
            try
            {
                execution = await _transactionRunner.ExecuteAsync(
                    (session, token) => CreateReservationCoreAsync(
                        initialActor.Nic,
                        initialActor.Role,
                        targetProsumerNic,
                        normalizedSlotId,
                        requestedEnergyKwh,
                        requestIdHash,
                        fingerprintHash,
                        serverNowUtc,
                        session,
                        useCompensation: false,
                        token),
                    token => CreateReservationCoreAsync(
                        initialActor.Nic,
                        initialActor.Role,
                        targetProsumerNic,
                        normalizedSlotId,
                        requestedEnergyKwh,
                        requestIdHash,
                        fingerprintHash,
                        serverNowUtc,
                        session: null,
                        useCompensation: true,
                        token),
                    cancellationToken);
            }
            catch (MongoWriteException exception)
                when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                EnergyReservation? conflictingCreation =
                    await FindByCreationRequestHashAsync(
                        requestIdHash,
                        session: null,
                        cancellationToken);
                if (conflictingCreation is not null)
                {
                    ValidateCreationFingerprint(conflictingCreation, fingerprintHash);
                    await ValidateCreationReferencesAsync(
                        conflictingCreation,
                        initialActor,
                        serverNowUtc,
                        session: null,
                        cancellationToken);
                    await ResumeCreationHoldAsync(
                        conflictingCreation,
                        session: null,
                        cancellationToken);
                    execution = new ConsistencyExecutionResult<ReservationCreationEntityResult>(
                        new ReservationCreationEntityResult(
                            conflictingCreation,
                            IdempotencyReplayed: true),
                        UsedTransaction: false,
                        UsedCompensation: true);
                }
                else
                {
                    throw new ConflictException(
                        "A duplicate or overlapping active reservation was created concurrently.");
                }
            }

            ReservationResponseDto response = MapToResponse(
                execution.Value.Reservation,
                initialActor,
                serverNowUtc);
            return new ReservationCreationResult(
                response,
                execution.Value.IdempotencyReplayed);
        }
        finally
        {
            await _schedulingGuardService.ReleaseAsync(lease, CancellationToken.None);
        }
    }

    private async Task<ReservationCreationEntityResult> CreateReservationCoreAsync(
        string actorNic,
        UserRole expectedActorRole,
        string targetProsumerNic,
        string slotId,
        decimal requestedEnergyKwh,
        string requestIdHash,
        string fingerprintHash,
        DateTime serverNowUtc,
        IClientSessionHandle? session,
        bool useCompensation,
        CancellationToken cancellationToken)
    {
        // Revalidate identities and either replay, resume, or atomically start one creation.
        User actor = await LoadAndValidateActorAsync(
            actorNic,
            expectedActorRole.ToString(),
            expectedActorRole == UserRole.Prosumer
                ? [UserRole.Prosumer]
                : [UserRole.Backoffice, UserRole.GridOperator],
            session,
            cancellationToken);
        EnergyReservation? existing = await FindByCreationRequestHashAsync(
            requestIdHash,
            session,
            cancellationToken);
        if (existing is not null)
        {
            ValidateCreationFingerprint(existing, fingerprintHash);
            if (existing.CapacityState != ReservationCapacityState.Held)
            {
                await LoadAndValidateTargetProsumerAsync(
                    targetProsumerNic,
                    session,
                    cancellationToken);
            }

            await ValidateCreationReferencesAsync(existing, actor, serverNowUtc, session, cancellationToken);
            await ResumeCreationHoldAsync(existing, session, cancellationToken);
            return new ReservationCreationEntityResult(existing, IdempotencyReplayed: true);
        }

        await LoadAndValidateTargetProsumerAsync(
            targetProsumerNic,
            session,
            cancellationToken);

        EnergyBookingSlot slot = await LoadSlotAsync(slotId, session, cancellationToken);
        SolarStationInfo station = await LoadStationAsync(slot.StationId, session, cancellationToken);
        ValidateCreationTarget(actor, station, slot, requestedEnergyKwh, serverNowUtc);
        await ValidateNoDuplicateOrOverlapAsync(
            targetProsumerNic,
            slot.Id,
            slot.StartTimeUtc,
            slot.EndTimeUtc,
            excludedReservationId: null,
            session,
            cancellationToken);

        EnergyReservation reservation = BuildPendingReservation(
            actor,
            targetProsumerNic,
            station,
            slot,
            requestedEnergyKwh,
            requestIdHash,
            fingerprintHash,
            serverNowUtc);

        try
        {
            await InsertReservationAsync(reservation, session, cancellationToken);
        }
        catch (MongoWriteException exception)
            when (useCompensation &&
                  exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            EnergyReservation? racedReplay = await FindByCreationRequestHashAsync(
                requestIdHash,
                session: null,
                cancellationToken);
            if (racedReplay is null)
            {
                throw new ConflictException(
                    "The prosumer already has an active reservation for this slot.");
            }

            ValidateCreationFingerprint(racedReplay, fingerprintHash);
            await ResumeCreationHoldAsync(racedReplay, session: null, cancellationToken);
            return new ReservationCreationEntityResult(racedReplay, IdempotencyReplayed: true);
        }

        try
        {
            await ResumeCreationHoldAsync(reservation, session, cancellationToken);
            return new ReservationCreationEntityResult(reservation, IdempotencyReplayed: false);
        }
        catch when (useCompensation)
        {
            await CleanupFailedCreationAsync(reservation, CancellationToken.None);
            throw;
        }
    }

    private async Task ResumeCreationHoldAsync(
        EnergyReservation reservation,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Resume a persisted provisional create and make the embedded slot claim exactly once.
        if (reservation.CapacityState == ReservationCapacityState.Held)
        {
            return;
        }

        if (reservation.CapacityState != ReservationCapacityState.HoldPending)
        {
            throw new ConflictException(
                "The previous creation request is not in a resumable capacity state.");
        }

        await _capacityService.HoldCapacityAsync(reservation, session, cancellationToken);
        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(item => item.Id, reservation.Id),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.CapacityState,
                ReservationCapacityState.HoldPending),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.CreationRequestIdHash,
                reservation.CreationRequestIdHash));
        UpdateDefinition<EnergyReservation> update = Builders<EnergyReservation>.Update
            .Set(item => item.CapacityState, ReservationCapacityState.Held)
            .Set(item => item.CapacityClaimVersion, reservation.Version);
        UpdateResult updateResult = session is null
            ? await _context.Reservations.UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            : await _context.Reservations.UpdateOneAsync(
                session,
                filter,
                update,
                cancellationToken: cancellationToken);

        if (updateResult.ModifiedCount == 0)
        {
            EnergyReservation persisted = await LoadReservationAsync(
                reservation.Id,
                session,
                cancellationToken);
            if (persisted.CapacityState != ReservationCapacityState.Held)
            {
                throw new ConflictException(
                    "The reservation creation state changed while capacity was being held.");
            }
        }

        reservation.CapacityState = ReservationCapacityState.Held;
        reservation.CapacityClaimVersion = reservation.Version;
    }

    private async Task CleanupFailedCreationAsync(
        EnergyReservation reservation,
        CancellationToken cancellationToken)
    {
        // Remove only an unallocated provisional reservation; otherwise retain it for safe retry repair.
        IReadOnlyList<SlotCapacityClaimSnapshot> claims = await _capacityService.FindClaimsAsync(
            reservation.Id,
            session: null,
            cancellationToken);
        if (claims.Count != 0)
        {
            return;
        }

        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(item => item.Id, reservation.Id),
            Builders<EnergyReservation>.Filter.Eq(
                item => item.CapacityState,
                ReservationCapacityState.HoldPending));
        await _context.Reservations.DeleteOneAsync(filter, cancellationToken);
    }

    private async Task<User> LoadAndValidateActorAsync(
        string actorNic,
        string roleClaim,
        IReadOnlyCollection<UserRole> allowedRoles,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Match authenticated claims to the current active user before authorizing a mutation.
        string normalizedNic = NormalizeNic(actorNic, nameof(actorNic));
        FilterDefinition<User> filter = Builders<User>.Filter.Eq(user => user.Nic, normalizedNic);
        User? actor = session is null
            ? await _context.Users.Find(filter).FirstOrDefaultAsync(cancellationToken)
            : await _context.Users.Find(session, filter).FirstOrDefaultAsync(cancellationToken);

        if (actor is null)
        {
            throw new UnauthorizedException("The authenticated user no longer exists.");
        }

        if (!Enum.TryParse(roleClaim, ignoreCase: false, out UserRole claimedRole) ||
            claimedRole != actor.Role)
        {
            throw new ForbiddenException("The authenticated role does not match the current user record.");
        }

        if (actor.Status != UserStatus.Active)
        {
            throw new ForbiddenException("Only active users may use reservation operations.");
        }

        if (!allowedRoles.Contains(actor.Role))
        {
            throw new ForbiddenException("This user role cannot perform this reservation action.");
        }

        return actor;
    }

    private async Task<User> LoadAndValidateTargetProsumerAsync(
        string targetProsumerNic,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Require the immutable reservation owner to be an existing active Prosumer.
        string normalizedNic = NormalizeNic(targetProsumerNic, nameof(targetProsumerNic));
        FilterDefinition<User> filter = Builders<User>.Filter.Eq(user => user.Nic, normalizedNic);
        User? prosumer = session is null
            ? await _context.Users.Find(filter).FirstOrDefaultAsync(cancellationToken)
            : await _context.Users.Find(session, filter).FirstOrDefaultAsync(cancellationToken);

        if (prosumer is null)
        {
            throw new NotFoundException("The target prosumer does not exist.");
        }

        if (prosumer.Role != UserRole.Prosumer || prosumer.Status != UserStatus.Active)
        {
            throw new ConflictException("The target user is not an eligible active prosumer.");
        }

        return prosumer;
    }

    private async Task ValidateCreationReferencesAsync(
        EnergyReservation reservation,
        User actor,
        DateTime serverNowUtc,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Recheck replayed references and actor station scope without reallocating capacity.
        if (reservation.CapacityState == ReservationCapacityState.Held)
        {
            ValidateStaffStationScope(actor, reservation.StationId);
            return;
        }

        EnergyBookingSlot slot = await LoadSlotAsync(reservation.SlotId, session, cancellationToken);
        SolarStationInfo station = await LoadStationAsync(reservation.StationId, session, cancellationToken);
        if (!string.Equals(slot.StationId, station.Id, StringComparison.Ordinal))
        {
            throw new ConflictException("The selected slot does not belong to the selected station.");
        }

        ValidateStaffStationScope(actor, station.Id);
        IReadOnlyList<SlotCapacityClaimSnapshot> claims =
            await _capacityService.FindClaimsAsync(
                reservation.Id,
                session,
                cancellationToken);
        SlotCapacityClaimSnapshot? canonicalClaim = claims.SingleOrDefault(
            claim => claim.SlotId == reservation.SlotId);
        if (canonicalClaim is null)
        {
            ValidateCreationTarget(actor, station, slot, reservation.RequestedEnergyKwh, serverNowUtc);
        }
        else if (canonicalClaim.ReservationVersion != reservation.Version ||
                 canonicalClaim.EnergyKwh != reservation.RequestedEnergyKwh)
        {
            throw new ConflictException(
                "The interrupted reservation capacity claim requires reconciliation.");
        }
    }

    private void ValidateCreationTarget(
        User actor,
        SolarStationInfo station,
        EnergyBookingSlot slot,
        decimal requestedEnergyKwh,
        DateTime serverNowUtc)
    {
        // Enforce station, slot, schedule, horizon, unit, and staff-scope rules from current data.
        if (!station.IsActive)
        {
            throw new ConflictException("Reservations cannot be created for an inactive station.");
        }

        if (!string.Equals(slot.StationId, station.Id, StringComparison.Ordinal))
        {
            throw new ConflictException("The selected slot does not belong to the selected station.");
        }

        ValidateStaffStationScope(actor, station.Id);
        if (slot.AvailabilityStatus != SlotAvailabilityStatus.Available)
        {
            throw new ConflictException("The selected slot is not available for reservation.");
        }

        if (slot.StartTimeUtc <= serverNowUtc)
        {
            throw new ConflictException("The selected slot must start in the future.");
        }

        if (slot.StartTimeUtc > serverNowUtc.AddDays(_maximumBookingDaysAhead))
        {
            throw new ConflictException(
                $"Reservations cannot be scheduled more than {_maximumBookingDaysAhead} days ahead.");
        }

        if (requestedEnergyKwh > slot.TotalCapacityKwh)
        {
            throw new ArgumentException(
                "Requested energy cannot exceed the slot's total energy capacity.",
                nameof(requestedEnergyKwh));
        }

        ValidateSlotFitsOperatingSchedule(station, slot.StartTimeUtc, slot.EndTimeUtc);
    }

    private static void ValidateStaffStationScope(User actor, string stationId)
    {
        // Limit Grid Operators to their assigned station while Backoffice remains globally scoped.
        if (actor.Role == UserRole.GridOperator &&
            !string.Equals(actor.AssignedStationId, stationId, StringComparison.Ordinal))
        {
            throw new ForbiddenException(
                "Grid Operators may create reservations only for their assigned station.");
        }
    }

    private async Task<EnergyBookingSlot> LoadSlotAsync(
        string slotId,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Resolve the selected Member 2 slot without accepting duplicated client schedule data.
        FilterDefinition<EnergyBookingSlot> filter =
            Builders<EnergyBookingSlot>.Filter.Eq(slot => slot.Id, slotId);
        EnergyBookingSlot? slot = session is null
            ? await _context.Slots.Find(filter).FirstOrDefaultAsync(cancellationToken)
            : await _context.Slots.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
        return slot ?? throw new NotFoundException("The selected slot does not exist.");
    }

    private async Task<SolarStationInfo> LoadStationAsync(
        string stationId,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Resolve the slot's current station so activation and schedule are server-owned.
        FilterDefinition<SolarStationInfo> filter =
            Builders<SolarStationInfo>.Filter.Eq(station => station.Id, stationId);
        SolarStationInfo? station = session is null
            ? await _context.Stations.Find(filter).FirstOrDefaultAsync(cancellationToken)
            : await _context.Stations.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
        return station ?? throw new NotFoundException("The selected station does not exist.");
    }

    private async Task<EnergyReservation?> FindByCreationRequestHashAsync(
        string requestIdHash,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Find the durable creation result for a scoped idempotency key without storing the key itself.
        FilterDefinition<EnergyReservation> filter =
            Builders<EnergyReservation>.Filter.Eq(
                reservation => reservation.CreationRequestIdHash,
                requestIdHash);
        return session is null
            ? await _context.Reservations.Find(filter).FirstOrDefaultAsync(cancellationToken)
            : await _context.Reservations.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task InsertReservationAsync(
        EnergyReservation reservation,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Persist the Pending reservation through the caller's transaction when one is available.
        if (session is null)
        {
            await _context.Reservations.InsertOneAsync(
                reservation,
                cancellationToken: cancellationToken);
            return;
        }

        await _context.Reservations.InsertOneAsync(
            session,
            reservation,
            cancellationToken: cancellationToken);
    }

    private static EnergyReservation BuildPendingReservation(
        User actor,
        string targetProsumerNic,
        SolarStationInfo station,
        EnergyBookingSlot slot,
        decimal requestedEnergyKwh,
        string requestIdHash,
        string fingerprintHash,
        DateTime serverNowUtc)
    {
        // Build server-owned identity, status, schedule snapshot, audit, and idempotency fields.
        var reservation = new EnergyReservation
        {
            Id = ObjectId.GenerateNewId().ToString(),
            ProsumerNic = targetProsumerNic,
            StationId = station.Id,
            SlotId = slot.Id,
            ScheduledStartTimeUtc = slot.StartTimeUtc,
            ScheduledEndTimeUtc = slot.EndTimeUtc,
            RequestedEnergyKwh = requestedEnergyKwh,
            Status = ReservationStatus.Pending,
            Version = 1,
            CapacityState = ReservationCapacityState.HoldPending,
            CapacityClaimVersion = 0,
            CreationRequestIdHash = requestIdHash,
            CreationRequestFingerprintHash = fingerprintHash,
            CreatedAtUtc = serverNowUtc,
            CreatedByActorNic = actor.Nic,
            UpdatedAtUtc = serverNowUtc,
            UpdatedByActorNic = actor.Nic
        };
        reservation.StatusHistory.Add(new ReservationStatusHistoryEntry
        {
            FromStatus = null,
            ToStatus = ReservationStatus.Pending,
            ChangedAtUtc = serverNowUtc,
            ActorNic = actor.Nic,
            ActorRole = actor.Role,
            Version = reservation.Version
        });
        return reservation;
    }

    private static FilterDefinition<EnergyReservation> BuildReadScopeFilter(User actor)
    {
        // Translate the current persisted role into a MongoDB ownership/station predicate.
        if (actor.Role == UserRole.GridOperator &&
            !ObjectId.TryParse(actor.AssignedStationId, out _))
        {
            throw new ForbiddenException(
                "The Grid Operator does not have a valid assigned station.");
        }

        return Builders<EnergyReservation>.Filter.Where(
            ReservationReadPolicy.BuildScopePredicate(actor));
    }

    private static FilterDefinition<EnergyReservation> BuildViewFilter(
        ReservationListView view,
        DateTime serverNowUtc)
    {
        // Keep pending/current/future/history definitions centralized for every client.
        return Builders<EnergyReservation>.Filter.Where(
            ReservationReadPolicy.BuildViewPredicate(view, serverNowUtc));
    }

    private async Task<FilterDefinition<EnergyReservation>> BuildSearchFilterAsync(
        string search,
        CancellationToken cancellationToken)
    {
        // Integrate Backoffice identifier and station-name search without widening actor scope.
        var pattern = new BsonRegularExpression(Regex.Escape(search), "i");
        List<string> matchingStationIds = await _context.Stations
            .Find(Builders<SolarStationInfo>.Filter.Or(
                Builders<SolarStationInfo>.Filter.Regex(station => station.Name, pattern),
                Builders<SolarStationInfo>.Filter.Regex(station => station.Address, pattern)))
            .Project(station => station.Id)
            .ToListAsync(cancellationToken);
        var searchFilters = new List<FilterDefinition<EnergyReservation>>
        {
            Builders<EnergyReservation>.Filter.Regex(
                reservation => reservation.ProsumerNic,
                pattern)
        };

        if (ObjectId.TryParse(search, out _))
        {
            searchFilters.Add(Builders<EnergyReservation>.Filter.Eq(
                reservation => reservation.Id,
                search));
            searchFilters.Add(Builders<EnergyReservation>.Filter.Eq(
                reservation => reservation.StationId,
                search));
            searchFilters.Add(Builders<EnergyReservation>.Filter.Eq(
                reservation => reservation.SlotId,
                search));
        }

        if (matchingStationIds.Count > 0)
        {
            searchFilters.Add(Builders<EnergyReservation>.Filter.In(
                reservation => reservation.StationId,
                matchingStationIds));
        }

        return Builders<EnergyReservation>.Filter.Or(searchFilters);
    }

    private async Task<ReservationDisplayReferences> LoadDisplayReferencesAsync(
        IReadOnlyCollection<EnergyReservation> reservations,
        CancellationToken cancellationToken)
    {
        // Batch station and slot display lookups so paged responses avoid per-item database calls.
        if (reservations.Count == 0)
        {
            return new ReservationDisplayReferences(
                new Dictionary<string, SolarStationInfo>(),
                new Dictionary<string, EnergyBookingSlot>());
        }

        string[] stationIds = reservations
            .Select(reservation => reservation.StationId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] slotIds = reservations
            .Select(reservation => reservation.SlotId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Task<List<SolarStationInfo>> stationTask = _context.Stations
            .Find(Builders<SolarStationInfo>.Filter.In(station => station.Id, stationIds))
            .ToListAsync(cancellationToken);
        Task<List<EnergyBookingSlot>> slotTask = _context.Slots
            .Find(Builders<EnergyBookingSlot>.Filter.In(slot => slot.Id, slotIds))
            .ToListAsync(cancellationToken);
        await Task.WhenAll(stationTask, slotTask);

        return new ReservationDisplayReferences(
            stationTask.Result.ToDictionary(station => station.Id, StringComparer.Ordinal),
            slotTask.Result.ToDictionary(slot => slot.Id, StringComparer.Ordinal));
    }

    private ReservationListItemResponseDto MapToListItem(
        EnergyReservation reservation,
        User actor,
        DateTime serverNowUtc,
        ReservationDisplayReferences references)
    {
        // Return a compact DTO with display metadata and the same action policy as details.
        references.Stations.TryGetValue(reservation.StationId, out SolarStationInfo? station);
        references.Slots.TryGetValue(reservation.SlotId, out EnergyBookingSlot? slot);
        return new ReservationListItemResponseDto
        {
            Id = reservation.Id,
            ProsumerNic = reservation.ProsumerNic,
            StationId = reservation.StationId,
            StationName = station?.Name,
            StationAddress = station?.Address,
            SlotId = reservation.SlotId,
            SlotAvailabilityStatus = slot?.AvailabilityStatus.ToString(),
            ScheduledStartTimeUtc = reservation.ScheduledStartTimeUtc,
            ScheduledEndTimeUtc = reservation.ScheduledEndTimeUtc,
            RequestedEnergyKwh = reservation.RequestedEnergyKwh,
            Status = reservation.Status.ToString(),
            Version = reservation.Version,
            QrEligible = reservation.Status == ReservationStatus.Approved,
            AllowedActions = BuildAllowedActions(reservation, actor, serverNowUtc),
            UpdatedAtUtc = reservation.UpdatedAtUtc
        };
    }

    private ReservationResponseDto MapToResponse(
        EnergyReservation reservation,
        User actor,
        DateTime serverNowUtc,
        SolarStationInfo? station = null,
        EnergyBookingSlot? slot = null)
    {
        // Project persisted state plus actor-scoped actions without exposing internal hashes or counters.
        return new ReservationResponseDto
        {
            Id = reservation.Id,
            ProsumerNic = reservation.ProsumerNic,
            StationId = reservation.StationId,
            StationName = station?.Name,
            StationAddress = station?.Address,
            SlotId = reservation.SlotId,
            SlotAvailabilityStatus = slot?.AvailabilityStatus.ToString(),
            ScheduledStartTimeUtc = reservation.ScheduledStartTimeUtc,
            ScheduledEndTimeUtc = reservation.ScheduledEndTimeUtc,
            RequestedEnergyKwh = reservation.RequestedEnergyKwh,
            Status = reservation.Status.ToString(),
            Version = reservation.Version,
            QrEligible = reservation.Status == ReservationStatus.Approved,
            AllowedActions = BuildAllowedActions(reservation, actor, serverNowUtc),
            CreatedAtUtc = reservation.CreatedAtUtc,
            CreatedByActorNic = reservation.CreatedByActorNic,
            UpdatedAtUtc = reservation.UpdatedAtUtc,
            UpdatedByActorNic = reservation.UpdatedByActorNic,
            ApprovedAtUtc = reservation.ApprovedAtUtc,
            ApprovedByActorNic = reservation.ApprovedByActorNic,
            RejectedAtUtc = reservation.RejectedAtUtc,
            RejectedByActorNic = reservation.RejectedByActorNic,
            RejectionReason = reservation.RejectionReason,
            CancelledAtUtc = reservation.CancelledAtUtc,
            CancelledByActorNic = reservation.CancelledByActorNic,
            CancellationReason = reservation.CancellationReason,
            CompletedAtUtc = reservation.CompletedAtUtc,
            CompletedByActorNic = reservation.CompletedByActorNic,
            CompletedVerificationId = reservation.CompletedVerificationId,
            StatusHistory = reservation.StatusHistory
                .Select(entry => new ReservationStatusHistoryEntryDto
                {
                    FromStatus = entry.FromStatus?.ToString(),
                    ToStatus = entry.ToStatus.ToString(),
                    ChangedAtUtc = entry.ChangedAtUtc,
                    ActorNic = entry.ActorNic,
                    ActorRole = entry.ActorRole.ToString(),
                    Version = entry.Version,
                    Reason = entry.Reason
                })
                .ToList()
        };
    }

    private ReservationAllowedActionsDto BuildAllowedActions(
        EnergyReservation reservation,
        User actor,
        DateTime serverNowUtc)
    {
        // Explain every unavailable actor-scoped action from current status, time, and role state.
        return ReservationReadPolicy.BuildAllowedActions(
            reservation,
            actor,
            serverNowUtc,
            _minimumChangeNoticeHours);
    }

    private static void ValidateCreationFingerprint(
        EnergyReservation reservation,
        string fingerprintHash)
    {
        // Reject reuse of one idempotency key for a different target, slot, or quantity.
        if (!string.Equals(
                reservation.CreationRequestFingerprintHash,
                fingerprintHash,
                StringComparison.Ordinal))
        {
            throw new ConflictException(
                "The Idempotency-Key was already used with a different reservation request.");
        }
    }

    private static void ValidateSlotFitsOperatingSchedule(
        SolarStationInfo station,
        DateTime startUtc,
        DateTime endUtc)
    {
        // Interpret Member 2's HH:mm station schedule in its documented Colombo time zone.
        DateTime localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc, OperatingScheduleTimeZone);
        DateTime localEnd = TimeZoneInfo.ConvertTimeFromUtc(endUtc, OperatingScheduleTimeZone);
        if (localStart.Date != localEnd.Date)
        {
            throw new ConflictException("The slot must fit within one local operating day.");
        }

        StationOperatingSchedule? schedule = station.OperatingSchedule.SingleOrDefault(
            item => item.DayOfWeek == localStart.DayOfWeek);
        if (schedule is null || !schedule.IsOpen)
        {
            throw new ConflictException("The station is closed during the selected slot.");
        }

        if (!TryParseScheduleTime(schedule.OpeningTime, out TimeOnly openingTime) ||
            !TryParseScheduleTime(schedule.ClosingTime, out TimeOnly closingTime))
        {
            throw new InvalidOperationException("The station operating schedule is invalid.");
        }

        TimeOnly slotStart = TimeOnly.FromDateTime(localStart);
        TimeOnly slotEnd = TimeOnly.FromDateTime(localEnd);
        if (slotStart < openingTime || slotEnd > closingTime)
        {
            throw new ConflictException("The slot must fit within the station operating schedule.");
        }
    }

    private static bool TryParseScheduleTime(string? value, out TimeOnly result)
    {
        // Parse the strict HH:mm wall-clock format used by Member 2 station schedules.
        return TimeOnly.TryParseExact(
            value,
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }

    private static string NormalizeNic(string? value, string parameterName)
    {
        // Normalize and validate the repository's Sri Lankan NIC identity format.
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
        // Normalize the repository's MongoDB ObjectId string representation.
        if (!ObjectId.TryParse(value?.Trim(), out ObjectId objectId))
        {
            throw new ArgumentException("A valid MongoDB ObjectId is required.", parameterName);
        }

        return objectId.ToString();
    }

    private static string? NormalizeOptionalSearch(string? value)
    {
        // Trim optional search text and retain the DTO's bounded public contract in the service.
        string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > 100)
        {
            throw new ArgumentException("Search cannot exceed 100 characters.", nameof(value));
        }

        return normalized;
    }

    private static void ValidatePositiveEnergy(decimal value, string parameterName)
    {
        // Enforce the positive kWh quantity supported by the existing slot capacity model.
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Requested energy must be greater than zero.");
        }
    }

    private static string NormalizeIdempotencyKey(string? value)
    {
        // Require a bounded printable retry key while retaining only its scoped hash.
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < MinimumIdempotencyKeyLength or > MaximumIdempotencyKeyLength ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"Idempotency-Key must contain {MinimumIdempotencyKeyLength} to " +
                $"{MaximumIdempotencyKeyLength} printable characters.",
                nameof(value));
        }

        return normalized;
    }

    private static string? NormalizeOptionalCancellationReason(string? value)
    {
        // Store a trimmed optional audit reason and enforce the DTO's maximum length in the domain layer.
        string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > MaximumAuditReasonLength)
        {
            throw new ArgumentException(
                $"Cancellation reason cannot exceed {MaximumAuditReasonLength} characters.",
                nameof(value));
        }

        return normalized;
    }

    private static string NormalizeRequiredRejectionReason(string? value)
    {
        // Require a trimmed bounded staff audit reason independently of controller model validation.
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Rejection reason is required.", nameof(value));
        }

        if (normalized.Length > MaximumAuditReasonLength)
        {
            throw new ArgumentException(
                $"Rejection reason cannot exceed {MaximumAuditReasonLength} characters.",
                nameof(value));
        }

        return normalized;
    }

    private static string ComputeHash(string value)
    {
        // Produce a stable lowercase SHA-256 digest without persisting the raw key or fingerprint.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    public async Task<EnergyReservation> LoadReservationAsync(
        string reservationId,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Load one reservation by the repository's ObjectId convention or return a domain 404.
        ValidateObjectId(reservationId, nameof(reservationId));
        FilterDefinition<EnergyReservation> filter =
            Builders<EnergyReservation>.Filter.Eq(reservation => reservation.Id, reservationId);

        EnergyReservation? reservation = session is null
            ? await _context.Reservations.Find(filter).FirstOrDefaultAsync(cancellationToken)
            : await _context.Reservations.Find(session, filter).FirstOrDefaultAsync(cancellationToken);

        return reservation ?? throw new NotFoundException("The reservation does not exist.");
    }

    public async Task<ReservationReferenceSet> LoadAndValidateReferencesAsync(
        EnergyReservation reservation,
        bool requireActiveStation,
        bool requireBookableSlot,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Resolve lightweight references and reject inactive, mismatched, stale, or invalid slot data.
        ArgumentNullException.ThrowIfNull(reservation);
        ValidateObjectId(reservation.StationId, nameof(reservation.StationId));
        ValidateObjectId(reservation.SlotId, nameof(reservation.SlotId));

        FilterDefinition<SolarStationInfo> stationFilter =
            Builders<SolarStationInfo>.Filter.Eq(station => station.Id, reservation.StationId);
        FilterDefinition<EnergyBookingSlot> slotFilter =
            Builders<EnergyBookingSlot>.Filter.Eq(slot => slot.Id, reservation.SlotId);

        SolarStationInfo? station = session is null
            ? await _context.Stations.Find(stationFilter).FirstOrDefaultAsync(cancellationToken)
            : await _context.Stations.Find(session, stationFilter).FirstOrDefaultAsync(cancellationToken);
        EnergyBookingSlot? slot = session is null
            ? await _context.Slots.Find(slotFilter).FirstOrDefaultAsync(cancellationToken)
            : await _context.Slots.Find(session, slotFilter).FirstOrDefaultAsync(cancellationToken);

        if (station is null)
        {
            throw new NotFoundException("The reservation station does not exist.");
        }

        if (slot is null)
        {
            throw new NotFoundException("The reservation slot does not exist.");
        }

        if (!string.Equals(slot.StationId, station.Id, StringComparison.Ordinal))
        {
            throw new ConflictException("The selected slot does not belong to the reservation station.");
        }

        if (reservation.ScheduledStartTimeUtc != slot.StartTimeUtc ||
            reservation.ScheduledEndTimeUtc != slot.EndTimeUtc)
        {
            throw new ConflictException(
                "The selected slot schedule changed and no longer matches the reservation snapshot.");
        }

        if (requireActiveStation && !station.IsActive)
        {
            throw new ConflictException("Reservations cannot be created for an inactive station.");
        }

        if (requireBookableSlot && slot.AvailabilityStatus != SlotAvailabilityStatus.Available)
        {
            throw new ConflictException("The selected slot is not available for reservation.");
        }

        if (requireBookableSlot && slot.StartTimeUtc <= GetUtcNow())
        {
            throw new ConflictException("Reservations require a slot that starts in the future.");
        }

        if (reservation.RequestedEnergyKwh <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservation.RequestedEnergyKwh),
                "Requested energy must be greater than zero.");
        }

        return new ReservationReferenceSet(reservation, station, slot);
    }

    public static bool StatusHoldsCapacity(ReservationStatus status)
    {
        // Use one active-status definition for allocation, overlap, and station checks.
        return status is ReservationStatus.Pending or ReservationStatus.Approved;
    }

    public async Task<CapacityMutationResult> HoldCapacityAsync(
        EnergyReservation reservation,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Validate references first, then delegate the atomic idempotent slot claim.
        if (!StatusHoldsCapacity(reservation.Status))
        {
            throw new ConflictException("Only pending or approved reservations may hold capacity.");
        }

        await LoadAndValidateReferencesAsync(
            reservation,
            requireActiveStation: true,
            requireBookableSlot: true,
            session,
            cancellationToken);

        return await _capacityService.HoldCapacityAsync(
            reservation,
            session,
            cancellationToken);
    }

    public async Task<CapacityMutationResult> ReleaseHeldCapacityAsync(
        EnergyReservation reservation,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Release only the exact claim stored for this reservation and make repeats harmless.
        ArgumentNullException.ThrowIfNull(reservation);
        return await _capacityService.ReleaseCapacityAsync(
            reservation.Id,
            reservation.SlotId,
            reservation.RequestedEnergyKwh,
            session,
            cancellationToken);
    }

    public async Task ValidateNoDuplicateOrOverlapAsync(
        string prosumerNic,
        string slotId,
        DateTime scheduledStartTimeUtc,
        DateTime scheduledEndTimeUtc,
        string? excludedReservationId,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Reject an active same-slot duplicate or any intersecting active reservation interval.
        if (string.IsNullOrWhiteSpace(prosumerNic))
        {
            throw new ArgumentException("Prosumer NIC is required.", nameof(prosumerNic));
        }

        ValidateObjectId(slotId, nameof(slotId));
        if (scheduledStartTimeUtc >= scheduledEndTimeUtc)
        {
            throw new ArgumentException("Scheduled start must be before scheduled end.");
        }

        var filters = new List<FilterDefinition<EnergyReservation>>
        {
            Builders<EnergyReservation>.Filter.Eq(
                reservation => reservation.ProsumerNic,
                prosumerNic.Trim().ToUpperInvariant()),
            Builders<EnergyReservation>.Filter.In(
                reservation => reservation.Status,
                CapacityHoldingStatuses),
            Builders<EnergyReservation>.Filter.Or(
                Builders<EnergyReservation>.Filter.Eq(
                    reservation => reservation.SlotId,
                    slotId),
                Builders<EnergyReservation>.Filter.And(
                    Builders<EnergyReservation>.Filter.Lt(
                        reservation => reservation.ScheduledStartTimeUtc,
                        scheduledEndTimeUtc),
                    Builders<EnergyReservation>.Filter.Gt(
                        reservation => reservation.ScheduledEndTimeUtc,
                        scheduledStartTimeUtc)))
        };

        if (excludedReservationId is not null)
        {
            ValidateObjectId(excludedReservationId, nameof(excludedReservationId));
            filters.Add(Builders<EnergyReservation>.Filter.Ne(
                reservation => reservation.Id,
                excludedReservationId));
        }

        FilterDefinition<EnergyReservation> filter =
            Builders<EnergyReservation>.Filter.And(filters);
        bool conflictExists = session is null
            ? await _context.Reservations.Find(filter).AnyAsync(cancellationToken)
            : await _context.Reservations.Find(session, filter).AnyAsync(cancellationToken);

        if (conflictExists)
        {
            throw new ConflictException(
                "The prosumer already has a duplicate or overlapping active reservation.");
        }
    }

    public static void EnsureTransitionAllowed(
        ReservationStatus currentStatus,
        ReservationStatus targetStatus)
    {
        // Enforce the contract lifecycle before any status or capacity mutation is attempted.
        if (currentStatus == targetStatus)
        {
            return;
        }

        bool allowed = currentStatus switch
        {
            ReservationStatus.Pending => targetStatus is
                ReservationStatus.Approved or
                ReservationStatus.Rejected or
                ReservationStatus.Cancelled,
            ReservationStatus.Approved => targetStatus is
                ReservationStatus.Pending or
                ReservationStatus.Cancelled or
                ReservationStatus.Completed,
            _ => false
        };

        if (!allowed)
        {
            throw new ConflictException(
                $"Reservation transition from {currentStatus} to {targetStatus} is not allowed.");
        }
    }

    public static void EnsureExpectedVersion(EnergyReservation reservation, long expectedVersion)
    {
        // Fail stale callers before they can perform a capacity or lifecycle mutation.
        ArgumentNullException.ThrowIfNull(reservation);
        if (expectedVersion < 1 || reservation.Version != expectedVersion)
        {
            throw new ConflictException("The reservation changed. Reload it and try again.");
        }
    }

    public async Task<bool> HasActiveReservationsForStationAsync(
        string stationId,
        DateTime serverNowUtc,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Apply the same pending/approved, not-ended definition used by capacity ownership.
        ValidateObjectId(stationId, nameof(stationId));
        DateTime normalizedNow = NormalizeUtc(serverNowUtc, nameof(serverNowUtc));

        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(
                reservation => reservation.StationId,
                stationId),
            Builders<EnergyReservation>.Filter.In(
                reservation => reservation.Status,
                CapacityHoldingStatuses),
            Builders<EnergyReservation>.Filter.Gt(
                reservation => reservation.ScheduledEndTimeUtc,
                normalizedNow));

        return session is null
            ? await _context.Reservations.Find(filter).AnyAsync(cancellationToken)
            : await _context.Reservations.Find(session, filter).AnyAsync(cancellationToken);
    }

    public async Task<ConsistencyExecutionResult<T>> ExecuteConsistentlyAsync<T>(
        Func<IClientSessionHandle, CancellationToken, Task<T>> transactionalWork,
        Func<CancellationToken, Task<T>> compensatingWork,
        CancellationToken cancellationToken)
    {
        // Route multi-document workflows through the shared transaction/fallback policy.
        return await _transactionRunner.ExecuteAsync(
            transactionalWork,
            compensatingWork,
            cancellationToken);
    }

    public async Task<ConsistencyExecutionResult<T>> RescheduleCapacitySafelyAsync<T>(
        EnergyReservation originalReservation,
        EnergyReservation proposedReservation,
        Func<IClientSessionHandle?, CancellationToken, Task<T>> persistReservationCompareAndSwap,
        CancellationToken cancellationToken)
    {
        // Coordinate capacity and the caller's version-filtered reservation update with rollback/compensation.
        ArgumentNullException.ThrowIfNull(originalReservation);
        ArgumentNullException.ThrowIfNull(proposedReservation);
        ArgumentNullException.ThrowIfNull(persistReservationCompareAndSwap);

        if (!string.Equals(originalReservation.Id, proposedReservation.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException("A reschedule must keep the same reservation identifier.");
        }

        if (proposedReservation.Version != originalReservation.Version + 1)
        {
            throw new ArgumentException("A reschedule must increment the reservation version exactly once.");
        }

        if (!StatusHoldsCapacity(originalReservation.Status) ||
            !StatusHoldsCapacity(proposedReservation.Status))
        {
            throw new ConflictException(
                "Only pending or approved reservations may be rescheduled while holding capacity.");
        }

        EnsureTransitionAllowed(originalReservation.Status, proposedReservation.Status);
        proposedReservation.CapacityState = ReservationCapacityState.Held;
        proposedReservation.CapacityClaimVersion = proposedReservation.Version;

        return await _transactionRunner.ExecuteAsync(
            async (session, token) =>
            {
                // Keep all target hold, reservation CAS, and old release writes in one transaction.
                await ValidateRescheduleTargetAsync(
                    originalReservation,
                    proposedReservation,
                    session,
                    token);
                await ApplyRescheduleCapacityAsync(
                    originalReservation,
                    proposedReservation,
                    session,
                    token);
                T value = await persistReservationCompareAndSwap(session, token);
                if (!string.Equals(
                        originalReservation.SlotId,
                        proposedReservation.SlotId,
                        StringComparison.Ordinal))
                {
                    await _capacityService.ReleaseCapacityAsync(
                        originalReservation.Id,
                        originalReservation.SlotId,
                        originalReservation.RequestedEnergyKwh,
                        session,
                        token);
                }

                return value;
            },
            async token =>
            {
                // Order fallback writes so failure retains at least the allocation represented by persisted state.
                await ValidateRescheduleTargetAsync(
                    originalReservation,
                    proposedReservation,
                    session: null,
                    token);

                bool sameSlot = string.Equals(
                    originalReservation.SlotId,
                    proposedReservation.SlotId,
                    StringComparison.Ordinal);
                bool prepareCapacityBeforePersistence = !sameSlot ||
                    proposedReservation.RequestedEnergyKwh > originalReservation.RequestedEnergyKwh;

                if (prepareCapacityBeforePersistence)
                {
                    await ApplyRescheduleCapacityAsync(
                        originalReservation,
                        proposedReservation,
                        session: null,
                        token);
                }

                T value;
                try
                {
                    value = await persistReservationCompareAndSwap(null, token);
                }
                catch
                {
                    await ReconcileCapacityAsync(originalReservation.Id, token);
                    throw;
                }

                try
                {
                    if (sameSlot && !prepareCapacityBeforePersistence)
                    {
                        await ApplyRescheduleCapacityAsync(
                            originalReservation,
                            proposedReservation,
                            session: null,
                            token);
                    }
                    else if (!sameSlot)
                    {
                        await _capacityService.ReleaseCapacityAsync(
                            originalReservation.Id,
                            originalReservation.SlotId,
                            originalReservation.RequestedEnergyKwh,
                            session: null,
                            token);
                    }
                }
                catch
                {
                    CapacityReconciliationResult reconciliation =
                        await ReconcileCapacityAsync(originalReservation.Id, token);
                    EnergyReservation persisted = await LoadReservationAsync(
                        originalReservation.Id,
                        session: null,
                        token);
                    bool proposedUpdatePersisted =
                        persisted.Version == proposedReservation.Version &&
                        string.Equals(
                            persisted.SlotId,
                            proposedReservation.SlotId,
                            StringComparison.Ordinal) &&
                        persisted.RequestedEnergyKwh == proposedReservation.RequestedEnergyKwh;
                    if (reconciliation.IsConsistent && proposedUpdatePersisted)
                    {
                        return value;
                    }

                    throw;
                }

                return value;
            },
            cancellationToken);
    }

    private async Task<ConsistencyExecutionResult<EnergyReservation>>
        ReleaseCapacityForFinalTransitionSafelyAsync(
        EnergyReservation originalReservation,
        EnergyReservation proposedReservation,
        Func<IClientSessionHandle?, CancellationToken, Task<EnergyReservation>>
            persistReservationCompareAndSwap,
        Func<EnergyReservation, IClientSessionHandle?, CancellationToken, Task<EnergyReservation>>
            finalizeRelease,
        CancellationToken cancellationToken)
    {
        // Commit a final status and exact claim release atomically, or reconcile an ordered fallback.
        ArgumentNullException.ThrowIfNull(originalReservation);
        ArgumentNullException.ThrowIfNull(proposedReservation);
        ArgumentNullException.ThrowIfNull(persistReservationCompareAndSwap);
        ArgumentNullException.ThrowIfNull(finalizeRelease);

        return await _transactionRunner.ExecuteAsync(
            async (session, token) =>
            {
                // Make one versioned final transition win before releasing its exact claim in the transaction.
                EnergyReservation transitioned = await persistReservationCompareAndSwap(session, token);
                CapacityMutationResult release = await ReleaseHeldCapacityAsync(
                    originalReservation,
                    session,
                    token);
                if (release.Outcome != CapacityMutationOutcome.Applied)
                {
                    throw new ConflictException(
                        "The reservation allocation was already absent and requires reconciliation.");
                }

                return await finalizeRelease(transitioned, session, token);
            },
            async token =>
            {
                // On standalone MongoDB, persist final status first so a competing transition cannot win.
                EnergyReservation transitioned = await persistReservationCompareAndSwap(null, token);
                try
                {
                    await ReleaseHeldCapacityAsync(originalReservation, session: null, token);
                    return await finalizeRelease(transitioned, null, token);
                }
                catch
                {
                    try
                    {
                        CapacityReconciliationResult reconciliation = await ReconcileCapacityAsync(
                            originalReservation.Id,
                            token);
                        EnergyReservation persisted = await LoadReservationAsync(
                            originalReservation.Id,
                            session: null,
                            token);
                        bool transitionPersisted =
                            persisted.Status == proposedReservation.Status &&
                            persisted.Version == proposedReservation.Version &&
                            persisted.CapacityState == ReservationCapacityState.Released;
                        if (reconciliation.IsConsistent &&
                            reconciliation.CapacityState == ReservationCapacityState.Released &&
                            transitionPersisted)
                        {
                            return persisted;
                        }
                    }
                    catch
                    {
                        // Preserve the original failure when best-effort reconciliation cannot finish.
                    }

                    throw;
                }
            },
            cancellationToken);
    }

    public async Task<CapacityReconciliationResult> ReconcileCapacityAsync(
        string reservationId,
        CancellationToken cancellationToken)
    {
        // Repair interrupted fallback workflows without allocating capacity speculatively.
        EnergyReservation reservation = await LoadReservationAsync(
            reservationId,
            session: null,
            cancellationToken);
        IReadOnlyList<SlotCapacityClaimSnapshot> claims = await _capacityService.FindClaimsAsync(
            reservation.Id,
            session: null,
            cancellationToken);

        bool mustRetainCanonicalClaim = StatusHoldsCapacity(reservation.Status) ||
                                        reservation.Status == ReservationStatus.Completed;
        SlotCapacityClaimSnapshot? canonicalClaim = claims.SingleOrDefault(
            claim => claim.SlotId == reservation.SlotId);
        int releasedStaleClaims = 0;

        foreach (SlotCapacityClaimSnapshot claim in claims.Where(
                     claim => !mustRetainCanonicalClaim || claim.SlotId != reservation.SlotId))
        {
            CapacityMutationResult release = await _capacityService.ReleaseCapacityAsync(
                reservation.Id,
                claim.SlotId,
                claim.EnergyKwh,
                session: null,
                cancellationToken);
            if (release.Outcome == CapacityMutationOutcome.Applied)
            {
                releasedStaleClaims++;
            }
        }

        ReservationCapacityState targetState;
        bool isConsistent;

        if (!mustRetainCanonicalClaim)
        {
            targetState = ReservationCapacityState.Released;
            isConsistent = true;
        }
        else if (canonicalClaim is not null)
        {
            long desiredClaimVersion = reservation.CapacityClaimVersion > 0
                ? reservation.CapacityClaimVersion
                : reservation.Version;

            if (canonicalClaim.EnergyKwh != reservation.RequestedEnergyKwh ||
                canonicalClaim.ReservationVersion != desiredClaimVersion)
            {
                CapacityMutationResult adjustment = await _capacityService.AdjustHeldCapacityAsync(
                    reservation.Id,
                    reservation.SlotId,
                    canonicalClaim.ReservationVersion,
                    canonicalClaim.EnergyKwh,
                    desiredClaimVersion,
                    reservation.RequestedEnergyKwh,
                    session: null,
                    cancellationToken);
                canonicalClaim = new SlotCapacityClaimSnapshot(
                    adjustment.Slot.Id,
                    reservation.Id,
                    desiredClaimVersion,
                    reservation.RequestedEnergyKwh);
            }

            targetState = reservation.Status == ReservationStatus.Completed
                ? ReservationCapacityState.Consumed
                : ReservationCapacityState.Held;
            isConsistent = true;
        }
        else
        {
            targetState = ReservationCapacityState.CompensationRequired;
            isConsistent = false;
        }

        await SetCapacityStateAsync(
            reservation,
            targetState,
            canonicalClaim?.ReservationVersion ?? 0,
            cancellationToken);

        return new CapacityReconciliationResult(
            reservation.Id,
            targetState,
            releasedStaleClaims,
            isConsistent);
    }

    public async Task CompensateFailedRescheduleAsync(
        string reservationId,
        string targetSlotId,
        decimal targetEnergyKwh,
        CancellationToken cancellationToken)
    {
        // Remove only the target claim created before a failed reservation compare-and-swap.
        await _capacityService.ReleaseCapacityAsync(
            reservationId,
            targetSlotId,
            targetEnergyKwh,
            session: null,
            cancellationToken);
    }

    private async Task ValidateRescheduleTargetAsync(
        EnergyReservation originalReservation,
        EnergyReservation proposedReservation,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Validate references and overlap before any target capacity claim is attempted.
        await LoadAndValidateReferencesAsync(
            proposedReservation,
            requireActiveStation: true,
            requireBookableSlot: false,
            session,
            cancellationToken);
        await ValidateNoDuplicateOrOverlapAsync(
            proposedReservation.ProsumerNic,
            proposedReservation.SlotId,
            proposedReservation.ScheduledStartTimeUtc,
            proposedReservation.ScheduledEndTimeUtc,
            originalReservation.Id,
            session,
            cancellationToken);
    }

    private async Task ApplyRescheduleCapacityAsync(
        EnergyReservation originalReservation,
        EnergyReservation proposedReservation,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Adjust one slot in place or idempotently hold the new slot before retiring the old claim.
        if (string.Equals(
                originalReservation.SlotId,
                proposedReservation.SlotId,
                StringComparison.Ordinal))
        {
            long claimVersion = originalReservation.CapacityClaimVersion > 0
                ? originalReservation.CapacityClaimVersion
                : originalReservation.Version;
            await _capacityService.AdjustHeldCapacityAsync(
                originalReservation.Id,
                originalReservation.SlotId,
                claimVersion,
                originalReservation.RequestedEnergyKwh,
                proposedReservation.Version,
                proposedReservation.RequestedEnergyKwh,
                session,
                cancellationToken);
            return;
        }

        await HoldCapacityAsync(proposedReservation, session, cancellationToken);
    }

    private async Task SetCapacityStateAsync(
        EnergyReservation reservation,
        ReservationCapacityState capacityState,
        long claimVersion,
        CancellationToken cancellationToken)
    {
        // Persist repair metadata only if the domain version has not changed concurrently.
        FilterDefinition<EnergyReservation> filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(item => item.Id, reservation.Id),
            Builders<EnergyReservation>.Filter.Eq(item => item.Version, reservation.Version));
        UpdateDefinition<EnergyReservation> update = Builders<EnergyReservation>.Update
            .Set(item => item.CapacityState, capacityState)
            .Set(item => item.CapacityClaimVersion, claimVersion);

        UpdateResult result = await _context.Reservations.UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        if (result.MatchedCount == 0)
        {
            throw new ConflictException(
                "The reservation changed while capacity was being reconciled.");
        }
    }

    private static DateTime NormalizeUtc(DateTime value, string parameterName)
    {
        // Require an explicit timestamp kind and compare all server-side times in UTC.
        if (value == default || value.Kind == DateTimeKind.Unspecified)
        {
            throw new ArgumentException("A timestamp with an explicit UTC kind is required.", parameterName);
        }

        return value.ToUniversalTime();
    }

    private DateTime GetUtcNow()
    {
        // Read reservation time from the injectable server clock for deterministic boundaries.
        return _timeProvider.GetUtcNow().UtcDateTime;
    }

    private static void ValidateObjectId(string? value, string parameterName)
    {
        // Enforce MongoDB ObjectId references before issuing any database query.
        if (!ObjectId.TryParse(value, out _))
        {
            throw new ArgumentException("A valid MongoDB ObjectId is required.", parameterName);
        }
    }
}
