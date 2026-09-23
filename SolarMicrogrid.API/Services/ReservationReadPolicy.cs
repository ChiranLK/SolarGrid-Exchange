/*
 * ReservationReadPolicy.cs
 * -----------------------------------------------------------------------------
 * Purpose : Centralizes reservation read scope, dashboard view definitions,
 *           paging math, and actor-scoped action availability for API clients.
 * Security: Scope predicates are translated into MongoDB filters before paging.
 * -----------------------------------------------------------------------------
 */

using System.Linq.Expressions;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Reservations;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Services;

internal static class ReservationReadPolicy
{
    internal static Expression<Func<EnergyReservation, bool>> BuildScopePredicate(User actor)
    {
        // Return a database-translatable owner or assigned-station predicate for the actor.
        return actor.Role switch
        {
            UserRole.Prosumer => reservation => reservation.ProsumerNic == actor.Nic,
            UserRole.Backoffice => reservation => true,
            UserRole.GridOperator when !string.IsNullOrWhiteSpace(actor.AssignedStationId) =>
                reservation => reservation.StationId == actor.AssignedStationId,
            UserRole.GridOperator => throw new ForbiddenException(
                "The Grid Operator does not have a valid assigned station."),
            _ => throw new ForbiddenException("This user role cannot read reservations.")
        };
    }

    internal static Expression<Func<EnergyReservation, bool>> BuildViewPredicate(
        ReservationListView view,
        DateTime serverNowUtc)
    {
        // Define mutually useful dashboard views without dropping final reservation records.
        return view switch
        {
            ReservationListView.All => reservation => true,
            ReservationListView.Pending => reservation =>
                reservation.Status == ReservationStatus.Pending &&
                reservation.ScheduledEndTimeUtc > serverNowUtc,
            ReservationListView.Current => reservation =>
                reservation.Status == ReservationStatus.Approved &&
                reservation.ScheduledStartTimeUtc <= serverNowUtc &&
                reservation.ScheduledEndTimeUtc > serverNowUtc,
            ReservationListView.ApprovedFuture => reservation =>
                reservation.Status == ReservationStatus.Approved &&
                reservation.ScheduledStartTimeUtc > serverNowUtc,
            ReservationListView.History => reservation =>
                reservation.Status == ReservationStatus.Cancelled ||
                reservation.Status == ReservationStatus.Rejected ||
                reservation.Status == ReservationStatus.Completed ||
                ((reservation.Status == ReservationStatus.Pending ||
                  reservation.Status == ReservationStatus.Approved) &&
                 reservation.ScheduledEndTimeUtc <= serverNowUtc),
            _ => throw new ArgumentOutOfRangeException(nameof(view), "Unknown reservation view.")
        };
    }

