/*
 * ProsumersControllerTests.cs
 * -----------------------------------------------------------------------------
 * File        : ProsumersControllerTests.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Tests for ProsumersController: Prosumer-only authorization,
 *               caller NIC taken from the JWT (never the route or body),
 *               request DTO validation and protected-field shape, and the
 *               shared error-response status mapping.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SolarMicrogrid.API.Controllers;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Helpers;
using SolarMicrogrid.API.Middleware;
using SolarMicrogrid.API.Models.DTOs;
using SolarMicrogrid.API.Models.DTOs.Prosumers;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Settings;
using Xunit;

namespace SolarMicrogrid.Tests;

public sealed class ProsumersControllerTests
{
    [Theory]
    [InlineData(UserRole.Prosumer, false)]
    [InlineData(UserRole.GridOperator, true)]
    [InlineData(UserRole.Backoffice, true)]
    public async Task ControllerPolicy_AllowsOnlyProsumers(UserRole role, bool expectForbidden)
    {
        // Proves the class-level [Authorize] admits Prosumers and forbids both staff roles.
        PolicyAuthorizationResult result = await EvaluatePolicyAsync(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role.ToString())], "test")));

        Assert.Equal(expectForbidden, result.Forbidden);
        Assert.Equal(!expectForbidden, result.Succeeded);
    }

    [Fact]
    public async Task ControllerPolicy_ChallengesAnonymousCallers()
    {
        // Proves an unauthenticated request is challenged (401), not allowed through.
        PolicyAuthorizationResult result = await EvaluatePolicyAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.True(result.Challenged);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Actions_DoNotOverrideControllerAuthorization()
    {
        // Proves no action re-opens access with [AllowAnonymous] or a broader role list.
        foreach (MethodInfo action in ActionMethods())
        {
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Null(action.GetCustomAttribute<AuthorizeAttribute>());
        }
    }

    [Fact]
    public void Actions_NeverAcceptANicFromTheRequest()
    {
        // Proves ownership: there is no route/body/query NIC a caller could use to target another user.
        foreach (MethodInfo action in ActionMethods())
        {
            Assert.DoesNotContain(
                action.GetParameters(),
                parameter => parameter.Name!.Contains("nic", StringComparison.OrdinalIgnoreCase));
        }

        Assert.Null(typeof(UpdateProfileDto).GetProperty("Nic"));
    }

    [Fact]
    public void UpdateProfileDto_HasNoProtectedFields()
    {
        // Proves model binding cannot carry NIC, role, status, password or activation fields.
        string[] allowed = ["FullName", "Email", "Phone", "Address"];

        Assert.Equal(
            allowed.OrderBy(name => name),
            typeof(UpdateProfileDto).GetProperties().Select(property => property.Name).OrderBy(name => name));
    }

    [Theory]
    [InlineData("", "nimal@example.com", "0771234567", null)]
    [InlineData("Nimal", "not-an-email", "0771234567", null)]
    [InlineData("Nimal", "nimal@example.com", "", null)]
    [InlineData("Nimal", "nimal@example.com", "012345678901234567890", null)]
    [InlineData("Nimal", "nimal@example.com", "0771234567", "x")]
    public void UpdateProfileDto_RejectsInvalidInput(string fullName, string email, string phone, string? addressSeed)
    {
        // Proves the [ApiController] model validation layer rejects bad input with 400 before the service.
        var request = new UpdateProfileDto
        {
            FullName = fullName,
            Email = email,
            Phone = phone,
            Address = addressSeed is null ? null : new string('a', 201)
        };

        bool valid = Validator.TryValidateObject(
            request, new ValidationContext(request), new List<ValidationResult>(), validateAllProperties: true);

        Assert.False(valid);
    }

    [Fact]
    public async Task GetMyProfile_UsesTheNicFromTheCallersToken()
    {
        // Proves the controller passes the JWT NIC to the service and returns 200 with the profile.
        var fixture = new ProsumerServiceTests.Fixture(ProsumerServiceTests.Prosumer());
        ProsumersController controller = ControllerFor(fixture, ProsumerServiceTests.CallerNic);

        ActionResult<ProsumerProfileResponseDto> result = await controller.GetMyProfile(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<ProsumerProfileResponseDto>(ok.Value);
        Assert.Equal(ProsumerServiceTests.CallerNic, profile.Nic);
    }

    [Fact]
    public async Task UpdateMyProfile_ReturnsUpdatedProfile()
    {
        // Proves a successful update returns 200 with the saved values.
        var fixture = new ProsumerServiceTests.Fixture(ProsumerServiceTests.Prosumer());
        ProsumersController controller = ControllerFor(fixture, ProsumerServiceTests.CallerNic);

        ActionResult<ProsumerProfileResponseDto> result = await controller.UpdateMyProfile(
            new UpdateProfileDto
            {
                FullName = "Nimal P",
                Email = "nimal.p@example.com",
                Phone = "0711111111"
            },
            CancellationToken.None);

        var profile = Assert.IsType<ProsumerProfileResponseDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Nimal P", profile.FullName);
        Assert.Equal("nimal.p@example.com", profile.Email);
        Assert.Null(profile.Address);
    }

    [Fact]
    public async Task RequestDeactivation_ReturnsFlaggedProfile()
    {
        // Proves the deactivation-request action returns 200 with the pending flag set.
        var fixture = new ProsumerServiceTests.Fixture(ProsumerServiceTests.Prosumer());
        ProsumersController controller = ControllerFor(fixture, ProsumerServiceTests.CallerNic);

        ActionResult<ProsumerProfileResponseDto> result = await controller.RequestDeactivation(CancellationToken.None);

        var profile = Assert.IsType<ProsumerProfileResponseDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(profile.DeactivationRequested);
    }

    [Fact]
    public async Task Actions_WithoutNicClaimAreUnauthorized()
    {
        // Proves a token missing the NameIdentifier claim cannot reach the service.
        var fixture = new ProsumerServiceTests.Fixture(ProsumerServiceTests.Prosumer());
        ProsumersController controller = ControllerFor(fixture, nic: null);

        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.GetMyProfile(CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.RequestDeactivation(CancellationToken.None));
        Assert.Empty(fixture.UserFilters);
    }

    [Fact]
    public void JwtIssuedAtLogin_ExposesNicAsNameIdentifierAfterValidation()
    {
        // Proves the claim the controller reads is the one JwtHelper writes, after inbound claim mapping.
        var settings = new JwtSettings
        {
            Key = "unit-test-signing-key-that-is-long-enough-123",
            Issuer = "test-issuer",
            Audience = "test-audience",
            ExpirationMinutes = 5
        };
        string token = new JwtHelper(Options.Create(settings)).GenerateToken(ProsumerServiceTests.Prosumer());

        ClaimsPrincipal principal = new JwtSecurityTokenHandler().ValidateToken(
            token,
            new TokenValidationParameters
            {
                ValidIssuer = settings.Issuer,
                ValidAudience = settings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key))
            },
            out _);

        Assert.Equal(ProsumerServiceTests.CallerNic, principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(principal.IsInRole("Prosumer"));
    }

    [Theory]
    [InlineData(typeof(UnauthorizedException), StatusCodes.Status401Unauthorized)]
    [InlineData(typeof(BadRequestException), StatusCodes.Status400BadRequest)]
    [InlineData(typeof(ForbiddenException), StatusCodes.Status403Forbidden)]
    [InlineData(typeof(ConflictException), StatusCodes.Status409Conflict)]
    public async Task ProfileFailures_UseTheSharedErrorResponse(Type exceptionType, int expectedStatus)
    {
        // Proves every exception ProsumerService throws maps to its HTTP status with the shared message body.
        Exception exception = (Exception)Activator.CreateInstance(exceptionType, "profile failure")!;
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionMiddleware(_ => throw exception, NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Contains("\"message\":\"profile failure\"", body);
    }

    private static IEnumerable<MethodInfo> ActionMethods()
    {
        // Lists the public HTTP actions declared on ProsumersController.
        return typeof(ProsumersController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes().Any(attribute =>
                attribute is Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute));
    }

    private static async Task<PolicyAuthorizationResult> EvaluatePolicyAsync(ClaimsPrincipal principal)
    {
        // Evaluates the controller's real [Authorize(Roles)] requirement the way the pipeline does.
        AuthorizeAttribute authorization = typeof(ProsumersController).GetCustomAttribute<AuthorizeAttribute>()!;
        Assert.Equal("Prosumer", authorization.Roles);
        AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole(authorization.Roles!.Split(','))
            .Build();
        using ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .AddAuthorization()
            .BuildServiceProvider();
        var evaluator = new PolicyEvaluator(services.GetRequiredService<IAuthorizationService>());
        var context = new DefaultHttpContext { User = principal };
        AuthenticateResult authenticated = principal.Identity?.IsAuthenticated == true
            ? AuthenticateResult.Success(new AuthenticationTicket(principal, "test"))
            : AuthenticateResult.NoResult();

        return await evaluator.AuthorizeAsync(policy, authenticated, context, resource: null);
    }

    private static ProsumersController ControllerFor(ProsumerServiceTests.Fixture fixture, string? nic)
    {
        // Builds the controller with a signed-in Prosumer principal carrying the given NIC claim.
        var claims = new List<Claim> { new(ClaimTypes.Role, "Prosumer") };
        if (nic is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, nic));
        }

        return new ProsumersController(fixture.Service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
                }
            }
        };
    }
}
