/*
 * EnergyReservation.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines the MongoDB reservation document, lifecycle statuses, audit
 *           data, UTC schedule snapshot, energy quantity, and concurrency data.
 * Ownership: Component 3 stores references to users, stations, and slots rather
 *            than duplicating documents owned by other components.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SolarMicrogrid.API.Models.Entities;

public enum ReservationStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
    Completed
}

public enum ReservationCapacityState
{
    Unallocated,
    HoldPending,
    Held,
    ReleasePending,
    Released,
    Consumed,
    CompensationRequired
}

[BsonIgnoreExtraElements]
public sealed class EnergyReservation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("prosumer_nic")]
    public string ProsumerNic { get; set; } = string.Empty;

    [BsonElement("station_id")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string StationId { get; set; } = string.Empty;

    [BsonElement("slot_id")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string SlotId { get; set; } = string.Empty;

    [BsonElement("scheduled_start_time")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ScheduledStartTimeUtc { get; set; }

    [BsonElement("scheduled_end_time")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ScheduledEndTimeUtc { get; set; }

    [BsonElement("requested_energy_kwh")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal RequestedEnergyKwh { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;

    [BsonElement("version")]
    public long Version { get; set; } = 1;

    [BsonElement("capacity_state")]
    [BsonRepresentation(BsonType.String)]
    public ReservationCapacityState CapacityState { get; set; } = ReservationCapacityState.Unallocated;

    [BsonElement("capacity_claim_version")]
    public long CapacityClaimVersion { get; set; }

    [BsonElement("creation_request_id_hash")]
    [BsonIgnoreIfNull]
    public string? CreationRequestIdHash { get; set; }

    [BsonElement("creation_request_fingerprint_hash")]
    [BsonIgnoreIfNull]
    public string? CreationRequestFingerprintHash { get; set; }

    [BsonElement("created_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    [BsonElement("created_by_actor_nic")]
    public string CreatedByActorNic { get; set; } = string.Empty;

    [BsonElement("updated_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }

    [BsonElement("updated_by_actor_nic")]
    public string UpdatedByActorNic { get; set; } = string.Empty;

    [BsonElement("approved_at")]
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ApprovedAtUtc { get; set; }

    [BsonElement("approved_by_actor_nic")]
    [BsonIgnoreIfNull]
    public string? ApprovedByActorNic { get; set; }

    [BsonElement("rejected_at")]
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? RejectedAtUtc { get; set; }

    [BsonElement("rejected_by_actor_nic")]
    [BsonIgnoreIfNull]
    public string? RejectedByActorNic { get; set; }

    [BsonElement("rejection_reason")]
    [BsonIgnoreIfNull]
    public string? RejectionReason { get; set; }

    [BsonElement("cancelled_at")]
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? CancelledAtUtc { get; set; }

    [BsonElement("cancelled_by_actor_nic")]
    [BsonIgnoreIfNull]
    public string? CancelledByActorNic { get; set; }

    [BsonElement("cancellation_reason")]
    [BsonIgnoreIfNull]
    public string? CancellationReason { get; set; }

    [BsonElement("completed_at")]
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? CompletedAtUtc { get; set; }

    [BsonElement("completed_by_actor_nic")]
    [BsonIgnoreIfNull]
    public string? CompletedByActorNic { get; set; }

    [BsonElement("completed_verification_id")]
    [BsonIgnoreIfNull]
    public string? CompletedVerificationId { get; set; }

    [BsonElement("status_history")]
    public List<ReservationStatusHistoryEntry> StatusHistory { get; set; } = [];
}

[BsonIgnoreExtraElements]
public sealed class ReservationStatusHistoryEntry
{
    [BsonElement("from_status")]
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public ReservationStatus? FromStatus { get; set; }

    [BsonElement("to_status")]
    [BsonRepresentation(BsonType.String)]
    public ReservationStatus ToStatus { get; set; }

    [BsonElement("changed_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ChangedAtUtc { get; set; }

    [BsonElement("actor_nic")]
    public string ActorNic { get; set; } = string.Empty;

    [BsonElement("actor_role")]
    [BsonRepresentation(BsonType.String)]
    public UserRole ActorRole { get; set; }

    [BsonElement("version")]
    public long Version { get; set; }

    [BsonElement("reason")]
    [BsonIgnoreIfNull]
    public string? Reason { get; set; }
}
