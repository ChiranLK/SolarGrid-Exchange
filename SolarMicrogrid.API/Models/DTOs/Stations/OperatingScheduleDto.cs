using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Stations;

public sealed class OperatingScheduleDto
{
    [Required]
    [MaxLength(9)]
    public string DayOfWeek { get; set; } = string.Empty;

    public bool IsOpen { get; set; }

    [RegularExpression(@"^(?:[01]\d|2[0-3]):[0-5]\d$",
        ErrorMessage = "Opening time must use 24-hour HH:mm format.")]
    public string? OpeningTime { get; set; }

    [RegularExpression(@"^(?:[01]\d|2[0-3]):[0-5]\d$",
        ErrorMessage = "Closing time must use 24-hour HH:mm format.")]
    public string? ClosingTime { get; set; }
}
