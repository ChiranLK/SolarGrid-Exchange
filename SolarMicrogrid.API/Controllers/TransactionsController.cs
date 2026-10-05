/*
 * TransactionsController.cs
 * -----------------------------------------------------------------------------
 * Purpose : Exposes the authenticated two-step QR verification and completion
 *           workflow while delegating every authorization/state decision.
 * Security: Only claims identify the actor; request bodies cannot select an
 *           owner, operator, or station.
 * -----------------------------------------------------------------------------
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Filters;
using SolarMicrogrid.API.Models.DTOs.Transactions;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;

namespace SolarMicrogrid.API.Controllers;

[ApiController]
[Authorize]
[RequireActiveAccount]
[Route("api/transactions")]
[Tags("Transactions")]
[Produces("application/json")]
public sealed class TransactionsController : ControllerBase
{
    private readonly ITransactionService _transactionService;

    public TransactionsController(ITransactionService transactionService)
    {
        // Keep transport concerns here and delegate security/state decisions to the service.
        _transactionService = transactionService;
    }

    [HttpPost("reservations/{reservationId}/qr")]
    [Authorize(Roles = nameof(UserRole.Prosumer))]
    [ProducesResponseType(typeof(IssueQrTransactionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IssueQrTransactionResponseDto>> Issue(
        string reservationId,
        CancellationToken cancellationToken)
    {
        // Issue only from persisted Prosumer identity and an owner-scoped reservation lookup.
        (string actorNic, string actorRole) = GetRequiredActorClaims();
        IssueQrTransactionResponseDto response = await _transactionService.IssueAsync(
            actorNic,
            actorRole,
            reservationId,
            cancellationToken);
        return Ok(response);
    }

    [HttpPost("verify")]
    [Authorize(Roles = nameof(UserRole.GridOperator))]
    [ProducesResponseType(typeof(VerifyQrTransactionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VerifyQrTransactionResponseDto>> Verify(
        [FromBody] VerifyQrTransactionRequestDto request,
        CancellationToken cancellationToken)
    {
        // Submit only the scanned opaque token; operator/station identity comes from server state.
        (string actorNic, string actorRole) = GetRequiredActorClaims();
        VerifyQrTransactionResponseDto response = await _transactionService.VerifyAsync(
            actorNic,
            actorRole,
            request,
            cancellationToken);
        return Ok(response);
    }

    [HttpPost("reservations/{reservationId}/complete")]
    [Authorize(Roles = nameof(UserRole.GridOperator))]
    [ProducesResponseType(typeof(CompleteQrTransactionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CompleteQrTransactionResponseDto>> Complete(
        string reservationId,
        [FromBody] CompleteQrTransactionRequestDto request,
        CancellationToken cancellationToken)
    {
        // Require an explicit second request carrying only the one-time verification receipt.
        (string actorNic, string actorRole) = GetRequiredActorClaims();
        CompleteQrTransactionResponseDto response = await _transactionService.CompleteAsync(
            actorNic,
            actorRole,
            reservationId,
            request,
            cancellationToken);
        return Ok(response);
    }

    private (string ActorNic, string ActorRole) GetRequiredActorClaims()
    {
        // Reject a principal that lacks the exact identity claims used by shared authorization.
        string? actorNic = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? actorRole = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrWhiteSpace(actorNic) || string.IsNullOrWhiteSpace(actorRole))
        {
            throw new UnauthorizedException("The access token is missing required identity claims.");
        }

        return (actorNic, actorRole);
    }
}
