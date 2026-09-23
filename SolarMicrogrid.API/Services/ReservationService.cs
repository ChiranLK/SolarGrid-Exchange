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

using MongoDB.Bson;
using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.Entities;

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

public sealed class ReservationService
{
    private static readonly ReservationStatus[] CapacityHoldingStatuses =
        [ReservationStatus.Pending, ReservationStatus.Approved];

    private readonly MongoDbContext _context;
    private readonly ReservationCapacityService _capacityService;
    private readonly MongoTransactionRunner _transactionRunner;

    public ReservationService(
        MongoDbContext context,
        ReservationCapacityService capacityService,
        MongoTransactionRunner transactionRunner)
    {
        // Reuse shared persistence and consistency services through dependency injection.
        _context = context;
        _capacityService = capacityService;
        _transactionRunner = transactionRunner;
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

        if (requireBookableSlot && slot.StartTimeUtc <= DateTime.UtcNow)
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
                    await ReconcileCapacityAsync(originalReservation.Id, token);
                    throw;
                }

                return value;
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

    private static void ValidateObjectId(string? value, string parameterName)
    {
        // Enforce MongoDB ObjectId references before issuing any database query.
        if (!ObjectId.TryParse(value, out _))
        {
            throw new ArgumentException("A valid MongoDB ObjectId is required.", parameterName);
        }
    }
}
