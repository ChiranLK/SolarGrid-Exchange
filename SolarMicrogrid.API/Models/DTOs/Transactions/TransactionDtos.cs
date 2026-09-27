/*
 * TransactionDtos.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines the public opaque-token, safe verification, and completed
 *           transfer contracts for the Member 4 QR workflow.
 * Security: These DTOs never expose stored hashes, actor identifiers, JWTs,
 *           credentials, or internal MongoDB transaction records.
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Transactions;

public sealed class IssueQrTransactionResponseDto
{
    public string ReservationId { get; set; } = string.Empty;

    public long ReservationVersion { get; set; }

    public string QrToken { get; set; } = string.Empty;

    public DateTime IssuedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class VerifyQrTransactionRequestDto
{
    [Required]
    [StringLength(128, MinimumLength = 32)]
    public string Token { get; set; } = string.Empty;
}

public sealed class VerifyQrTransactionResponseDto
{
    public string VerificationId { get; set; } = string.Empty;

    public string ReservationId { get; set; } = string.Empty;

    public string ReservationReference { get; set; } = string.Empty;

    public string ProsumerReference { get; set; } = string.Empty;

    public long ReservationVersion { get; set; }

    public string StationId { get; set; } = string.Empty;

    public string? StationName { get; set; }

    public DateTime ScheduledStartTimeUtc { get; set; }

    public DateTime ScheduledEndTimeUtc { get; set; }

    public decimal RequestedEnergyKwh { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime VerifiedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class CompleteQrTransactionRequestDto
{
    [Required]
    [StringLength(128, MinimumLength = 32)]
    public string VerificationId { get; set; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
}

public sealed class CompleteQrTransactionResponseDto
{
    public string ReservationId { get; set; } = string.Empty;

    public string ReservationReference { get; set; } = string.Empty;

    public string StationId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public long Version { get; set; }

    public DateTime CompletedAtUtc { get; set; }
}
