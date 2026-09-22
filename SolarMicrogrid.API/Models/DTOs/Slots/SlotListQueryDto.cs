using System.ComponentModel.DataAnnotations;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Models.DTOs.Slots;

public sealed class SlotListQueryDto
{
    [RegularExpression("^[a-fA-F0-9]{24}$", ErrorMessage = "StationId must be a valid MongoDB ObjectId.")]
    public string? StationId { get; set; }

    [EnumDataType(typeof(SlotAvailabilityStatus))]
    public SlotAvailabilityStatus? Status { get; set; }

    public DateTime? FromUtc { get; set; }

    public DateTime? ToUtc { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}
