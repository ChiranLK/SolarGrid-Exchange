/*
 * JwtHelper.cs
 * -----------------------------------------------------------------------------
 * File        : JwtHelper.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Issues the signed HS256 JWT returned at login. The token carries
 *               the NIC (NameIdentifier), email, full name and role claims that
 *               every [Authorize] endpoint and [RequireActiveAccount] rely on.
 *               Issuer, audience, key and lifetime come from validated JwtSettings.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Settings;
namespace SolarMicrogrid.API.Helpers
{
    public class JwtHelper
    {
        private readonly JwtSettings _settings;

        
        public JwtHelper(IOptions<JwtSettings> options)
        {
            // Read the validated JWT settings once; Program.cs rejects a missing or short key at start-up.
            _settings = options.Value;
        }


        public string GenerateToken(User user)
        {
            // Build the identity and role claims, sign them with the configured key, and set the expiry.
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Nic),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));

            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_settings.ExpirationMinutes),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}