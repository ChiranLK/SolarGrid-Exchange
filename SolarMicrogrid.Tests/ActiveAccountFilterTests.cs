/*
 * ActiveAccountFilterTests.cs
 * -----------------------------------------------------------------------------
 * File        : ActiveAccountFilterTests.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Tests for [RequireActiveAccount]: an unexpired token belonging
 *               to a pending, deactivated, removed or role-changed account is
 *               stopped before the action runs, active accounts pass, and the
 *               attribute is applied to every protected Member 1 endpoint
 *               (but not to Member 3's eligible-prosumers search).
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using SolarMicrogrid.API.Controllers;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Filters;
using SolarMicrogrid.API.Models.Entities;
using Xunit;

namespace SolarMicrogrid.Tests;

public sealed class ActiveAccountFilterTests
{
    [Theory]
    [InlineData(UserRole.Backoffice)]
    [InlineData(UserRole.GridOperator)]
    [InlineData(UserRole.Prosumer)]
    public async Task ActiveAccount_ContinuesToTheAction(UserRole role)
    {
        // Proves an active account whose role matches its token is let through.
        const string nic = UserAdministrationServiceTests.AdminNic;
        bool reachedAction = await RunAsync(
            UserAdministrationServiceTests.Account(nic, role, UserStatus.Active),
            Principal(nic, role.ToString()));

        Assert.True(reachedAction);
    }

    [Fact]
    public async Task PendingAccountWithValidToken_IsForbidden()
    {
        // Proves a PendingActivation account cannot use protected Member 1 endpoints.
        const string nic = UserAdministrationServiceTests.PendingNic;

        ForbiddenException error = await Assert.ThrowsAsync<ForbiddenException>(() => RunAsync(
            UserAdministrationServiceTests.Account(nic, UserRole.Prosumer, UserStatus.PendingActivation),
            Principal(nic, "Prosumer")));

        Assert.Equal("This account is awaiting activation.", error.Message);
    }

    [Theory]
    [InlineData(UserRole.Backoffice)]
    [InlineData(UserRole.GridOperator)]
    [InlineData(UserRole.Prosumer)]
    public async Task DeactivatedAccountWithValidToken_IsForbidden(UserRole role)
    {
        // Proves an account deactivated after login is locked out despite its unexpired JWT.
        const string nic = UserAdministrationServiceTests.DeactivatedNic;
        bool reachedAction = false;

        ForbiddenException error = await Assert.ThrowsAsync<ForbiddenException>(async () =>
            reachedAction = await RunAsync(
                UserAdministrationServiceTests.Account(nic, role, UserStatus.Deactivated),
                Principal(nic, role.ToString())));

        Assert.Contains("deactivated", error.Message);
        Assert.False(reachedAction);
    }

    [Fact]
    public async Task TokenForMissingAccount_IsUnauthorized()
    {
        // Proves a token whose account no longer exists must sign in again.
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => RunAsync(account: null, Principal(UserAdministrationServiceTests.ProsumerNic, "Prosumer")));
    }

    [Fact]
    public async Task TokenRoleDifferentFromDatabase_IsUnauthorized()
    {
        // Proves a stale role claim (e.g. Backoffice token for a now-Prosumer account) is rejected.
        const string nic = UserAdministrationServiceTests.ProsumerNic;

        await Assert.ThrowsAsync<UnauthorizedException>(() => RunAsync(
            UserAdministrationServiceTests.Account(nic, UserRole.Prosumer, UserStatus.Active),
            Principal(nic, "Backoffice")));
    }

    [Fact]
    public async Task TokenWithoutNic_IsUnauthorized()
    {
        // Proves an authenticated principal without a NIC claim cannot continue.
        await Assert.ThrowsAsync<UnauthorizedException>(() => RunAsync(
            UserAdministrationServiceTests.Admin(),
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Backoffice")], "test"))));
    }

    [Fact]
    public async Task AnonymousRequest_IsLeftToAuthorize()
    {
        // Proves the filter does not replace [Authorize]; it only adds a check for signed-in callers.
        bool reachedAction = await RunAsync(account: null, new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.True(reachedAction);
    }

    [Theory]
    [InlineData(nameof(UsersController.CreateStaffUser))]
    [InlineData(nameof(UsersController.GetAll))]
    [InlineData(nameof(UsersController.GetByNic))]
    [InlineData(nameof(UsersController.UpdateDetails))]
    [InlineData(nameof(UsersController.GetPending))]
    [InlineData(nameof(UsersController.GetDeactivationRequests))]
    [InlineData(nameof(UsersController.Activate))]
    [InlineData(nameof(UsersController.Deactivate))]
    [InlineData(nameof(UsersController.AssignStation))]
    public void Member1UserActions_RequireActiveAccount(string actionName)
    {
        // Proves every Member 1 user-management action carries the status re-check.
        MethodInfo action = typeof(UsersController).GetMethod(actionName)!;

        Assert.NotNull(action.GetCustomAttribute<RequireActiveAccountAttribute>());
    }

    [Fact]
    public void AuthMe_RequiresActiveAccount_ButLoginAndRegisterDoNot()
    {
        // Proves session validation re-checks status while anonymous sign-in endpoints stay open.
        Assert.NotNull(typeof(AuthController).GetMethod(nameof(AuthController.Me))!
            .GetCustomAttribute<RequireActiveAccountAttribute>());
        Assert.Null(typeof(AuthController).GetMethod(nameof(AuthController.Login))!
            .GetCustomAttribute<RequireActiveAccountAttribute>());
        Assert.Null(typeof(AuthController).GetMethod(nameof(AuthController.Register))!
            .GetCustomAttribute<RequireActiveAccountAttribute>());
    }

    [Fact]
    public void Member3EligibleProsumerSearch_RequiresActiveStaffAccount()
    {
        // Proves the Member 3 search keeps its roles and now also blocks deactivated staff tokens.
        MethodInfo action = typeof(UsersController).GetMethod(nameof(UsersController.SearchEligibleProsumers))!;

        Assert.NotNull(action.GetCustomAttribute<RequireActiveAccountAttribute>());
        Assert.Null(typeof(UsersController).GetCustomAttribute<RequireActiveAccountAttribute>());
        Assert.Equal("Backoffice,GridOperator", action.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
    }

    [Fact]
    public void ReservationsController_RequiresActiveAccount()
    {
        // Proves every Component 3 reservation endpoint rechecks the account before the service runs.
        Assert.NotNull(typeof(ReservationsController).GetCustomAttribute<RequireActiveAccountAttribute>());
    }

    [Theory]
    [InlineData(typeof(StationsController))]
    [InlineData(typeof(SlotsController))]
    [InlineData(typeof(DashboardController))]
    [InlineData(typeof(TransactionsController))]
    [InlineData(typeof(ProsumersController))]
    public void RemainingProtectedControllers_RequireActiveAccount(Type controllerType)
    {
        // Proves a stale token cannot use any protected station, dashboard, QR or profile endpoint.
        Assert.NotNull(controllerType.GetCustomAttribute<RequireActiveAccountAttribute>());
    }

    private static ClaimsPrincipal Principal(string nic, string role)
    {
        // Builds an authenticated principal shaped like a validated SolarGrid JWT.
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, nic), new Claim(ClaimTypes.Role, role)],
            "test"));
    }

    private static async Task<bool> RunAsync(User? account, ClaimsPrincipal principal)
    {
        // Executes ActiveAccountFilter once and reports whether it let the request reach the action.
        var database = new MongoTestContext();
        _ = account is null
            ? new InMemoryUserStore(database.Users)
            : new InMemoryUserStore(database.Users, account);
        var filter = new ActiveAccountFilter(database.Context);

        var actionContext = new ActionContext(
            new DefaultHttpContext { User = principal },
            new RouteData(),
            new ActionDescriptor());
        var executing = new ResourceExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new List<IValueProviderFactory>());

        bool reachedAction = false;
        await filter.OnResourceExecutionAsync(executing, () =>
        {
            reachedAction = true;
            return Task.FromResult(new ResourceExecutedContext(actionContext, new List<IFilterMetadata>()));
        });

        return reachedAction;
    }
}
