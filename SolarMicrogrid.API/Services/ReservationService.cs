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

internal sealed record ReservationCreationEntityResult(
    EnergyReservation Reservation,
    bool IdempotencyReplayed);

public sealed class ReservationService
{
    private const string OperatingScheduleTimeZoneId = "Asia/Colombo";
    private const int MaximumIdempotencyKeyLength = 200;
    private const int MinimumIdempotencyKeyLength = 8;

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
    private readonly int _maximumBookingDaysAhead;
    private readonly int _minimumChangeNoticeHours;

    public ReservationService(
        MongoDbContext context,
        ReservationCapacityService capacityService,
        MongoTransactionRunner transactionRunner,
        ReservationSchedulingGuardService schedulingGuardService,
        IOptions<BusinessRules> businessRulesOptions)
    {
        // Reuse shared persistence and consistency services through dependency injection.
        _context = context;
        _capacityService = capacityService;
        _transactionRunner = transactionRunner;
        _schedulingGuardService = schedulingGuardService;
        BusinessRules rules = businessRulesOptions.Value;
        _maximumBookingDaysAhead = rules.MaxBookingDaysAhead;
        _minimumChangeNoticeHours = rules.MinChangeNoticeHours;
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
            DateTime serverNowUtc = DateTime.UtcNow;
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
            throw new ForbiddenException("Only active users may create reservations.");
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

    private ReservationResponseDto MapToResponse(
        EnergyReservation reservation,
        User actor,
        DateTime serverNowUtc)
    {
        // Project persisted state plus actor-scoped actions without exposing internal hashes or counters.
        bool ownerProsumer = actor.Role == UserRole.Prosumer &&
            string.Equals(actor.Nic, reservation.ProsumerNic, StringComparison.Ordinal);
        bool noticeSatisfied = reservation.ScheduledStartTimeUtc - serverNowUtc >=
            TimeSpan.FromHours(_minimumChangeNoticeHours);
        bool mutable = StatusHoldsCapacity(reservation.Status);
        bool backofficeCanDecide = actor.Role == UserRole.Backoffice &&
            reservation.Status == ReservationStatus.Pending &&
            reservation.ScheduledStartTimeUtc > serverNowUtc;
        bool assignedGridOperator = actor.Role == UserRole.GridOperator &&
            string.Equals(actor.AssignedStationId, reservation.StationId, StringComparison.Ordinal);

        return new ReservationResponseDto
        {
            Id = reservation.Id,
            ProsumerNic = reservation.ProsumerNic,
            StationId = reservation.StationId,
            SlotId = reservation.SlotId,
            ScheduledStartTimeUtc = reservation.ScheduledStartTimeUtc,
            ScheduledEndTimeUtc = reservation.ScheduledEndTimeUtc,
            RequestedEnergyKwh = reservation.RequestedEnergyKwh,
            Status = reservation.Status.ToString(),
            Version = reservation.Version,
            QrEligible = reservation.Status == ReservationStatus.Approved,
            AllowedActions = new ReservationAllowedActionsDto
            {
                CanUpdate = ownerProsumer && mutable && noticeSatisfied,
                CanCancel = ownerProsumer && mutable && noticeSatisfied,
                CanApprove = backofficeCanDecide,
                CanReject = backofficeCanDecide,
                CanGetQr = ownerProsumer && reservation.Status == ReservationStatus.Approved,
                CanVerifyQr = assignedGridOperator && reservation.Status == ReservationStatus.Approved,
                CanComplete = false
            },
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
