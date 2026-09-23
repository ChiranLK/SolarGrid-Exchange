/*
 * ReservationResponseDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Returns reservation detail and authorized audit metadata for web,
 *           Android summary pages, and staff workflows.
 * Security: Never returns idempotency hashes, capacity counters, or raw QR data.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Models.DTOs.Reservations;

public sealed class ReservationResponseDto
{
    public string Id { get; set; } = string.Empty;

    public string ProsumerNic { get; set; } = string.Empty;

    public string StationId { get; set; } = string.Empty;

    public string SlotId { get; set; } = string.Empty;

    public DateTime ScheduledStartTimeUtc { get; set; }

    public DateTime ScheduledEndTimeUtc { get; set; }

    public decimal RequestedEnergyKwh { get; set; }

    public string Status { get; set; } = string.Empty;

    public long Version { get; set; }

    public bool QrEligible { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string CreatedByActorNic { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }

    public string UpdatedByActorNic { get; set; } = string.Empty;

    public DateTime? ApprovedAtUtc { get; set; }

    public string? ApprovedByActorNic { get; set; }

    public DateTime? RejectedAtUtc { get; set; }

    public string? RejectedByActorNic { get; set; }

    public string? RejectionReason { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public string? CancelledByActorNic { get; set; }

    public string? CancellationReason { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? CompletedByActorNic { get; set; }

    public string? CompletedVerificationId { get; set; }

    public IReadOnlyList<ReservationStatusHistoryEntryDto> StatusHistory { get; set; } = [];
}
