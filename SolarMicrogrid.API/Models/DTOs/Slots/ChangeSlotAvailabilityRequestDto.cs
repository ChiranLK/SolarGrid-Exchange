using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Slots;

public sealed class ChangeSlotAvailabilityRequestDto
{
    [Required]
    [RegularExpression("^(Available|Unavailable)$",
        ErrorMessage = "Status must be Available or Unavailable.")]
    public string Status { get; set; } = string.Empty;
}
