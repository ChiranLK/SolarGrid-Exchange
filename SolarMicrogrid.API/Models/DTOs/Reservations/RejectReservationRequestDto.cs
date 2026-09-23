/*
 * RejectReservationRequestDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Supplies the optimistic concurrency version and required audit reason
 *           for an authorized staff rejection.
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Reservations;

public sealed class RejectReservationRequestDto
{
    [Range(typeof(long), "1", "9223372036854775807")]
    public long ExpectedVersion { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
