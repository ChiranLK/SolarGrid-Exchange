using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver.GeoJsonObjectModel;

namespace SolarMicrogrid.API.Models.Entities;

public sealed class SolarStationInfo
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    [BsonIgnoreIfNull]
    public string? Description { get; set; }

    [BsonElement("address")]
    public string Address { get; set; } = string.Empty;

    [BsonElement("location")]
    public GeoJsonPoint<GeoJson2DGeographicCoordinates> Location { get; set; } =
        new(new GeoJson2DGeographicCoordinates(0, 0));

    [BsonElement("energy_generation_capacity_kw")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal EnergyGenerationCapacityKw { get; set; }

    [BsonElement("battery_storage_capacity_kwh")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal BatteryStorageCapacityKwh { get; set; }

    [BsonElement("operating_schedule")]
    public List<StationOperatingSchedule> OperatingSchedule { get; set; } = [];

    [BsonElement("is_active")]
    public bool IsActive { get; set; } = true;

    [BsonElement("created_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    [BsonElement("updated_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class StationOperatingSchedule
{
    [BsonElement("day_of_week")]
    [BsonRepresentation(BsonType.String)]
    public DayOfWeek DayOfWeek { get; set; }

    [BsonElement("is_open")]
    public bool IsOpen { get; set; }

    [BsonElement("opening_time")]
    [BsonIgnoreIfNull]
    public string? OpeningTime { get; set; }

    [BsonElement("closing_time")]
    [BsonIgnoreIfNull]
    public string? ClosingTime { get; set; }
}
