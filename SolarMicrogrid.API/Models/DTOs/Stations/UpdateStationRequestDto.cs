using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Stations;

public sealed class UpdateStationRequestDto
{
    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(250)]
    public string Address { get; set; } = string.Empty;

    [Range(-90d, 90d)]
    public double Latitude { get; set; }

    [Range(-180d, 180d)]
    public double Longitude { get; set; }

    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal EnergyGenerationCapacityKw { get; set; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal BatteryStorageCapacityKwh { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(7)]
    public List<OperatingScheduleDto> OperatingSchedule { get; set; } = [];
}
