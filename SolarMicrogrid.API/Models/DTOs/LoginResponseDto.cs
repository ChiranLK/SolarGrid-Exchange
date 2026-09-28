/*
 * LoginResponseDto.cs
 * -----------------------------------------------------------------------------
 * File        : LoginResponseDto.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Successful login response: the signed JWT plus the NIC, name, role
 *               and status the web and Android clients store for routing.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

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