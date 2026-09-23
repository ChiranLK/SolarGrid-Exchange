/*
 * ApproveReservationRequestDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Supplies the optimistic concurrency version for an authorized staff
 *           approval while keeping status and approval identity server-owned.
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Reservations;

public sealed class ApproveReservationRequestDto
{
    [Range(typeof(long), "1", "9223372036854775807")]
    public long ExpectedVersion { get; set; }
}
