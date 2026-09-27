/*
 * DashboardControllerContractTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Verifies Member 4 dashboard routes retain authentication and the
 *           repository's exact Prosumer, Backoffice, and GridOperator role names.
 * -----------------------------------------------------------------------------
 */

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolarMicrogrid.API.Controllers;
using SolarMicrogrid.API.Models.Entities;
using Xunit;

namespace SolarMicrogrid.API.Tests;

public sealed class DashboardControllerContractTests
{
    [Fact]
    public void ControllerRequiresAuthenticationAndUsesExpectedRoute()
    {
        // Preserve the shared authenticated API boundary and stable dashboard route prefix.
        AuthorizeAttribute? authorization = typeof(DashboardController)
            .GetCustomAttribute<AuthorizeAttribute>();
        RouteAttribute? route = typeof(DashboardController)
            .GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal("api/dashboard", route?.Template);
    }

    [Theory]
    [InlineData(nameof(DashboardController.GetDashboard))]
    [InlineData(nameof(DashboardController.GetBookingHistory))]
    public void ReadEndpointsAllowOnlyExistingDashboardRoles(string methodName)
    {
        // Ensure neither endpoint weakens or invents role names in controller metadata.
        MethodInfo method = typeof(DashboardController).GetMethod(methodName)!;
        AuthorizeAttribute? authorization = method.GetCustomAttribute<AuthorizeAttribute>();
        string[] roles = authorization?.Roles?.Split(',') ?? [];

        Assert.Equal(3, roles.Length);
        Assert.Contains(nameof(UserRole.Prosumer), roles);
        Assert.Contains(nameof(UserRole.Backoffice), roles);
        Assert.Contains(nameof(UserRole.GridOperator), roles);
    }
}
