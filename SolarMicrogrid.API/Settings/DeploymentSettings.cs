/*
 * DeploymentSettings.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines bounded deployment-check behavior without carrying any
 *           infrastructure address, credential, or secret.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Settings;

public sealed class DeploymentSettings
{
    public const string SectionName = "Deployment";

    public int MongoHealthTimeoutSeconds { get; set; } = 5;
}
