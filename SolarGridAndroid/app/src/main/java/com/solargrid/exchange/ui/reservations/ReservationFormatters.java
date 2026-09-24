package com.solargrid.exchange.ui.reservations;

import java.text.DateFormat;
import java.text.ParseException;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.TimeZone;

final class ReservationFormatters {
    private ReservationFormatters() { }

    static String localDateTime(String utcValue) {
        if (utcValue == null || utcValue.trim().isEmpty()) {
            return "Not supplied";
        }
        String normalized = normalizeFraction(utcValue.trim());
        String[] patterns = {
                "yyyy-MM-dd'T'HH:mm:ss.SSSX",
                "yyyy-MM-dd'T'HH:mm:ssX"
        };
        for (String pattern : patterns) {
            try {
                SimpleDateFormat parser = new SimpleDateFormat(pattern, Locale.US);
                parser.setLenient(false);
                parser.setTimeZone(TimeZone.getTimeZone("UTC"));
                Date value = parser.parse(normalized);
                if (value != null) {
                    return DateFormat.getDateTimeInstance(
                            DateFormat.MEDIUM,
                            DateFormat.SHORT,
                            Locale.getDefault()).format(value);
                }
            } catch (ParseException ignored) {
                // Try the next ISO-8601 shape before preserving the server value.
            }
        }
        return utcValue;
    }

    static String energy(double value) {
        return String.format(Locale.getDefault(), "%.2f kWh", value);
    }

    static String station(String name, String id) {
        return name == null || name.trim().isEmpty() ? id : name;
    }

    private static String normalizeFraction(String value) {
        int decimal = value.indexOf('.');
        int zone = value.indexOf('Z', decimal);
        if (zone < 0) {
            int plus = value.indexOf('+', decimal);
            int minus = value.indexOf('-', decimal);
            zone = plus >= 0 ? plus : minus;
        }
        if (decimal >= 0 && zone > decimal + 4) {
            return value.substring(0, decimal + 4) + value.substring(zone);
        }
        return value;
    }
}
