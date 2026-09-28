package com.solargrid.exchange.features.transactions;

import static org.junit.Assert.assertEquals;

import org.junit.Test;

import java.text.SimpleDateFormat;
import java.util.Locale;
import java.util.TimeZone;

public final class QrExpiryTest {
    @Test
    public void calculatesCountdownForServerFractionalUtcTimestamp() throws Exception {
        SimpleDateFormat parser = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.US);
        parser.setTimeZone(TimeZone.getTimeZone("UTC"));
        long now = parser.parse("2026-09-27T10:00:00.000Z").getTime();

        assertEquals(65, QrExpiry.remainingSeconds("2026-09-27T10:01:05.0000000Z", now));
    }

    @Test
    public void expiredOrMalformedValuesNeverRemainUsable() {
        assertEquals(0, QrExpiry.remainingSeconds("2026-09-27T09:59:59Z", Long.MAX_VALUE));
        assertEquals(0, QrExpiry.remainingSeconds("not-a-date", 0));
    }
}
