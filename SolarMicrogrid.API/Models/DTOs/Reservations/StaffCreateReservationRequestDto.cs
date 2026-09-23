/*
 * StaffCreateReservationRequestDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Accepts a proposed target Prosumer, slot, and kWh quantity for an
 *           authorized staff-created reservation; the API resolves ownership.
 * Note    : Service/controller authorization for this contract extension is not
 *           enabled by the DTO itself.
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Reservations;

public sealed class StaffCreateReservationRequestDto
{
    [Required]
    [RegularExpression(
        "^([0-9]{9}[VvXx]|[0-9]{12})$",
        ErrorMessage = "TargetProsumerNic must be 9 digits followed by V or X, or 12 digits.")]
    public string TargetProsumerNic { get; set; } = string.Empty;

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