    internal static ReservationAllowedActionsDto BuildAllowedActions(
        EnergyReservation reservation,
        User actor,
        DateTime serverNowUtc,
        int minimumChangeNoticeHours)
    {
        // Derive actions and denial reasons only from authoritative actor and reservation state.
        bool ownerProsumer = actor.Role == UserRole.Prosumer &&
            string.Equals(actor.Nic, reservation.ProsumerNic, StringComparison.Ordinal);
        bool noticeSatisfied = reservation.ScheduledStartTimeUtc - serverNowUtc >=
            TimeSpan.FromHours(minimumChangeNoticeHours);
        bool mutable = reservation.Status is ReservationStatus.Pending or ReservationStatus.Approved;
        bool assignedGridOperator = actor.Role == UserRole.GridOperator &&
            string.Equals(actor.AssignedStationId, reservation.StationId, StringComparison.Ordinal);
        bool pendingDecisionAvailable = reservation.Status == ReservationStatus.Pending &&
            reservation.ScheduledStartTimeUtc > serverNowUtc;
        bool staffCanApprove =
            (actor.Role == UserRole.Backoffice || assignedGridOperator) &&
            pendingDecisionAvailable;
        bool backofficeCanReject = actor.Role == UserRole.Backoffice && pendingDecisionAvailable;
        bool staffCanUpdate = actor.Role == UserRole.Backoffice || assignedGridOperator;
        bool staffCanCancel = actor.Role == UserRole.Backoffice || assignedGridOperator;
        bool canUpdate = (ownerProsumer || staffCanUpdate) && mutable && noticeSatisfied;
        bool canCancel = (ownerProsumer || staffCanCancel) && mutable && noticeSatisfied;
        bool canGetQr = ownerProsumer && reservation.Status == ReservationStatus.Approved;
        bool canVerifyQr = assignedGridOperator && reservation.Status == ReservationStatus.Approved;

        return new ReservationAllowedActionsDto
        {
            CanUpdate = canUpdate,
            UpdateUnavailableReason = canUpdate
                ? null
                : BuildMutationUnavailableReason(
                    ownerProsumer || staffCanUpdate,
                    mutable,
                    noticeSatisfied,
                    reservation.Status,
                    "update"),
            CanCancel = canCancel,
            CancelUnavailableReason = canCancel
                ? null
                : BuildMutationUnavailableReason(
                    ownerProsumer || staffCanCancel,
                    mutable,
                    noticeSatisfied,
                    reservation.Status,
                    "cancel"),
            CanApprove = staffCanApprove,
            ApproveUnavailableReason = staffCanApprove
                ? null
                : BuildDecisionUnavailableReason(
                    actor.Role == UserRole.Backoffice || assignedGridOperator,
                    reservation,
                    serverNowUtc,
                    "approve"),
            CanReject = backofficeCanReject,
            RejectUnavailableReason = backofficeCanReject
                ? null
                : BuildDecisionUnavailableReason(
                    actor.Role == UserRole.Backoffice,
                    reservation,
                    serverNowUtc,
                    "reject"),
            CanGetQr = canGetQr,
            GetQrUnavailableReason = canGetQr
                ? null
                : ownerProsumer
                    ? "Only approved reservations are eligible for a QR code."
                    : "Only the owning Prosumer may retrieve the QR code.",
            CanVerifyQr = canVerifyQr,
            VerifyQrUnavailableReason = canVerifyQr
                ? null
                : assignedGridOperator
                    ? "Only approved reservations may be verified."
                    : "Only the assigned Grid Operator may verify this reservation.",
            CanComplete = false,
            CompleteUnavailableReason =
                "Completion requires a current Component 4 verification receipt."
        };
    }

    internal static int CalculateTotalPages(long totalCount, int pageSize)
    {
        // Return the shared zero-for-empty page count without hard-coded record totals.
        if (totalCount <= 0)
        {
            return 0;
        }

        long totalPages = (totalCount + pageSize - 1) / pageSize;
        return checked((int)totalPages);
    }

    private static string BuildMutationUnavailableReason(
        bool authorized,
        bool mutable,
        bool noticeSatisfied,
        ReservationStatus status,
        string action)
    {
        // Return one deterministic reason in authorization, lifecycle, then time order.
        string pastTenseAction = action == "cancel" ? "cancelled" : "updated";
        if (!authorized)
        {
            return $"The authenticated user is not authorized to {action} this reservation.";
        }

        if (!mutable)
        {
            return $"A {status} reservation cannot be {pastTenseAction}.";
        }

        if (!noticeSatisfied)
        {
            return $"The minimum notice period to {action} this reservation has passed.";
        }

        return $"The reservation cannot be {pastTenseAction}.";
    }

    private static string BuildDecisionUnavailableReason(
        bool authorized,
        EnergyReservation reservation,
        DateTime serverNowUtc,
        string action)
    {
        // Return the role/status/time reason that prevents a staff decision.
        string pastTenseAction = action == "reject" ? "rejected" : "approved";
        if (!authorized)
        {
            return $"The authenticated staff user is not authorized to {action} this reservation.";
        }

        if (reservation.Status != ReservationStatus.Pending)
        {
            return $"Only pending reservations may be {pastTenseAction}.";
        }

        if (reservation.ScheduledStartTimeUtc <= serverNowUtc)
        {
            return $"A reservation that has started cannot be {pastTenseAction}.";
        }

        return $"The reservation cannot be {pastTenseAction}.";
    }
}
