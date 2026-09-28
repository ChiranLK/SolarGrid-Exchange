package com.solargrid.exchange.features.operations;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import com.solargrid.exchange.network.ApiError;

import org.junit.Test;

public final class TransactionErrorMapperTest {
    @Test
    public void mapsInvalidExpiredCompletedAndWrongStationResponses() {
        assertEquals(
                TransactionErrorMapper.Category.INVALID,
                map(ApiError.Kind.NOT_FOUND, 404, "invalid").getCategory());
        assertEquals(
                TransactionErrorMapper.Category.EXPIRED,
                map(ApiError.Kind.CONFLICT, 409, "The QR token has expired.").getCategory());
        assertEquals(
                TransactionErrorMapper.Category.ALREADY_COMPLETED,
                map(ApiError.Kind.CONFLICT, 409, "The transfer has already been completed.")
                        .getCategory());
        assertEquals(
                TransactionErrorMapper.Category.WRONG_STATION,
                map(ApiError.Kind.FORBIDDEN, 403,
                        "The QR transaction belongs to a different operator station.")
                        .getCategory());
    }

    @Test
    public void onlyUncertainNetworkAndServerFailuresOfferRetry() {
        assertTrue(map(ApiError.Kind.NETWORK, 0, "offline").isRetryable());
        assertTrue(map(ApiError.Kind.SERVER, 500, "server").isRetryable());
        assertFalse(map(ApiError.Kind.CONFLICT, 409, "already completed").isRetryable());
        assertFalse(map(ApiError.Kind.FORBIDDEN, 403, "different operator station").isRetryable());
    }

    @Test
    public void completionConflictNeverBecomesRetryableLocalSuccess() {
        TransactionErrorMapper.Presentation conflict = map(
                ApiError.Kind.CONFLICT,
                409,
                "The reservation changed before completion.");

        assertEquals(TransactionErrorMapper.Category.UNKNOWN, conflict.getCategory());
        assertFalse(conflict.isRetryable());
        assertEquals("The reservation changed before completion.", conflict.getMessage());
    }

    private static TransactionErrorMapper.Presentation map(
            ApiError.Kind kind,
            int status,
            String message) {
        return TransactionErrorMapper.map(new ApiError(kind, status, message));
    }
}
