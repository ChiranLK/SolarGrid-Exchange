namespace SolarMicrogrid.API.Models.DTOs.Slots;

public sealed class PagedSlotResponseDto
{
    public IReadOnlyList<SlotResponseDto> Items { get; set; } = [];

    public long TotalCount { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalPages { get; set; }
}
