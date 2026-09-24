/*
 * PagedEligibleProsumerResponseDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Carries one bounded page of eligible Prosumer search results and
 *           authoritative paging metadata to staff clients.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Models.DTOs.Users;

public sealed class PagedEligibleProsumerResponseDto
{
    public IReadOnlyList<EligibleProsumerResponseDto> Items { get; set; } = [];

    public long TotalCount { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalPages { get; set; }
}
