/*
 * ReservationStatusHistoryEntryDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Exposes authorized, sanitized lifecycle audit entries without leaking
 *           persistence-only or idempotency data.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Models.DTOs.Reservations;

public sealed class ReservationStatusHistoryEntryDto
{
    public string? FromStatus { get; set; }

    public string ToStatus { get; set; } = string.Empty;

    public DateTime ChangedAtUtc { get; set; }

    public string ActorNic { get; set; } = string.Empty;

    public string ActorRole { get; set; } = string.Empty;

    public long Version { get; set; }

    public string? Reason { get; set; }
}
