/*
 * ReservationCapacityService.cs
 * -----------------------------------------------------------------------------
 * Purpose : Provides the shared, retry-safe slot capacity primitives used by the
 *           reservation workflow for holds, adjustments, releases, and recovery.
 * Safety  : Every mutation is one conditional MongoDB document update. Embedded
 *           reservation claims make retries idempotent and releases exact-once.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Bson;
using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Services;

public enum CapacityMutationOutcome
{
    Applied,
    AlreadyApplied,
    AlreadyReleased
}

public sealed record CapacityMutationResult(
    CapacityMutationOutcome Outcome,
    EnergyBookingSlot Slot,
    decimal EnergyKwh);

public sealed record SlotCapacityClaimSnapshot(
    string SlotId,
    string ReservationId,
    long ReservationVersion,
    decimal EnergyKwh);

public sealed class ReservationCapacityService
{
    private const int MaximumCompareAndSwapAttempts = 8;

    private readonly MongoDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ReservationCapacityService(MongoDbContext context, TimeProvider timeProvider)
    {
        // Reuse the shared MongoDB context and Member 2 slot collection.
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<CapacityMutationResult> HoldCapacityAsync(
        EnergyReservation reservation,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Atomically add one reservation claim and decrement only currently available energy.
        ValidateReservationCapacityInput(reservation);

        for (int attempt = 0; attempt < MaximumCompareAndSwapAttempts; attempt++)
        {
            EnergyBookingSlot slot = await GetRequiredSlotAsync(
                reservation.SlotId,
                session,
                cancellationToken);

            SlotCapacityAllocation? existingClaim = GetSingleClaim(slot, reservation.Id);
            if (existingClaim is not null)
            {
                if (existingClaim.ReservationVersion == reservation.Version &&
                    existingClaim.EnergyKwh == reservation.RequestedEnergyKwh)
                {
                    return new CapacityMutationResult(
                        CapacityMutationOutcome.AlreadyApplied,
                        slot,
                        existingClaim.EnergyKwh);
                }

                throw new ConflictException(
                    "This reservation already holds a different allocation in the selected slot.");
            }

            if (!string.Equals(slot.StationId, reservation.StationId, StringComparison.Ordinal) ||
                slot.StartTimeUtc != reservation.ScheduledStartTimeUtc ||
                slot.EndTimeUtc != reservation.ScheduledEndTimeUtc)
            {
                throw new ConflictException(
                    "The selected slot station or schedule changed during reservation creation.");
            }

            if (slot.AvailabilityStatus != SlotAvailabilityStatus.Available)
            {
                throw new ConflictException("The selected slot is not available for reservation.");
            }

            if (slot.AvailableCapacityKwh < reservation.RequestedEnergyKwh)
            {
                throw new ConflictException("The selected slot does not have enough available energy.");
            }

            decimal newAvailableCapacity = slot.AvailableCapacityKwh - reservation.RequestedEnergyKwh;
            SlotAvailabilityStatus newStatus = CalculateAvailabilityStatus(
                slot.AvailabilityStatus,
                newAvailableCapacity);
            DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

            var claim = new SlotCapacityAllocation
            {
                ReservationId = reservation.Id,
                ReservationVersion = reservation.Version,
                EnergyKwh = reservation.RequestedEnergyKwh,
                AllocatedAtUtc = now
            };

            FilterDefinition<SlotCapacityAllocation> reservationClaimFilter =
                Builders<SlotCapacityAllocation>.Filter.Eq(
                    allocation => allocation.ReservationId,
                    reservation.Id);

            FilterDefinition<EnergyBookingSlot> updateFilter =
                Builders<EnergyBookingSlot>.Filter.And(
                    Builders<EnergyBookingSlot>.Filter.Eq(item => item.Id, reservation.SlotId),
                    Builders<EnergyBookingSlot>.Filter.Eq(
                        item => item.StationId,
                        reservation.StationId),
                    Builders<EnergyBookingSlot>.Filter.Eq(
                        item => item.StartTimeUtc,
                        reservation.ScheduledStartTimeUtc),
                    Builders<EnergyBookingSlot>.Filter.Eq(
                        item => item.EndTimeUtc,
                        reservation.ScheduledEndTimeUtc),
                    Builders<EnergyBookingSlot>.Filter.Eq(
                        item => item.AvailabilityStatus,
                        SlotAvailabilityStatus.Available),
                    Builders<EnergyBookingSlot>.Filter.Eq(
                        item => item.AvailableCapacityKwh,
                        slot.AvailableCapacityKwh),
                    Builders<EnergyBookingSlot>.Filter.Gte(
                        item => item.AvailableCapacityKwh,
                        reservation.RequestedEnergyKwh),
                    Builders<EnergyBookingSlot>.Filter.Not(
                        Builders<EnergyBookingSlot>.Filter.ElemMatch(
                            item => item.CapacityAllocations,
                            reservationClaimFilter)));

            UpdateDefinition<EnergyBookingSlot> update = Builders<EnergyBookingSlot>.Update
                .Set(item => item.AvailableCapacityKwh, newAvailableCapacity)
                .Set(item => item.AvailabilityStatus, newStatus)
                .Set(item => item.UpdatedAtUtc, now)
                .Push(item => item.CapacityAllocations, claim);

            EnergyBookingSlot? updatedSlot = await FindOneAndUpdateSlotAsync(
                updateFilter,
                update,
                session,
                cancellationToken);

            if (updatedSlot is not null)
            {
                return new CapacityMutationResult(
                    CapacityMutationOutcome.Applied,
                    updatedSlot,
                    reservation.RequestedEnergyKwh);
            }
        }

        throw new ConflictException(
            "Slot capacity changed repeatedly. Reload the slot and try the reservation again.");
    }

    public async Task<CapacityMutationResult> AdjustHeldCapacityAsync(
        string reservationId,
        string slotId,
        long expectedClaimVersion,
        decimal expectedEnergyKwh,
        long newClaimVersion,
        decimal newEnergyKwh,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Atomically replace an existing claim and apply only its energy delta.
        ValidateObjectId(reservationId, nameof(reservationId));
        ValidateObjectId(slotId, nameof(slotId));
        ValidatePositiveEnergy(expectedEnergyKwh, nameof(expectedEnergyKwh));
        ValidatePositiveEnergy(newEnergyKwh, nameof(newEnergyKwh));

        for (int attempt = 0; attempt < MaximumCompareAndSwapAttempts; attempt++)
        {
            EnergyBookingSlot slot = await GetRequiredSlotAsync(slotId, session, cancellationToken);
            SlotCapacityAllocation? claim = GetSingleClaim(slot, reservationId);

            if (claim is null)
            {
                throw new ConflictException("The reservation no longer holds capacity in this slot.");
            }

            if (claim.ReservationVersion == newClaimVersion && claim.EnergyKwh == newEnergyKwh)
            {
                return new CapacityMutationResult(
                    CapacityMutationOutcome.AlreadyApplied,
                    slot,
                    claim.EnergyKwh);
            }

            if (claim.ReservationVersion != expectedClaimVersion || claim.EnergyKwh != expectedEnergyKwh)
            {
                throw new ConflictException(
                    "The reservation capacity claim changed. Reload the reservation and try again.");
            }

            decimal capacityDelta = newEnergyKwh - claim.EnergyKwh;
            if (capacityDelta > 0 && slot.AvailabilityStatus != SlotAvailabilityStatus.Available)
            {
                throw new ConflictException("The selected slot is not available for more energy.");
            }

            if (capacityDelta > 0 && slot.AvailableCapacityKwh < capacityDelta)
            {
                throw new ConflictException("The selected slot does not have enough available energy.");
            }

            decimal newAvailableCapacity = slot.AvailableCapacityKwh - capacityDelta;
            if (newAvailableCapacity < 0 || newAvailableCapacity > slot.TotalCapacityKwh)
            {
                throw new InvalidOperationException("The stored slot capacity values are inconsistent.");
            }

            SlotAvailabilityStatus newStatus = CalculateAvailabilityStatus(
                slot.AvailabilityStatus,
                newAvailableCapacity);

            FilterDefinition<SlotCapacityAllocation> exactClaimFilter = BuildExactClaimFilter(
                reservationId,
                expectedClaimVersion,
                expectedEnergyKwh);

            FilterDefinition<EnergyBookingSlot> updateFilter =
                Builders<EnergyBookingSlot>.Filter.And(
                    Builders<EnergyBookingSlot>.Filter.Eq(item => item.Id, slotId),
                    Builders<EnergyBookingSlot>.Filter.Eq(
                        item => item.AvailableCapacityKwh,
                        slot.AvailableCapacityKwh),
                    Builders<EnergyBookingSlot>.Filter.ElemMatch(
                        item => item.CapacityAllocations,
                        exactClaimFilter));

            DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
            UpdateDefinition<EnergyBookingSlot> update = Builders<EnergyBookingSlot>.Update
                .Set(item => item.AvailableCapacityKwh, newAvailableCapacity)
                .Set(item => item.AvailabilityStatus, newStatus)
                .Set(item => item.UpdatedAtUtc, now)
                .Set("capacity_allocations.$.energy_kwh", newEnergyKwh)
                .Set("capacity_allocations.$.reservation_version", newClaimVersion);

            EnergyBookingSlot? updatedSlot = await FindOneAndUpdateSlotAsync(
                updateFilter,
                update,
                session,
                cancellationToken);

            if (updatedSlot is not null)
            {
                return new CapacityMutationResult(
                    CapacityMutationOutcome.Applied,
                    updatedSlot,
                    newEnergyKwh);
            }
        }

        throw new ConflictException(
            "Slot capacity changed repeatedly. Reload the slot and try the reservation again.");
    }

    public async Task<CapacityMutationResult> ReleaseCapacityAsync(
        string reservationId,
        string slotId,
        decimal? expectedEnergyKwh,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Atomically remove the reservation claim and restore exactly the energy recorded by that claim.
        ValidateObjectId(reservationId, nameof(reservationId));
        ValidateObjectId(slotId, nameof(slotId));
        if (expectedEnergyKwh.HasValue)
        {
            ValidatePositiveEnergy(expectedEnergyKwh.Value, nameof(expectedEnergyKwh));
        }

        for (int attempt = 0; attempt < MaximumCompareAndSwapAttempts; attempt++)
        {
            EnergyBookingSlot slot = await GetRequiredSlotAsync(slotId, session, cancellationToken);
            SlotCapacityAllocation? claim = GetSingleClaim(slot, reservationId);

            if (claim is null)
            {
                return new CapacityMutationResult(
                    CapacityMutationOutcome.AlreadyReleased,
                    slot,
                    0);
            }

            if (expectedEnergyKwh.HasValue && claim.EnergyKwh != expectedEnergyKwh.Value)
            {
                throw new ConflictException(
                    "The held capacity differs from the reservation. Reconciliation is required.");
            }

            decimal newAvailableCapacity = slot.AvailableCapacityKwh + claim.EnergyKwh;
            if (newAvailableCapacity > slot.TotalCapacityKwh)
            {
                throw new InvalidOperationException("Releasing the claim would exceed total slot capacity.");
            }

            SlotAvailabilityStatus newStatus = CalculateAvailabilityStatus(
                slot.AvailabilityStatus,
                newAvailableCapacity);
            FilterDefinition<SlotCapacityAllocation> exactClaimFilter = BuildExactClaimFilter(
                reservationId,
                claim.ReservationVersion,
                claim.EnergyKwh);

            FilterDefinition<EnergyBookingSlot> updateFilter =
                Builders<EnergyBookingSlot>.Filter.And(
                    Builders<EnergyBookingSlot>.Filter.Eq(item => item.Id, slotId),
                    Builders<EnergyBookingSlot>.Filter.Eq(
                        item => item.AvailableCapacityKwh,
                        slot.AvailableCapacityKwh),
                    Builders<EnergyBookingSlot>.Filter.ElemMatch(
                        item => item.CapacityAllocations,
                        exactClaimFilter));

            DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
            UpdateDefinition<EnergyBookingSlot> update = Builders<EnergyBookingSlot>.Update
                .Set(item => item.AvailableCapacityKwh, newAvailableCapacity)
                .Set(item => item.AvailabilityStatus, newStatus)
                .Set(item => item.UpdatedAtUtc, now)
                .PullFilter(item => item.CapacityAllocations, exactClaimFilter);

            EnergyBookingSlot? updatedSlot = await FindOneAndUpdateSlotAsync(
                updateFilter,
                update,
                session,
                cancellationToken);

            if (updatedSlot is not null)
            {
                return new CapacityMutationResult(
                    CapacityMutationOutcome.Applied,
                    updatedSlot,
                    claim.EnergyKwh);
            }
        }

        throw new ConflictException(
            "Slot capacity changed repeatedly. Reload the reservation and try again.");
    }

    public async Task<IReadOnlyList<SlotCapacityClaimSnapshot>> FindClaimsAsync(
        string reservationId,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Locate every persisted claim for compensation and stale-reschedule cleanup.
        ValidateObjectId(reservationId, nameof(reservationId));
        FilterDefinition<SlotCapacityAllocation> claimFilter =
            Builders<SlotCapacityAllocation>.Filter.Eq(
                allocation => allocation.ReservationId,
                reservationId);
        FilterDefinition<EnergyBookingSlot> slotFilter =
            Builders<EnergyBookingSlot>.Filter.ElemMatch(
                slot => slot.CapacityAllocations,
                claimFilter);

        List<EnergyBookingSlot> slots = await FindSlotsAsync(
            slotFilter,
            session,
            cancellationToken);

        return slots
            .SelectMany(slot => slot.CapacityAllocations
                .Where(allocation => allocation.ReservationId == reservationId)
                .Select(allocation => new SlotCapacityClaimSnapshot(
                    slot.Id,
                    allocation.ReservationId,
                    allocation.ReservationVersion,
                    allocation.EnergyKwh)))
            .ToList();
    }

    private async Task<EnergyBookingSlot> GetRequiredSlotAsync(
        string slotId,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Load one slot through either a transaction session or the normal collection path.
        ValidateObjectId(slotId, nameof(slotId));
        FilterDefinition<EnergyBookingSlot> filter =
            Builders<EnergyBookingSlot>.Filter.Eq(slot => slot.Id, slotId);

        EnergyBookingSlot? slot = session is null
            ? await _context.Slots.Find(filter).FirstOrDefaultAsync(cancellationToken)
            : await _context.Slots.Find(session, filter).FirstOrDefaultAsync(cancellationToken);

        return slot ?? throw new NotFoundException("The selected slot does not exist.");
    }

    private async Task<EnergyBookingSlot?> FindOneAndUpdateSlotAsync(
        FilterDefinition<EnergyBookingSlot> filter,
        UpdateDefinition<EnergyBookingSlot> update,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Execute one compare-and-swap mutation and return its post-update document.
        var options = new FindOneAndUpdateOptions<EnergyBookingSlot>
        {
            ReturnDocument = ReturnDocument.After
        };

        return session is null
            ? await _context.Slots.FindOneAndUpdateAsync(
                filter,
                update,
                options,
                cancellationToken)
            : await _context.Slots.FindOneAndUpdateAsync(
                session,
                filter,
                update,
                options,
                cancellationToken);
    }

    private async Task<List<EnergyBookingSlot>> FindSlotsAsync(
        FilterDefinition<EnergyBookingSlot> filter,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        // Query allocation-bearing slots consistently with the caller's session choice.
        return session is null
            ? await _context.Slots.Find(filter).ToListAsync(cancellationToken)
            : await _context.Slots.Find(session, filter).ToListAsync(cancellationToken);
    }

    private static FilterDefinition<SlotCapacityAllocation> BuildExactClaimFilter(
        string reservationId,
        long reservationVersion,
        decimal energyKwh)
    {
        // Match the exact persisted claim so a stale release cannot remove newer capacity.
        return Builders<SlotCapacityAllocation>.Filter.And(
            Builders<SlotCapacityAllocation>.Filter.Eq(
                allocation => allocation.ReservationId,
                reservationId),
            Builders<SlotCapacityAllocation>.Filter.Eq(
                allocation => allocation.ReservationVersion,
                reservationVersion),
            Builders<SlotCapacityAllocation>.Filter.Eq(
                allocation => allocation.EnergyKwh,
                energyKwh));
    }

    private static SlotCapacityAllocation? GetSingleClaim(
        EnergyBookingSlot slot,
        string reservationId)
    {
        // Detect corrupted duplicate claims instead of guessing which quantity is authoritative.
        List<SlotCapacityAllocation> matchingClaims = slot.CapacityAllocations
            .Where(allocation => allocation.ReservationId == reservationId)
            .ToList();

        return matchingClaims.Count switch
        {
            0 => null,
            1 => matchingClaims[0],
            _ => throw new InvalidOperationException(
                "The slot contains duplicate capacity claims for one reservation.")
        };
    }

    private static SlotAvailabilityStatus CalculateAvailabilityStatus(
        SlotAvailabilityStatus currentStatus,
        decimal availableCapacityKwh)
    {
        // Preserve explicit administrative disabling while deriving normal capacity status.
        if (currentStatus == SlotAvailabilityStatus.Unavailable)
        {
            return SlotAvailabilityStatus.Unavailable;
        }

        return availableCapacityKwh == 0
            ? SlotAvailabilityStatus.FullyBooked
            : SlotAvailabilityStatus.Available;
    }

    private static void ValidateReservationCapacityInput(EnergyReservation reservation)
    {
        // Reject malformed reservation references before constructing MongoDB filters.
        ArgumentNullException.ThrowIfNull(reservation);
        ValidateObjectId(reservation.Id, nameof(reservation.Id));
        ValidateObjectId(reservation.SlotId, nameof(reservation.SlotId));
        ValidatePositiveEnergy(reservation.RequestedEnergyKwh, nameof(reservation.RequestedEnergyKwh));
        if (reservation.Version < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservation.Version),
                "Reservation version must be positive.");
        }
    }

    private static void ValidateObjectId(string? value, string parameterName)
    {
        // Enforce the ObjectId convention shared by stations, slots, and reservations.
        if (!ObjectId.TryParse(value, out _))
        {
            throw new ArgumentException("A valid MongoDB ObjectId is required.", parameterName);
        }
    }

    private static void ValidatePositiveEnergy(decimal value, string parameterName)
    {
        // Keep capacity arithmetic strictly positive and in the slot model's kWh unit.
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Requested energy must be greater than zero.");
        }
    }
}
