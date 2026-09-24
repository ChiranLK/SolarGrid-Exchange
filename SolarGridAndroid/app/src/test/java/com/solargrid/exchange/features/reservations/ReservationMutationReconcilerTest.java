package com.solargrid.exchange.features.reservations;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import com.solargrid.exchange.data.model.Reservation;

import org.junit.Test;

import java.util.Collections;

public final class ReservationMutationReconcilerTest {
    @Test
    public void approvedMaterialUpdateMustReturnAsNewerPendingState() {
        Reservation updated = reservation("Pending", 4, "slot-2", 6, "", "");

        assertTrue(ReservationMutationReconciler.matchesUpdate(
                updated, 3, "Approved", "slot-1", 4, "slot-2", 6));
        assertFalse(ReservationMutationReconciler.matchesUpdate(
                reservation("Approved", 4, "slot-2", 6, "", ""),
                3, "Approved", "slot-1", 4, "slot-2", 6));
    }

    @Test
    public void staleOrDifferentUpdateIsNotReportedAsSuccessful() {
        assertFalse(ReservationMutationReconciler.matchesUpdate(
                reservation("Pending", 3, "slot-2", 6, "", ""),
                3, "Pending", "slot-1", 4, "slot-2", 6));
        assertFalse(ReservationMutationReconciler.matchesUpdate(
                reservation("Pending", 4, "slot-3", 6, "", ""),
                3, "Pending", "slot-1", 4, "slot-2", 6));
    }

    @Test
    public void cancellationRequiresNewerCancelledServerResult() {
        assertTrue(ReservationMutationReconciler.matchesCancellation(
                reservation("Cancelled", 4, "slot-1", 4, "Weather", "2026-09-25T09:00:00Z"),
                3, "Weather"));
        assertFalse(ReservationMutationReconciler.matchesCancellation(
                reservation("Completed", 4, "slot-1", 4, "", ""),
                3, ""));
    }

    @Test
    public void cancellationDoesNotClaimAConflictingReason() {
        assertFalse(ReservationMutationReconciler.matchesCancellation(
                reservation("Cancelled", 4, "slot-1", 4, "Other", "2026-09-25T09:00:00Z"),
                3, "Weather"));
    }

    private static Reservation reservation(
            String status,
            long version,
            String slotId,
            double energy,
            String cancellationReason,
            String cancelledAtUtc) {
        return new Reservation(
                "reservation-1",
                "prosumer-nic",
                "Prosumer",
                "station-1",
                "Station",
                "Address",
                slotId,
                "Available",
                "2026-09-26T10:00:00Z",
                "2026-09-26T11:00:00Z",
                energy,
                status,
                version,
                false,
                null,
                "2026-09-24T10:00:00Z",
                "2026-09-25T09:00:00Z",
                cancelledAtUtc,
                cancellationReason,
                "",
                Collections.emptyList());
    }
}
