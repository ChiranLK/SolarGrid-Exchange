namespace SolarMicrogrid.API.Models.DTOs.Stations;

public sealed class PagedStationResponseDto
{
    public IReadOnlyList<StationResponseDto> Items { get; set; } = [];

    public long TotalCount { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalPages { get; set; }
}
