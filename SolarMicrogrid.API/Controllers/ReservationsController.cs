/*
 * ReservationsController.cs
 * -----------------------------------------------------------------------------
 * Purpose : Exposes authenticated Component 3 reservation REST operations while
 *           delegating identity, permission, validation, and capacity rules to
 *           ReservationService.
 * -----------------------------------------------------------------------------
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Reservations;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;

namespace SolarMicrogrid.API.Controllers;

[ApiController]
[Authorize]
[Route("api/reservations")]
public sealed class ReservationsController : ControllerBase
{
    private readonly ReservationService _reservationService;

    public ReservationsController(ReservationService reservationService)
    {
        // Keep the controller thin by delegating the complete workflow to the domain service.
        _reservationService = reservationService;
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Prosumer))]
    public async Task<ActionResult<ReservationResponseDto>> CreateOwnReservation(
        [FromBody] CreateReservationRequestDto request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Read actor claims only; the request cannot select a different reservation owner.
        (string actorNic, string actorRole) = GetRequiredActorClaims();
        ReservationCreationResult result = await _reservationService.CreateOwnReservationAsync(
            actorNic,
            actorRole,
            request,
            idempotencyKey ?? string.Empty,
            cancellationToken);
        SetReplayHeader(result.IdempotencyReplayed);
        return Created($"/api/reservations/{result.Reservation.Id}", result.Reservation);
    }

    [HttpPost("staff")]
    [Authorize(Roles = $"{nameof(UserRole.Backoffice)},{nameof(UserRole.GridOperator)}")]
    public async Task<ActionResult<ReservationResponseDto>> CreateReservationForProsumer(
        [FromBody] StaffCreateReservationRequestDto request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Pass the authenticated staff actor and proposed target to service-level authorization.
        (string actorNic, string actorRole) = GetRequiredActorClaims();
        ReservationCreationResult result =
            await _reservationService.CreateReservationForProsumerAsync(
                actorNic,
                actorRole,
                request,
                idempotencyKey ?? string.Empty,
                cancellationToken);
        SetReplayHeader(result.IdempotencyReplayed);
        return Created($"/api/reservations/{result.Reservation.Id}", result.Reservation);
    }

    [HttpPut("{reservationId}")]
    [Authorize(Roles =
        $"{nameof(UserRole.Prosumer)},{nameof(UserRole.Backoffice)},{nameof(UserRole.GridOperator)}")]
    public async Task<ActionResult<ReservationResponseDto>> UpdateReservation(
        string reservationId,
        [FromBody] UpdateReservationRequestDto request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Pass only authenticated actor claims and client-editable fields to the update workflow.
        (string actorNic, string actorRole) = GetRequiredActorClaims();
        ReservationUpdateResult result = await _reservationService.UpdateReservationAsync(
            actorNic,
            actorRole,
            reservationId,
            request,
            idempotencyKey ?? string.Empty,
            cancellationToken);
        SetReplayHeader(result.IdempotencyReplayed);
        return Ok(result.Reservation);
    }

    [HttpPost("{reservationId}/cancel")]
    [Authorize(Roles =
        $"{nameof(UserRole.Prosumer)},{nameof(UserRole.Backoffice)},{nameof(UserRole.GridOperator)}")]
    public async Task<ActionResult<ReservationResponseDto>> CancelReservation(
        string reservationId,
        [FromBody] CancelReservationRequestDto request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Pass actor claims, expected version, and optional reason to the capacity-safe cancellation workflow.
        (string actorNic, string actorRole) = GetRequiredActorClaims();
        ReservationCancellationResult result = await _reservationService.CancelReservationAsync(
            actorNic,
            actorRole,
            reservationId,
            request,
            idempotencyKey ?? string.Empty,
            cancellationToken);
        SetReplayHeader(result.IdempotencyReplayed);
        return Ok(result.Reservation);
    }

    [HttpPost("{reservationId}/approve")]
    [Authorize(Roles = $"{nameof(UserRole.Backoffice)},{nameof(UserRole.GridOperator)}")]
    public async Task<ActionResult<ReservationResponseDto>> ApproveReservation(
        string reservationId,
        [FromBody] ApproveReservationRequestDto request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Delegate staff identity, current-state validation, and approval auditing to the domain service.
        (string actorNic, string actorRole) = GetRequiredActorClaims();
        ReservationApprovalResult result = await _reservationService.ApproveReservationAsync(
            actorNic,
            actorRole,
            reservationId,
            request,
            idempotencyKey ?? string.Empty,
            cancellationToken);
        SetReplayHeader(result.IdempotencyReplayed);
        return Ok(result.Reservation);
    }

    [HttpPost("{reservationId}/reject")]
    [Authorize(Roles = nameof(UserRole.Backoffice))]
    public async Task<ActionResult<ReservationResponseDto>> RejectReservation(
        string reservationId,
        [FromBody] RejectReservationRequestDto request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Delegate Backoffice identity, rejection auditing, and exact capacity release to the service.
        (string actorNic, string actorRole) = GetRequiredActorClaims();
        ReservationRejectionResult result = await _reservationService.RejectReservationAsync(
            actorNic,
            actorRole,
            reservationId,
            request,
            idempotencyKey ?? string.Empty,
            cancellationToken);
        SetReplayHeader(result.IdempotencyReplayed);
        return Ok(result.Reservation);
    }

    private (string ActorNic, string ActorRole) GetRequiredActorClaims()
    {
        // Reject an authenticated principal that lacks the repository's required NIC or role claims.
        string? actorNic = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? actorRole = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrWhiteSpace(actorNic) || string.IsNullOrWhiteSpace(actorRole))
        {
            throw new UnauthorizedException("The access token is missing required identity claims.");
        }

        return (actorNic, actorRole);
    }

    private void SetReplayHeader(bool replayed)
    {
        // Tell clients that a safe retry returned the existing mutation result.
        if (replayed)
        {
            Response.Headers["Idempotency-Replayed"] = "true";
        }
    }
}
