/*
 * ReservationQrActionPolicyTests.cs
 * -----------------------------------------------------------------------------
 * Purpose : Keeps the Component 3 QR allowed-action projection aligned with the
 *           authoritative Component 4 rule in TransactionService (Approved,
 *           capacity Held, not yet ended), so clients never offer a QR action
 *           that the API would always refuse.
 * -----------------------------------------------------------------------------
 */

using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;
using Xunit;

namespace SolarMicrogrid.API.Tests;

public sealed class ReservationQrActionPolicyTests
{
    private const string OwnerNic = "200012345678";
    private const string StationId = "000000000000000000000001";
    private static readonly DateTime NowUtc = new(2026, 9, 24, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ApprovedHeldFutureReservation_OffersQrToOwnerAndAssignedOperator()
    {
        // The valid case must remain available.
        EnergyReservation reservation = Reservation(ReservationStatus.Approved, ReservationCapacityState.Held, NowUtc.AddHours(2));

        Assert.True(Actions(reservation, Owner()).CanGetQr);
        Assert.True(Actions(reservation, Operator()).CanVerifyQr);
        Assert.True(ReservationReadPolicy.IsQrUsable(reservation, NowUtc));
    }

    [Fact]
    public void CurrentReservation_StillOffersQrUntilItEnds()
    {
        // Issuance is allowed during the slot; the end is exclusive.
        EnergyReservation started = Reservation(ReservationStatus.Approved, ReservationCapacityState.Held, NowUtc.AddMinutes(-30));

        Assert.True(Actions(started, Owner()).CanGetQr);
        Assert.False(ReservationReadPolicy.IsQrUsable(started, started.ScheduledEndTimeUtc));
    }

    [Fact]
    public void EndedApprovedReservation_NoLongerOffersQr()
    {
        // Previously the flag was status-only and the QR button always failed with 409 here.
        EnergyReservation ended = Reservation(ReservationStatus.Approved, ReservationCapacityState.Held, NowUtc.AddHours(-3));

        var owner = Actions(ended, Owner());
        Assert.False(owner.CanGetQr);
        Assert.Contains("ended", owner.GetQrUnavailableReason);
        Assert.False(Actions(ended, Operator()).CanVerifyQr);
    }

    [Theory]
    [InlineData(ReservationCapacityState.CompensationRequired)]
    [InlineData(ReservationCapacityState.ReleasePending)]
    [InlineData(ReservationCapacityState.Consumed)]
    public void ApprovedReservationWithoutHeldCapacity_DoesNotOfferQr(ReservationCapacityState state)
    {
        // TransactionService refuses anything whose capacity is not Held.
        EnergyReservation reservation = Reservation(ReservationStatus.Approved, state, NowUtc.AddHours(2));

        var owner = Actions(reservation, Owner());
        Assert.False(owner.CanGetQr);
        Assert.Contains("reconciled", owner.GetQrUnavailableReason);
    }

    [Theory]
    [InlineData(ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Rejected)]
    [InlineData(ReservationStatus.Completed)]
    public void NonApprovedReservation_DoesNotOfferQr(ReservationStatus status)
    {
        // Cancellation, rejection, a material update back to Pending and completion all stop QR use.
        EnergyReservation reservation = Reservation(status, ReservationCapacityState.Held, NowUtc.AddHours(2));

        Assert.False(Actions(reservation, Owner()).CanGetQr);
        Assert.Equal("Only approved reservations are eligible for a QR code.", Actions(reservation, Owner()).GetQrUnavailableReason);
    }

    private static Models.DTOs.Reservations.ReservationAllowedActionsDto Actions(EnergyReservation reservation, User actor)
    {
        // Call the production projection exactly as the reservation list/detail mappers do.
        return ReservationReadPolicy.BuildAllowedActions(reservation, actor, NowUtc, minimumChangeNoticeHours: 12);
    }

    private static User Owner()
    {
        // The owning active Prosumer.
        return new User { Nic = OwnerNic, Role = UserRole.Prosumer, Status = UserStatus.Active };
    }

    private static User Operator()
    {
        // An active Grid Operator assigned to the reservation's station.
        return new User
        {
            Nic = "199912345678",
            Role = UserRole.GridOperator,
            Status = UserStatus.Active,
            AssignedStationId = StationId
        };
    }

    private static EnergyReservation Reservation(ReservationStatus status, ReservationCapacityState capacityState, DateTime startUtc)
    {
        // Minimal reservation fields read by the allowed-action projection.
        return new EnergyReservation
        {
            Id = "000000000000000000000010",
            ProsumerNic = OwnerNic,
            StationId = StationId,
            SlotId = "000000000000000000000020",
            Status = status,
            CapacityState = capacityState,
            ScheduledStartTimeUtc = startUtc,
            ScheduledEndTimeUtc = startUtc.AddHours(1),
            CreatedAtUtc = NowUtc.AddDays(-1),
            UpdatedAtUtc = NowUtc.AddDays(-1)
        };
    }
}
