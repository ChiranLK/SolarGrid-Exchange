package com.solargrid.exchange.data.model;

import java.util.Arrays;
import java.util.Collections;
import java.util.HashSet;
import java.util.Set;

public final class SharedReservationContract {
    public static final String PENDING = "Pending";
    public static final String APPROVED = "Approved";
    public static final String REJECTED = "Rejected";
    public static final String CANCELLED = "Cancelled";
    public static final String COMPLETED = "Completed";

    private static final Set<String> STATUSES = Collections.unmodifiableSet(new HashSet<>(
            Arrays.asList(PENDING, APPROVED, REJECTED, CANCELLED, COMPLETED)));

    private SharedReservationContract() {
    }

    public static boolean isKnownStatus(String value) {
        return STATUSES.contains(value);
    }
}
