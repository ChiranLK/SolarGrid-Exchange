package com.solargrid.exchange.ui.reservations;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotEquals;

import org.junit.Test;

import java.util.Locale;
import java.util.TimeZone;

public final class ReservationFormattersTest {
    @Test
    public void formatsDotNetUtcTimestampWithSevenFractionDigits() {
        Locale originalLocale = Locale.getDefault();
        TimeZone originalZone = TimeZone.getDefault();
        try {
            Locale.setDefault(Locale.US);
            TimeZone.setDefault(TimeZone.getTimeZone("Asia/Colombo"));
            String utc = "2026-09-25T10:20:30.1234567Z";

            assertNotEquals(utc, ReservationFormatters.localDateTime(utc));
        } finally {
            Locale.setDefault(originalLocale);
            TimeZone.setDefault(originalZone);
        }
    }

    @Test
    public void preservesUnreadableServerTimestamp() {
        assertEquals("not-a-date", ReservationFormatters.localDateTime("not-a-date"));
    }
}
