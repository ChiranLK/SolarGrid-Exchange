/*
 * UpdateProfileDto.cs
 * -----------------------------------------------------------------------------
 * File        : UpdateProfileDto.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Request body for PUT /api/prosumers/me. Contains only the fields a
 *               Prosumer may change (name, email, phone, address); NIC, role, status
 *               and password cannot be bound from the request.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs
{
    public class UpdateProfileDto
    {
        [Required(ErrorMessage = "Full name is required.")]
        [MaxLength(100, ErrorMessage = "Full name cannot be longer than 100 characters.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [MaxLength(100, ErrorMessage = "Email cannot be longer than 100 characters.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone is required.")]
        [MaxLength(20, ErrorMessage = "Phone cannot be longer than 20 characters.")]
        public string Phone { get; set; } = string.Empty;

        [MaxLength(200, ErrorMessage = "Address cannot be longer than 200 characters.")]
        public string? Address { get; set; }
    }
}