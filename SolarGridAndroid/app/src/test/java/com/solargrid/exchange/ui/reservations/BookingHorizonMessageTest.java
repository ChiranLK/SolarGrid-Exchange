package com.solargrid.exchange.ui.reservations;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.util.Locale;

public final class BookingHorizonMessageTest {
    private static boolean horizon(String apiMessage) {
        return BookingReviewViewModel.isBookingHorizonMessage(apiMessage.toLowerCase(Locale.US));
    }

    @Test
    public void recognisesTheApiSevenDayConflict() {
        assertTrue(horizon("Reservations cannot be scheduled more than 7 days ahead."));
    }

    @Test
    public void doesNotTreatOtherConflictsAsTheHorizon() {
        assertFalse(horizon("Reservations require at least 12 hours' notice to update."));
        assertFalse(horizon("The prosumer already has a duplicate or overlapping active reservation."));
        assertFalse(horizon("The slot does not have enough available capacity."));
    }
}
