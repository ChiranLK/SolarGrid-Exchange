/*
 * ProsumerProfileResponseDto.cs
 * -----------------------------------------------------------------------------
 * File        : ProsumerProfileResponseDto.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Safe, read-only view of the signed-in Prosumer's own account,
 *               returned by the /api/prosumers/me endpoints. Never carries the
 *               password hash or any other internal field.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Models.DTOs.Prosumers
{
    public class ProsumerProfileResponseDto
    {
        public string Nic { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string? Address { get; set; }

        public string Role { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public bool DeactivationRequested { get; set; }

        public DateTime? DeactivationRequestedAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }
}
