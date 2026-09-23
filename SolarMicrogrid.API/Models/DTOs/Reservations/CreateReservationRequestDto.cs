/*
 * CreateReservationRequestDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Accepts the slot and kWh quantity for a Prosumer creating their own
 *           reservation; identity, status, capacity, and audit data are server-owned.
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Reservations;

public sealed class CreateReservationRequestDto
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
}
