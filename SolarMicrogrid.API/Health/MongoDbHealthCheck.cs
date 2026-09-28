/*
 * MongoDbHealthCheck.cs
 * -----------------------------------------------------------------------------
 * Purpose : Confirms MongoDB connectivity for deployment readiness checks using
 *           a bounded server-side timeout and a non-mutating ping command.
 * Security: Results contain no exception, connection string, database name,
 *           server topology, credential, or other database internals.
 * -----------------------------------------------------------------------------
 */

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SolarMicrogrid.API.Settings;

namespace SolarMicrogrid.API.Health;

public sealed class MongoDbHealthCheck(
    IMongoClient mongoClient,
    IOptions<MongoSettings> mongoOptions,
    IOptions<DeploymentSettings> deploymentOptions) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Bound the driver operation independently from proxy and health-check timeouts.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            deploymentOptions.Value.MongoHealthTimeoutSeconds));

        try
        {
            IMongoDatabase database = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
            await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: timeout.Token);
            return HealthCheckResult.Healthy("Database connection is available.");
        }
        catch (Exception exception) when (
            exception is MongoException or OperationCanceledException or TimeoutException)
        {
            // Deliberately omit the caught exception so health output cannot disclose internals.
            return HealthCheckResult.Unhealthy("Database connection is unavailable.");
        }
    }
}
