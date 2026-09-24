package com.solargrid.exchange.features.reservations;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNull;

import com.solargrid.exchange.data.model.Reservation;

import org.junit.Test;

import java.util.Arrays;
import java.util.Collections;
import java.util.HashSet;

public final class ReservationCreationReconcilerTest {
    @Test
    public void findsOnlyNewPendingReservationForExactRequest() {
        Reservation baseline = reservation("old", "slot-1", 4.5, "Pending");
        Reservation created = reservation("new", "slot-1", 4.5, "Pending");

        Reservation match = ReservationCreationReconciler.findNewPendingMatch(
                Arrays.asList(created, baseline),
                new HashSet<>(Collections.singletonList("old")),
                "slot-1",
                4.5);

        assertEquals("new", match.getId());
    }

    @Test
    public void doesNotTreatBaselineOrNonPendingReservationAsCreatedResult() {
        Reservation baseline = reservation("old", "slot-1", 4.5, "Pending");
        Reservation approved = reservation("new", "slot-1", 4.5, "Approved");

        Reservation match = ReservationCreationReconciler.findNewPendingMatch(
                Arrays.asList(approved, baseline),
                new HashSet<>(Collections.singletonList("old")),
                "slot-1",
                4.5);

        assertNull(match);
    }

    private static Reservation reservation(
            String id,
            String slotId,
            double energy,
            String status) {
        return new Reservation(
                id,
                "prosumer-nic",
                "Prosumer",
                "station-1",
                "Station",
                "Address",
                slotId,
                "Available",
                "2026-09-25T10:00:00Z",
                "2026-09-25T11:00:00Z",
                energy,
                status,
                1,
                false,
                null,
                "2026-09-24T10:00:00Z",
                "2026-09-24T10:00:00Z",
                "",
                "",
                "",
                Collections.emptyList());
    }
}
