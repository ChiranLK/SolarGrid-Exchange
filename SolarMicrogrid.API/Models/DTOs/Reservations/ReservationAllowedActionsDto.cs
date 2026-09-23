/*
 * ReservationAllowedActionsDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Returns actor-scoped actions calculated by the central API so web and
 *           Android clients do not reproduce reservation authorization rules.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Models.DTOs.Reservations;

public sealed class ReservationAllowedActionsDto
{
    public bool CanUpdate { get; set; }

    public string? UpdateUnavailableReason { get; set; }

    public bool CanCancel { get; set; }

    public string? CancelUnavailableReason { get; set; }

    public bool CanApprove { get; set; }

    public string? ApproveUnavailableReason { get; set; }

    public bool CanReject { get; set; }

    public string? RejectUnavailableReason { get; set; }

    public bool CanGetQr { get; set; }

    public string? GetQrUnavailableReason { get; set; }

    public bool CanVerifyQr { get; set; }

    public string? VerifyQrUnavailableReason { get; set; }

    public bool CanComplete { get; set; }

    public string? CompleteUnavailableReason { get; set; }
}
