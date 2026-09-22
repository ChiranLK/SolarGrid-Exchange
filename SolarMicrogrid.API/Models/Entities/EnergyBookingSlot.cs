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

    [BsonElement("created_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    [BsonElement("updated_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }
}
