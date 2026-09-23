/*
 * EnergyBookingSlot.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines Member 2's energy slot and the minimal shared allocation
 *           ledger used by Component 3 for idempotent capacity holds/releases.
 * Safety  : Allocation entries reference reservations only; reservation documents
 *           are not embedded or duplicated in the slot collection.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SolarMicrogrid.API.Models.Entities;

public enum SlotAvailabilityStatus
{
    Available,
    FullyBooked,
    Unavailable
}

public sealed class EnergyBookingSlot
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("station_id")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string StationId { get; set; } = string.Empty;

    [BsonElement("start_time")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime StartTimeUtc { get; set; }

    [BsonElement("end_time")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime EndTimeUtc { get; set; }

    [BsonElement("total_capacity_kwh")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalCapacityKwh { get; set; }

    [BsonElement("available_capacity_kwh")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal AvailableCapacityKwh { get; set; }

    [BsonElement("availability_status")]
    [BsonRepresentation(BsonType.String)]
    public SlotAvailabilityStatus AvailabilityStatus { get; set; } = SlotAvailabilityStatus.Available;

    [BsonElement("capacity_allocations")]
    public List<SlotCapacityAllocation> CapacityAllocations { get; set; } = [];

    [BsonElement("created_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    [BsonElement("updated_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class SlotCapacityAllocation
{
    [BsonElement("reservation_id")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string ReservationId { get; set; } = string.Empty;

    [BsonElement("reservation_version")]
    public long ReservationVersion { get; set; }

    [BsonElement("energy_kwh")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal EnergyKwh { get; set; }

    [BsonElement("allocated_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime AllocatedAtUtc { get; set; }
}
