/*
 * BootstrapBackofficeSettings.cs
 * -----------------------------------------------------------------------------
 * File        : BootstrapBackofficeSettings.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Optional settings for creating the very first Backoffice
 *               account at start-up. Disabled by default. Values are meant to
 *               come from environment variables or user secrets, e.g.
 *               BootstrapBackoffice__Enabled=true, and must never be committed
 *               to a tracked appsettings file. See
 *               docs/member-1/backoffice-bootstrap.md.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Settings
{
    public class BootstrapBackofficeSettings
    {
        public const string SectionName = "BootstrapBackoffice";

        // Nothing happens unless this is explicitly set to true.
        public bool Enabled { get; set; }

        public string? Nic { get; set; }

        public string? FullName { get; set; }

        public string? Email { get; set; }

        public string? Phone { get; set; }

        public string? Password { get; set; }
    }
}
