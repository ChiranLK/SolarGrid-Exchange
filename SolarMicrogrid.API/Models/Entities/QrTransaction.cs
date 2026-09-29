/*
 * QrTransaction.cs
 * -----------------------------------------------------------------------------
 * Purpose : Stores the minimum server-side state needed to issue, verify, and
 *           consume opaque QR transaction tokens with replay protection.
 * Security: Raw QR and verification secrets are never persisted; actor
 *           references are one-way hashes and public DTOs omit all hashes.
 * -----------------------------------------------------------------------------
 */

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SolarMicrogrid.API.Models.Entities;

public enum QrTransactionState
{
    Issued,
    Verified,
    Completed,
    Expired,
    Revoked
}

[BsonIgnoreExtraElements]
public sealed class QrTransaction
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("token_hash")]
    public string TokenHash { get; set; } = string.Empty;

    [BsonElement("verification_hash")]
    [BsonIgnoreIfNull]
    public string? VerificationHash { get; set; }

    [BsonElement("reservation_id")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string ReservationId { get; set; } = string.Empty;

    [BsonElement("reservation_version")]
    public long ReservationVersion { get; set; }

    [BsonElement("station_id")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string StationId { get; set; } = string.Empty;

    [BsonElement("owner_reference_hash")]
    public string OwnerReferenceHash { get; set; } = string.Empty;

    [BsonElement("state")]
    [BsonRepresentation(BsonType.String)]
    public QrTransactionState State { get; set; } = QrTransactionState.Issued;

    [BsonElement("issued_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime IssuedAtUtc { get; set; }

    [BsonElement("token_expires_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime TokenExpiresAtUtc { get; set; }

    [BsonElement("verified_at")]
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? VerifiedAtUtc { get; set; }

    [BsonElement("verification_expires_at")]
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? VerificationExpiresAtUtc { get; set; }

    [BsonElement("verified_by_operator_reference_hash")]
    [BsonIgnoreIfNull]
    public string? VerifiedByOperatorReferenceHash { get; set; }

    [BsonElement("completed_at")]
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? CompletedAtUtc { get; set; }

    [BsonElement("completed_by_operator_reference_hash")]
    [BsonIgnoreIfNull]
    public string? CompletedByOperatorReferenceHash { get; set; }

    [BsonElement("updated_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }
}
