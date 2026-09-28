/*
 * SafeHealthResponseWriter.cs
 * -----------------------------------------------------------------------------
 * Purpose : Writes a minimal deployment-safe health response containing only
 *           aggregate/component states and the request correlation identifier.
 * -----------------------------------------------------------------------------
 */

using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SolarMicrogrid.API.Health;

public static class SafeHealthResponseWriter
{
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        // Expose stable check names and states only, never descriptions, data, or exceptions.
        context.Response.ContentType = "application/json; charset=utf-8";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Status.ToString()),
            correlationId = context.TraceIdentifier
        };

        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            context.RequestAborted);
    }
}
