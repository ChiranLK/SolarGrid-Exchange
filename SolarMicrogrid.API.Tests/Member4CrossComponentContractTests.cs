/*
 * Member4CrossComponentContractTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Detects drift between Member 1 identity, Member 2 station fields,
 *           Member 3 lifecycle labels, and Member 4 public JSON contracts.
 * Boundary: These tests assert shared transport contracts without duplicating
 *           reservation, station, authentication, or capacity business rules.
 * -----------------------------------------------------------------------------
 */

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Helpers;
using SolarMicrogrid.API.Middleware;
using SolarMicrogrid.API.Models.DTOs.Dashboard;
using SolarMicrogrid.API.Models.DTOs.Stations;
using SolarMicrogrid.API.Models.DTOs.Transactions;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Settings;
using Xunit;

namespace SolarMicrogrid.API.Tests;

public sealed class Member4CrossComponentContractTests
{
    private const string TestIssuer = "solargrid-contract-tests";
    private const string TestAudience = "solargrid-clients";
    private const string TestKey = "integration-only-key-at-least-32-characters";

    [Fact]
    public void JwtCarriesCanonicalIdentityAndRoleClaimsWithUtcExpiry()
    {
        // Validate Member 1's signed token through the same claim types consumed by Member 4 controllers.
        DateTime issuedAfterUtc = DateTime.UtcNow.AddSeconds(-1);
        var settings = new JwtSettings
        {
            Key = TestKey,
            Issuer = TestIssuer,
            Audience = TestAudience,
            ExpirationMinutes = 30
        };
        var user = new User
        {
            Nic = "200000000000",
            Email = "contract@example.test",
            FullName = "Contract User",
            Role = UserRole.GridOperator,
            Status = UserStatus.Active
        };

        string token = new JwtHelper(Options.Create(settings)).GenerateToken(user);
        ClaimsPrincipal principal = ValidateToken(token, settings, out JwtSecurityToken validated);

        Assert.Equal(user.Nic, principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(nameof(UserRole.GridOperator), principal.FindFirstValue(ClaimTypes.Role));
        Assert.Equal(DateTimeKind.Utc, validated.ValidTo.Kind);
        Assert.InRange(
            validated.ValidTo,
            issuedAfterUtc.AddMinutes(29),
            DateTime.UtcNow.AddMinutes(31));
    }

    [Fact]
    public async Task SharedErrorMiddlewareRetainsStatusAndMessageSchema()
    {
        // Exercise the error envelope tolerated by both web and Android clients.
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionMiddleware(
            _ => throw new ConflictException("The reservation changed."),
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        using JsonDocument document = await JsonDocument.ParseAsync(context.Response.Body);
        JsonElement root = document.RootElement;

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        AssertJsonProperties(root, "message", "status");
        Assert.Equal(StatusCodes.Status409Conflict, root.GetProperty("status").GetInt32());
        Assert.Equal("The reservation changed.", root.GetProperty("message").GetString());
    }

    [Fact]
    public void CanonicalRolesAndReservationStatusesRemainExact()
    {
        // Pin the case-sensitive labels shared by authorization metadata and both client UIs.
        Assert.Equal(
            ["Backoffice", "GridOperator", "Prosumer"],
            Enum.GetNames<UserRole>());
        Assert.Equal(
            ["Pending", "Approved", "Rejected", "Cancelled", "Completed"],
            Enum.GetNames<ReservationStatus>());
    }

    [Fact]
    public void DashboardTransactionAndStationDtosUseStableCamelCaseUtcContracts()
    {
        // Serialize representative DTOs with ASP.NET's web defaults and assert shared client fields.
        DateTime timestampUtc = new(2026, 9, 27, 10, 30, 0, DateTimeKind.Utc);
        var dashboard = new PagedBookingHistoryResponseDto
        {
            ServerNowUtc = timestampUtc,
            Items =
            [
                new DashboardReservationSummaryDto
                {
                    ReservationId = "000000000000000000000001",
                    Reference = "RES-00000001",
                    ProsumerNic = "200000000000",
                    StationId = "000000000000000000000002",
                    StationName = "Central Solar Hub",
                    StationAddress = "Integration address",
                    SlotId = "000000000000000000000003",
                    ScheduledStartTimeUtc = timestampUtc.AddHours(1),
                    ScheduledEndTimeUtc = timestampUtc.AddHours(2),
                    RequestedEnergyKwh = 5m,
                    Status = nameof(ReservationStatus.Completed),
                    Version = 2,
                    CreatedAtUtc = timestampUtc.AddDays(-1),
                    UpdatedAtUtc = timestampUtc,
                    CompletedAtUtc = timestampUtc
                }
            ],
            TotalCount = 1,
            Page = 2,
            PageSize = 20,
            TotalPages = 3
        };
        var verification = new VerifyQrTransactionResponseDto
        {
            VerificationId = new string('v', 43),
            ReservationId = "000000000000000000000001",
            ReservationReference = "RES-00000001",
            ProsumerReference = "PRO-00000001",
            ReservationVersion = 2,
            StationId = "000000000000000000000002",
            StationName = "Central Solar Hub",
            ScheduledStartTimeUtc = timestampUtc.AddHours(1),
            ScheduledEndTimeUtc = timestampUtc.AddHours(2),
            RequestedEnergyKwh = 5m,
            Status = nameof(ReservationStatus.Approved),
            VerifiedAtUtc = timestampUtc,
            ExpiresAtUtc = timestampUtc.AddMinutes(5)
        };
        var station = new StationResponseDto
        {
            Id = "000000000000000000000002",
            Name = "Central Solar Hub",
            Address = "Integration address",
            Latitude = 6.9271,
            Longitude = 79.8612,
            IsActive = true,
            CreatedAtUtc = timestampUtc,
            UpdatedAtUtc = timestampUtc
        };

        JsonElement historyJson = SerializeToElement(dashboard);
        JsonElement itemJson = historyJson.GetProperty("items")[0];
        JsonElement verificationJson = SerializeToElement(verification);
        JsonElement stationJson = SerializeToElement(station);

        AssertJsonProperties(
            historyJson,
            "items", "page", "pageSize", "serverNowUtc", "totalCount", "totalPages");
        AssertJsonProperties(
            itemJson,
            "completedAtUtc", "createdAtUtc", "prosumerFullName", "prosumerNic",
            "reference", "requestedEnergyKwh", "reservationId", "scheduledEndTimeUtc",
            "scheduledStartTimeUtc", "slotId", "stationAddress", "stationId", "stationName",
            "status", "updatedAtUtc", "version");
        AssertJsonProperties(
            verificationJson,
            "expiresAtUtc", "prosumerReference", "requestedEnergyKwh", "reservationId",
            "reservationReference", "reservationVersion", "scheduledEndTimeUtc",
            "scheduledStartTimeUtc", "stationId", "stationName", "status", "verificationId",
            "verifiedAtUtc");
        Assert.Equal(2, historyJson.GetProperty("page").GetInt32());
        Assert.Equal(20, historyJson.GetProperty("pageSize").GetInt32());
        Assert.EndsWith("Z", historyJson.GetProperty("serverNowUtc").GetString());
        Assert.Equal(6.9271, stationJson.GetProperty("latitude").GetDouble(), 4);
        Assert.Equal(79.8612, stationJson.GetProperty("longitude").GetDouble(), 4);
    }

    private static ClaimsPrincipal ValidateToken(
        string token,
        JwtSettings settings,
        out JwtSecurityToken validatedToken)
    {
        // Validate signature, issuer, audience, and lifetime while preserving standard claim mappings.
        var handler = new JwtSecurityTokenHandler();
        ClaimsPrincipal principal = handler.ValidateToken(
            token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = settings.Issuer,
                ValidateAudience = true,
                ValidAudience = settings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            },
            out SecurityToken securityToken);
        validatedToken = Assert.IsType<JwtSecurityToken>(securityToken);
        return principal;
    }

    private static JsonElement SerializeToElement<T>(T value)
    {
        // Match ASP.NET Core's camel-case web JSON defaults used by React and native Android.
        return JsonSerializer.SerializeToElement(
            value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static void AssertJsonProperties(JsonElement value, params string[] expected)
    {
        // Compare the full public field set so additions, removals, or casing changes fail loudly.
        string[] actual = value.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }
}
