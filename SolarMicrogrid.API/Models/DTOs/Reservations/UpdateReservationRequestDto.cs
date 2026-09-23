/*
 * UpdateReservationRequestDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Accepts a reschedule/quantity change and the optimistic concurrency
 *           version; lifecycle and capacity outcomes remain server-owned.
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Reservations;

public sealed class UpdateReservationRequestDto
{
    [Required]
    [RegularExpression("^[a-fA-F0-9]{24}$", ErrorMessage = "SlotId must be a valid MongoDB ObjectId.")]
    public string SlotId { get; set; } = string.Empty;

    [Range(
        typeof(decimal),
        "0.0000000000000000000000000001",
        "79228162514264337593543950335",
        ErrorMessage = "Requested energy must be greater than zero.")]
    public decimal RequestedEnergyKwh { get; set; }

    [Range(typeof(long), "1", "9223372036854775807")]
    public long ExpectedVersion { get; set; }
}
