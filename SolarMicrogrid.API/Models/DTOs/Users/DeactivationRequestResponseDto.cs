/*
 * DeactivationRequestResponseDto.cs
 * -----------------------------------------------------------------------------
 * File        : DeactivationRequestResponseDto.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : One pending Prosumer deactivation request, as listed for
 *               Backoffice review. Contains contact details only; no password
 *               hash or other security fields.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Models.DTOs.Users
{
    public class DeactivationRequestResponseDto
    {
        public string Nic { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public DateTime? DeactivationRequestedAtUtc { get; set; }
    }
}
