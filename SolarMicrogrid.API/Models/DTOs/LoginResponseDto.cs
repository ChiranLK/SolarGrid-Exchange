namespace SolarMicrogrid.API.Models.DTOs
{
    
    public class LoginResponseDto
    {
       
        public string Token { get; set; } = string.Empty;
        public string Nic { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}