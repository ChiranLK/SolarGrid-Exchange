package com.solargrid.exchange.data.model;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.util.ArrayList;
import java.util.List;

public final class ReservationModelsTest {
    @Test
    public void allowedActionsPreserveServerDecisionFlags() {
        ReservationAllowedActions actions = new ReservationAllowedActions(
                true, "", true, "", false, "Approval is staff-only.",
                false, "Rejection is staff-only.", true, "", false, "", false, "");

        assertTrue(actions.canUpdate());
        assertTrue(actions.canCancel());
        assertTrue(actions.canGetQr());
        assertFalse(actions.canApprove());
        assertEquals("Approval is staff-only.", actions.getApproveUnavailableReason());
    }

    @Test
    public void pagedReservationsDefensivelyCopyItems() {
        List<Reservation> source = new ArrayList<>();
        PagedReservations page = new PagedReservations(source, 0, 1, 0);

        source.add(null);

        assertTrue(page.getItems().isEmpty());
        assertThrows(UnsupportedOperationException.class, () -> page.getItems().add(null));
    }
}
