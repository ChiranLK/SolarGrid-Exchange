/*
 * DashboardResponseDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines stable server-authoritative dashboard counts and concise
 *           reservation summaries shared by web and native Android clients.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Models.DTOs.Dashboard;

public sealed class DashboardResponseDto
{
    public DateTime ServerNowUtc { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public string? StationId { get; set; }

    public DashboardStatusSummaryDto StatusSummary { get; set; } = new();

    public IReadOnlyList<DashboardReservationSummaryDto> CurrentReservations { get; set; } = [];

    public IReadOnlyList<DashboardReservationSummaryDto> PendingReservations { get; set; } = [];

    public IReadOnlyList<DashboardReservationSummaryDto> RecentHistory { get; set; } = [];

    public IReadOnlyList<DashboardReservationSummaryDto> RecentTransfers { get; set; } = [];

    public IReadOnlyList<DashboardReservationSummaryDto> ActiveTransfers { get; set; } = [];

    public IReadOnlyList<DashboardReservationSummaryDto> CompletedTransfers { get; set; } = [];
}

public sealed class DashboardStatusSummaryDto
{
    public long PendingTotal { get; set; }

    public long ApprovedTotal { get; set; }

    public long RejectedTotal { get; set; }

    public long CancelledTotal { get; set; }

    public long CompletedTotal { get; set; }

    public long CurrentCount { get; set; }

    public long PendingCount { get; set; }

    public long ApprovedFutureCount { get; set; }

    public long HistoryCount { get; set; }
}

public sealed class DashboardReservationSummaryDto
{
    public string ReservationId { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    public string ProsumerNic { get; set; } = string.Empty;

    public string? ProsumerFullName { get; set; }

    public string StationId { get; set; } = string.Empty;

    public string? StationName { get; set; }

    public string? StationAddress { get; set; }

    public string SlotId { get; set; } = string.Empty;

    public DateTime ScheduledStartTimeUtc { get; set; }

    public DateTime ScheduledEndTimeUtc { get; set; }

    public decimal RequestedEnergyKwh { get; set; }

    public string Status { get; set; } = string.Empty;

    public long Version { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}
