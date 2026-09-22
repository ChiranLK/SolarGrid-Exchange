namespace SolarMicrogrid.API.Models.DTOs.Stations;

public sealed class StationResponseDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Address { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public decimal EnergyGenerationCapacityKw { get; set; }

    public decimal BatteryStorageCapacityKwh { get; set; }

    public List<OperatingScheduleDto> OperatingSchedule { get; set; } = [];

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
