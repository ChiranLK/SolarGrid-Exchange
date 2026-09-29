/*
 * UserResponseDto.cs
 * -----------------------------------------------------------------------------
 * File        : UserResponseDto.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Safe public view of a user account returned by registration
 *               and the Backoffice user-management endpoints. Never carries
 *               the password hash.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Models.DTOs
{
    public class UserResponseDto
    {
        public string Nic {get; set;} = string.Empty;
        public string FullName {get; set;} = string.Empty;

        public string Email {get; set;} = string.Empty;

        public string Phone {get; set;} = string.Empty;

        public string? Address {get; set;}

        public string Role { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        // Only set for Grid Operators; the Member 2 station they operate.
        public string? AssignedStationId { get; set; }

        public bool DeactivationRequested { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}
