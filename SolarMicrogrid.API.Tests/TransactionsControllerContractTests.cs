/*
 * TransactionsControllerContractTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Verifies the QR transaction routes retain authentication and the
 *           exact Prosumer/GridOperator role boundary in OpenAPI controllers.
 * -----------------------------------------------------------------------------
 */

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolarMicrogrid.API.Controllers;
using SolarMicrogrid.API.Models.Entities;
using Xunit;

namespace SolarMicrogrid.API.Tests;

public sealed class TransactionsControllerContractTests
{
    [Fact]
    public void ControllerRequiresAuthenticationAndUsesTransactionRoute()
    {
        // Preserve the shared authenticated API boundary and stable transaction prefix.
        AuthorizeAttribute? authorization = typeof(TransactionsController)
            .GetCustomAttribute<AuthorizeAttribute>();
        RouteAttribute? route = typeof(TransactionsController)
            .GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal("api/transactions", route?.Template);
    }

    [Theory]
    [InlineData(nameof(TransactionsController.Issue), nameof(UserRole.Prosumer))]
    [InlineData(nameof(TransactionsController.Verify), nameof(UserRole.GridOperator))]
    [InlineData(nameof(TransactionsController.Complete), nameof(UserRole.GridOperator))]
    public void EndpointsUseExactExistingRole(string methodName, string expectedRole)
    {
        // Prevent accidental Backoffice access or invented aliases on sensitive QR operations.
        MethodInfo method = typeof(TransactionsController).GetMethod(methodName)!;
        AuthorizeAttribute? authorization = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(expectedRole, authorization?.Roles);
    }
}
