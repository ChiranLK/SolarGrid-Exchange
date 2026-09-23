/*
 * PagedReservationResponseDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Wraps reservation list items with the same paging metadata used by
 *           the station and slot APIs.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Models.DTOs.Reservations;

public sealed class PagedReservationResponseDto
{
    public IReadOnlyList<ReservationListItemResponseDto> Items { get; set; } = [];

    public long TotalCount { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalPages { get; set; }
}
