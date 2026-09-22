using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Slots;

public sealed class UpdateSlotRequestDto
{
    public DateTime StartTimeUtc { get; set; }

    public DateTime EndTimeUtc { get; set; }

    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal TotalCapacityKwh { get; set; }
}
