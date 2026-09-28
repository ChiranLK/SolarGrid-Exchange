/*
 * DeploymentContractTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Verifies Member 4 deployment boundaries for safe health output,
 *           bounded MongoDB failure, CORS origins, and request correlation.
 * -----------------------------------------------------------------------------
 */

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Health;
using SolarMicrogrid.API.Middleware;
using SolarMicrogrid.API.Settings;
using Xunit;

namespace SolarMicrogrid.API.Tests;

public sealed class DeploymentContractTests
{
    [Fact]
    public void CorsOriginsRejectWildcardsAndNonHttpsProductionValues()
    {
        // Prevent deployment configuration from broadening browser access accidentally.
        var wildcard = new CorsSettings { AllowedOrigins = ["*"] };
        var productionHttp = new CorsSettings
        {
            AllowedOrigins = ["http://web.example.test"]
        };
        var productionHttps = new CorsSettings
        {
            AllowedOrigins = [" https://web.example.test/ ", "https://web.example.test"]
        };

        Assert.False(wildcard.HasValidOrigins(requireHttps: false));
        Assert.False(productionHttp.HasValidOrigins(requireHttps: true));
        Assert.True(productionHttps.HasValidOrigins(requireHttps: true));
        Assert.Equal(["https://web.example.test"], productionHttps.GetNormalizedOrigins());
    }

    [Fact]
    public async Task HealthResponseOmitsDescriptionsExceptionsAndDiagnosticData()
    {
        // Confirm an unhealthy dependency cannot leak database or credential details to callers.
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "safe-correlation-id"
        };
        context.Response.Body = new MemoryStream();
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["api"] = new(
                HealthStatus.Healthy,
                "process detail",
                TimeSpan.Zero,
                null,
                null),
            ["database"] = new(
                HealthStatus.Unhealthy,
                "sensitive connection diagnostic at internal-host",
                TimeSpan.Zero,
                new InvalidOperationException("sensitive stack detail"),
                new Dictionary<string, object> { ["credential"] = "sensitive value" })
        };
        var report = new HealthReport(entries, TimeSpan.Zero);

        await SafeHealthResponseWriter.WriteAsync(context, report);
        context.Response.Body.Position = 0;
        using JsonDocument document = await JsonDocument.ParseAsync(context.Response.Body);
        string serialized = document.RootElement.GetRawText();

        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "Unhealthy",
            document.RootElement.GetProperty("checks").GetProperty("database").GetString());
        Assert.Equal(
            "safe-correlation-id",
            document.RootElement.GetProperty("correlationId").GetString());
        Assert.DoesNotContain("connection", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal-host", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MongoHealthFailureReturnsSafeUnhealthyResultWithinBound()
    {
        // Exercise an unreachable local endpoint without requiring or modifying a real database.
        var client = new MongoClient(
            "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=100&connectTimeoutMS=100");
        var check = new MongoDbHealthCheck(
            client,
            Options.Create(new MongoSettings { DatabaseName = "health_test" }),
            Options.Create(new DeploymentSettings { MongoHealthTimeoutSeconds = 1 }));
        var startedAt = DateTime.UtcNow;

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.Empty(result.Data);
        Assert.True(DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UnexpectedErrorsReturnGenericCorrelatedResponseWithoutSensitiveDetail()
    {
        // Exercise the production middleware chain and prove exception text never reaches the caller.
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var exceptions = new ExceptionMiddleware(
            _ => throw new InvalidOperationException(
                "Sensitive JWT, QR token, NIC, connection and stack detail."),
            NullLogger<ExceptionMiddleware>.Instance);
        var correlation = new CorrelationIdMiddleware(
            exceptions.InvokeAsync,
            NullLogger<CorrelationIdMiddleware>.Instance);

        await correlation.InvokeAsync(context);
        context.Response.Body.Position = 0;
        using JsonDocument document = await JsonDocument.ParseAsync(context.Response.Body);
        string serialized = document.RootElement.GetRawText();

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(
            "An unexpected error occurred.",
            document.RootElement.GetProperty("message").GetString());
        Assert.Equal(
            context.TraceIdentifier,
            context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
        Assert.DoesNotContain("Sensitive", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JWT", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NIC", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("deploy-check-123", "deploy-check-123")]
    [InlineData("bad\r\nforged-header", null)]
    public async Task CorrelationMiddlewareAcceptsOnlySafeHeaderValues(
        string supplied,
        string? expected)
    {
        // Keep correlation useful across IIS while rejecting response/log header injection.
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = supplied;
        var middleware = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        string actual = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.Equal(context.TraceIdentifier, actual);
        Assert.True(actual.Length is > 0 and <= 64);
        if (expected is not null)
        {
            Assert.Equal(expected, actual);
        }
        else
        {
            Assert.NotEqual(supplied, actual);
        }
    }
}
