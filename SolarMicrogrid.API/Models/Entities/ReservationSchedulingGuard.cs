/*
 * ReservationSchedulingGuard.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines the Component 3 MongoDB lease that serializes active booking
 *           checks for one prosumer across different stations and slots.
 * Safety  : The document stores no user data beyond the repository NIC identity;
 *           ownership tokens are server-generated and expire after interrupted work.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Bson.Serialization.Attributes;

namespace SolarMicrogrid.API.Models.Entities;

[BsonIgnoreExtraElements]
public sealed class ReservationSchedulingGuard
{
    [BsonId]
    public string ProsumerNic { get; set; } = string.Empty;

    [BsonElement("lease_token")]
    public string LeaseToken { get; set; } = string.Empty;

    [BsonElement("lease_expires_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LeaseExpiresAtUtc { get; set; }
}
