/*
 * UsersControllerAdministrationTests.cs
 * -----------------------------------------------------------------------------
 * File        : UsersControllerAdministrationTests.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Tests for the Backoffice administration endpoints on
 *               UsersController: Backoffice-only authorization, the caller NIC
 *               used for self-deactivation protection, successful responses,
 *               request DTO validation, and the shared error format.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SolarMicrogrid.API.Controllers;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Middleware;
using SolarMicrogrid.API.Models.DTOs;
using SolarMicrogrid.API.Models.DTOs.Users;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using Xunit;

namespace SolarMicrogrid.Tests;

public sealed class UsersControllerAdministrationTests
{
    public static TheoryData<string, UserRole, bool> BackofficeOnlyCases()
    {
        // Every Backoffice-only action paired with every role and whether it must be forbidden.
        var data = new TheoryData<string, UserRole, bool>();
        string[] actions =
        [
            nameof(UsersController.CreateStaffUser),
            nameof(UsersController.GetPending),
            nameof(UsersController.GetDeactivationRequests),
            nameof(UsersController.Activate),
            nameof(UsersController.Deactivate),
            nameof(UsersController.AssignStation)
        ];
        foreach (string action in actions)
        {
            data.Add(action, UserRole.Backoffice, false);
            data.Add(action, UserRole.GridOperator, true);
            data.Add(action, UserRole.Prosumer, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BackofficeOnlyCases))]
    public async Task AdministrationActions_AreBackofficeOnly(string actionName, UserRole role, bool expectForbidden)
    {
        // Proves each administration action's [Authorize(Roles)] admits only Backoffice.
        AuthorizeAttribute authorization = typeof(UsersController).GetMethod(actionName)!
            .GetCustomAttribute<AuthorizeAttribute>()!;
        Assert.Equal("Backoffice", authorization.Roles);

        PolicyAuthorizationResult result = await EvaluateAsync(authorization, role);

        Assert.Equal(expectForbidden, result.Forbidden);
        Assert.Equal(!expectForbidden, result.Succeeded);
    }

    [Fact]
    public async Task Deactivate_UsesCallerNicFromTokenToBlockSelfDeactivation()
    {
        // Proves the controller passes the signed-in NIC, so a Backoffice user cannot deactivate themselves.
        var (controller, store) = Create(UserAdministrationServiceTests.AdminNic,
            UserAdministrationServiceTests.Admin(),
            UserAdministrationServiceTests.Account(
                UserAdministrationServiceTests.OtherAdminNic, UserRole.Backoffice, UserStatus.Active));

        await Assert.ThrowsAsync<BadRequestException>(
            () => controller.Deactivate(UserAdministrationServiceTests.AdminNic, CancellationToken.None));

        Assert.Equal(UserStatus.Active, store.Get(UserAdministrationServiceTests.AdminNic)!.Status);
    }

    [Fact]
    public async Task Deactivate_WithoutNicClaimIsUnauthorized()
    {
        // Proves a token without the NIC claim cannot perform administration.
        var (controller, _) = Create(nic: null, UserAdministrationServiceTests.Admin());

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => controller.Deactivate(UserAdministrationServiceTests.ProsumerNic, CancellationToken.None));
    }

    [Fact]
    public async Task Deactivate_ReturnsOkWithSafeDto()
    {
        // Proves a successful deactivation returns 200 with the updated account.
        var (controller, _) = Create(UserAdministrationServiceTests.AdminNic,
            UserAdministrationServiceTests.Admin(),
            UserAdministrationServiceTests.Account(
                UserAdministrationServiceTests.ProsumerNic, UserRole.Prosumer, UserStatus.Active));

        ActionResult<UserResponseDto> result =
            await controller.Deactivate(UserAdministrationServiceTests.ProsumerNic, CancellationToken.None);

        var dto = Assert.IsType<UserResponseDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Deactivated", dto.Status);
    }

    [Fact]
    public async Task Activate_ReturnsOkForPendingAccount()
    {
        // Proves the preserved activation route still approves a pending Prosumer.
        var (controller, _) = Create(UserAdministrationServiceTests.AdminNic,
            UserAdministrationServiceTests.Admin(),
            UserAdministrationServiceTests.Account(
                UserAdministrationServiceTests.PendingNic, UserRole.Prosumer, UserStatus.PendingActivation));

        ActionResult<UserResponseDto> result =
            await controller.Activate(UserAdministrationServiceTests.PendingNic, CancellationToken.None);

        var dto = Assert.IsType<UserResponseDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Active", dto.Status);
    }

    [Fact]
    public async Task GetDeactivationRequests_ReturnsOkWithEmptyList()
    {
        // Proves the listing endpoint returns 200 with [] when nothing is pending.
        var (controller, _) = Create(UserAdministrationServiceTests.AdminNic, UserAdministrationServiceTests.Admin());

        ActionResult<List<DeactivationRequestResponseDto>> result =
            await controller.GetDeactivationRequests(CancellationToken.None);

        var list = Assert.IsType<List<DeactivationRequestResponseDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(list);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456789abcdef012345678")]
    public void AssignStationRequest_RejectsMissingOrOverlongId(string stationId)
    {
        // Proves [ApiController] validation returns 400 for an empty or too-long station ID.
        var request = new AssignStationRequestDto { StationId = stationId };

        Assert.False(Validator.TryValidateObject(
            request, new ValidationContext(request), new List<ValidationResult>(), validateAllProperties: true));
    }

    [Theory]
    [InlineData(typeof(ConflictException), StatusCodes.Status409Conflict)]
    [InlineData(typeof(NotFoundException), StatusCodes.Status404NotFound)]
    [InlineData(typeof(BadRequestException), StatusCodes.Status400BadRequest)]
    [InlineData(typeof(ForbiddenException), StatusCodes.Status403Forbidden)]
    [InlineData(typeof(UnauthorizedException), StatusCodes.Status401Unauthorized)]
    public async Task AdministrationFailures_UseTheSharedErrorResponse(Type exceptionType, int expectedStatus)
    {
        // Proves invalid transitions and assignment errors produce the shared { status, message } body.
        Exception exception = (Exception)Activator.CreateInstance(exceptionType, "This account is already deactivated.")!;
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionMiddleware(_ => throw exception, NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal($"{{\"status\":{expectedStatus},\"message\":\"This account is already deactivated.\"}}", body);
    }

    private static async Task<PolicyAuthorizationResult> EvaluateAsync(AuthorizeAttribute authorization, UserRole role)
    {
        // Evaluates an action's role requirement the way the ASP.NET Core pipeline does.
        AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole(authorization.Roles!.Split(','))
            .Build();
        using ServiceProvider services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var evaluator = new PolicyEvaluator(services.GetRequiredService<IAuthorizationService>());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role.ToString())], "test"));

        return await evaluator.AuthorizeAsync(
            policy,
            AuthenticateResult.Success(new AuthenticationTicket(principal, "test")),
            new DefaultHttpContext { User = principal },
            resource: null);
    }

    private static (UsersController Controller, InMemoryUserStore Store) Create(string? nic, params User[] users)
    {
        // Builds UsersController over an in-memory store with a signed-in Backoffice principal.
        var database = new MongoTestContext();
        var store = new InMemoryUserStore(database.Users, users);
        database.ReturnStations();
        var claims = new List<Claim> { new(ClaimTypes.Role, "Backoffice") };
        if (nic is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, nic));
        }

        var controller = new UsersController(new UserService(
            database.Context, new UserAdministrationServiceTests.FixedClock(UserAdministrationServiceTests.FixedNow)))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
            }
        };

        return (controller, store);
    }
}
