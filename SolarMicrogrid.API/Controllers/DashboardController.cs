/*
 * DashboardController.cs
 * -----------------------------------------------------------------------------
 * Purpose : Exposes authenticated, role-scoped Member 4 dashboard and booking
 *           history reads while keeping aggregation and authorization server-side.
 * Security: JWT claims identify the actor; DashboardService revalidates the
 *           persisted user, role, status, and assigned-station scope.
 * -----------------------------------------------------------------------------
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Dashboard;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;

namespace SolarMicrogrid.API.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
[Tags("Dashboard")]
[Produces("application/json")]
public sealed class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        // Keep HTTP concerns here and delegate every data, scope, and time decision to the service.
        _dashboardService = dashboardService;
    }

    [HttpGet]
    [Authorize(Roles =
        $"{nameof(UserRole.Prosumer)},{nameof(UserRole.Backoffice)},{nameof(UserRole.GridOperator)}")]
    [ProducesResponseType(typeof(DashboardResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DashboardResponseDto>> GetDashboard(
        [FromQuery] DashboardQueryDto query,
        CancellationToken cancellationToken)
    {
        // Use authenticated claims only and return the persisted actor's live dashboard scope.
        (string actorNic, string actorRole) = GetRequiredActorClaims();
        DashboardResponseDto response = await _dashboardService.GetDashboardAsync(
            actorNic,
            actorRole,
            query,
            cancellationToken);
        return Ok(response);
    }

    [HttpGet("history")]
    [Authorize(Roles =
        $"{nameof(UserRole.Prosumer)},{nameof(UserRole.Backoffice)},{nameof(UserRole.GridOperator)}")]
    [ProducesResponseType(typeof(PagedBookingHistoryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedBookingHistoryResponseDto>> GetBookingHistory(
        [FromQuery] BookingHistoryQueryDto query,
        CancellationToken cancellationToken)
    {
        // Apply owner/global/assigned-station scope before filtering, sorting, and paging history.
        (string actorNic, string actorRole) = GetRequiredActorClaims();
        PagedBookingHistoryResponseDto response = await _dashboardService.GetBookingHistoryAsync(
            actorNic,
            actorRole,
            query,
            cancellationToken);
        return Ok(response);
    }

    private (string ActorNic, string ActorRole) GetRequiredActorClaims()
    {
        // Reject a principal that lacks the NIC or exact role required by repository authorization.
        string? actorNic = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? actorRole = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrWhiteSpace(actorNic) || string.IsNullOrWhiteSpace(actorRole))
        {
            throw new UnauthorizedException("The access token is missing required identity claims.");
        }

        return (actorNic, actorRole);
    }
}
