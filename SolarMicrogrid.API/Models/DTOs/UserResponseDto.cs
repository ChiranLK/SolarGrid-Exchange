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

        public DateTime CreatedAtUtc { get; set; }
    }
}
    
