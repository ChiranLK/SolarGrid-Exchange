/*
 * JwtSettings.cs
 * -----------------------------------------------------------------------------
 * File        : JwtSettings.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Bound from the JwtSettings configuration section: signing key,
 *               issuer, audience and token lifetime. Program.cs validates it at
 *               start-up; the key comes from user secrets or the environment only.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Settings
{
    public class JwtSettings
    {
        public const string SectionName = "JwtSettings";

        public string Key { get; set; } = string.Empty;

        public string Issuer { get; set; } = string.Empty;

        public string Audience { get; set; } = string.Empty;

        public int ExpirationMinutes { get; set; } = 60;
    }
}