using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Slots;

public sealed class CreateSlotRequestDto
{
    [Required]
    [RegularExpression("^[a-fA-F0-9]{24}$", ErrorMessage = "StationId must be a valid MongoDB ObjectId.")]
    public string StationId { get; set; } = string.Empty;

    public DateTime StartTimeUtc { get; set; }

    public DateTime EndTimeUtc { get; set; }

    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal TotalCapacityKwh { get; set; }
}
