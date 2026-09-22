namespace SolarMicrogrid.API.Models.DTOs.Slots;

public sealed class SlotResponseDto
{
    public string Id { get; set; } = string.Empty;

    public string StationId { get; set; } = string.Empty;

    public DateTime StartTimeUtc { get; set; }

    public DateTime EndTimeUtc { get; set; }

    public decimal TotalCapacityKwh { get; set; }

    public decimal AvailableCapacityKwh { get; set; }

    public string AvailabilityStatus { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
