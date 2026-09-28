/*
 * OpenApiSettings.cs
 * -----------------------------------------------------------------------------
 * Purpose : Controls whether the generated OpenAPI contract is externally
 *           mapped in an environment. Production keeps it disabled by default.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Settings;

public sealed class OpenApiSettings
{
    public const string SectionName = "OpenApi";

    public bool Enabled { get; set; }
}
