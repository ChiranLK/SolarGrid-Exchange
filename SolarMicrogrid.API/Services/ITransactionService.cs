/*
 * ITransactionService.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines the server-authoritative issue, verify, and complete
 *           operations for opaque reservation QR transactions.
 * -----------------------------------------------------------------------------
 */

using SolarMicrogrid.API.Models.DTOs.Transactions;

namespace SolarMicrogrid.API.Services;

public interface ITransactionService
{
    /// <summary>Issues one short-lived opaque QR token for an eligible owned reservation.</summary>
    Task<IssueQrTransactionResponseDto> IssueAsync(
        string actorNic,
        string actorRoleClaim,
        string reservationId,
        CancellationToken cancellationToken);

    /// <summary>Consumes an issued QR token and returns a separate one-time verification receipt.</summary>
    Task<VerifyQrTransactionResponseDto> VerifyAsync(
        string actorNic,
        string actorRoleClaim,
        VerifyQrTransactionRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Completes one verified reservation and transaction exactly once.</summary>
    Task<CompleteQrTransactionResponseDto> CompleteAsync(
        string actorNic,
        string actorRoleClaim,
        string reservationId,
        CompleteQrTransactionRequestDto request,
        CancellationToken cancellationToken);
}
