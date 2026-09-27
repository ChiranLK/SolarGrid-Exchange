package com.solargrid.exchange.features.transactions;

import java.text.ParseException;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.TimeZone;

public final class QrExpiry {
    private QrExpiry() { }

    public static long remainingSeconds(String expiresAtUtc, long nowMillis) {
        Date expiry = parseUtc(expiresAtUtc);
        if (expiry == null) {
            return 0;
        }
        return Math.max(0, (expiry.getTime() - nowMillis + 999) / 1000);
    }

    private static Date parseUtc(String value) {
        if (value == null || value.trim().isEmpty()) {
            return null;
        }
        String normalized = normalizeFraction(value.trim());
        String[] patterns = {"yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", "yyyy-MM-dd'T'HH:mm:ss'Z'"};
        for (String pattern : patterns) {
            SimpleDateFormat format = new SimpleDateFormat(pattern, Locale.US);
            format.setLenient(false);
            format.setTimeZone(TimeZone.getTimeZone("UTC"));
            try {
                return format.parse(normalized);
            } catch (ParseException ignored) {
                // Try the next supported ISO-8601 precision.
            }
        }
        return null;
    }

    private static String normalizeFraction(String value) {
        int decimal = value.indexOf('.');
        int zone = value.indexOf('Z', decimal);
        if (decimal >= 0 && zone > decimal + 4) {
            return value.substring(0, decimal + 4) + value.substring(zone);
        }
        return value;
    }
}
