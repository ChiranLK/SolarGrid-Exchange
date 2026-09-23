/*
 * ReservationListItemResponseDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Returns the compact reservation fields required by role-scoped lists,
 *           history views, search results, and client summary navigation.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Models.DTOs.Reservations;

public sealed class ReservationListItemResponseDto
{
    public string Id { get; set; } = string.Empty;

    public string ProsumerNic { get; set; } = string.Empty;

    public string StationId { get; set; } = string.Empty;

    public string? StationName { get; set; }

    public string? StationAddress { get; set; }

    public string SlotId { get; set; } = string.Empty;

    public string? SlotAvailabilityStatus { get; set; }

    public DateTime ScheduledStartTimeUtc { get; set; }

    public DateTime ScheduledEndTimeUtc { get; set; }

    public decimal RequestedEnergyKwh { get; set; }

    public string Status { get; set; } = string.Empty;

    public long Version { get; set; }

    public bool QrEligible { get; set; }

    public ReservationAllowedActionsDto AllowedActions { get; set; } = new();

    public DateTime UpdatedAtUtc { get; set; }
}
