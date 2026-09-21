using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs
{
    public class RegisterRequestDto
    {
        [Required(ErrorMessage = "NIC is required.")]
        [MaxLength(12, ErrorMessage = "NIC cannot be longer than 12 characters.")]
        [RegularExpression(@"^([0-9]{9}[VvXx]|[0-9]{12})$",
            ErrorMessage = "NIC must be 9 digits followed by V or X, or 12 digits.")]
        public string Nic { get; set; } = string.Empty;

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

        [Required(ErrorMessage = "Password is required.")]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        [MaxLength(100, ErrorMessage = "Password cannot be longer than 100 characters.")]
        public string Password { get; set; } = string.Empty;
    }
}