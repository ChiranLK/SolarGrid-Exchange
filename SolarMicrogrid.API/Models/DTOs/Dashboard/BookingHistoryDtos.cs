/*
 * BookingHistoryDtos.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines validated history/search inputs and stable paged output for
 *           the role-scoped Member 4 booking-history endpoint.
 * Time    : FromUtc and ToUtc inclusively filter ScheduledStartTimeUtc.
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Models.DTOs.Dashboard;

public sealed class BookingHistoryQueryDto
{
    [MaxLength(100)]
    public string? Search { get; set; }

    [EnumDataType(typeof(ReservationStatus))]
    public ReservationStatus? Status { get; set; }

    [RegularExpression("^[a-fA-F0-9]{24}$", ErrorMessage = "StationId must be a valid MongoDB ObjectId.")]
    public string? StationId { get; set; }

    public DateTime? FromUtc { get; set; }

    public DateTime? ToUtc { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

public sealed class PagedBookingHistoryResponseDto
{
    public DateTime ServerNowUtc { get; set; }

    public IReadOnlyList<DashboardReservationSummaryDto> Items { get; set; } = [];

    public long TotalCount { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalPages { get; set; }
}
