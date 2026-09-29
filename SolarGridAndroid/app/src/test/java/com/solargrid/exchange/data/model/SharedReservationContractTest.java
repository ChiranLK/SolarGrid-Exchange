package com.solargrid.exchange.data.model;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

public final class SharedReservationContractTest {
    @Test
    public void acceptsOnlyCanonicalApiReservationStatuses() {
        assertTrue(SharedReservationContract.isKnownStatus("Pending"));
        assertTrue(SharedReservationContract.isKnownStatus("Approved"));
        assertTrue(SharedReservationContract.isKnownStatus("Rejected"));
        assertTrue(SharedReservationContract.isKnownStatus("Cancelled"));
        assertTrue(SharedReservationContract.isKnownStatus("Completed"));
        assertFalse(SharedReservationContract.isKnownStatus("Expired"));
        assertFalse(SharedReservationContract.isKnownStatus("approved"));
        assertFalse(SharedReservationContract.isKnownStatus(""));
    }
}
