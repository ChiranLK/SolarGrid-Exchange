/*
 * CancelReservationRequestDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Accepts the expected reservation version and an optional audit reason
 *           for an owner-requested cancellation.
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Reservations;

public sealed class CancelReservationRequestDto
{
    [Range(typeof(long), "1", "9223372036854775807")]
    public long ExpectedVersion { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}
