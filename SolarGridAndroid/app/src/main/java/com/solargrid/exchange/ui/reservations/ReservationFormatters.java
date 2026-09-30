package com.solargrid.exchange.ui.reservations;

import java.text.DateFormat;
import java.text.ParseException;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.TimeZone;

public final class ReservationFormatters {
    private ReservationFormatters() { }

    public static String localDateTime(String utcValue) {
        if (utcValue == null || utcValue.trim().isEmpty()) {
            return "Not supplied";
        }
        Date value = parseUtc(utcValue);
        if (value == null) {
            return utcValue;
        }
        return DateFormat.getDateTimeInstance(
                DateFormat.MEDIUM,
                DateFormat.SHORT,
                Locale.getDefault()).format(value);
    }

    static String localDate(String utcValue) {
        Date value = parseUtc(utcValue);
        return value == null
                ? utcValue
                : DateFormat.getDateInstance(DateFormat.FULL, Locale.getDefault()).format(value);
    }

    static String localTimeRange(String startUtc, String endUtc) {
        Date start = parseUtc(startUtc);
        Date end = parseUtc(endUtc);
        if (start == null || end == null) {
            return localDateTime(startUtc) + " - " + localDateTime(endUtc);
        }
        DateFormat formatter = DateFormat.getTimeInstance(DateFormat.SHORT, Locale.getDefault());
        return formatter.format(start) + " - " + formatter.format(end);
    }

    private static Date parseUtc(String utcValue) {
        if (utcValue == null || utcValue.trim().isEmpty()) {
            return null;
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
                    return value;
                }
            } catch (ParseException ignored) {
                // Try the next ISO-8601 shape before preserving the server value.
            }
        }
        return null;
    }

    /** Display only: "Thu, 1 Oct" in the device locale and time zone. Server values are untouched. */
    public static String displayShortDate(String utcValue) {
        Date value = parseUtc(utcValue);
        return value == null
                ? (utcValue == null ? "" : utcValue)
                : new SimpleDateFormat("EEE, d MMM", Locale.getDefault()).format(value);
    }

    /** Display only: short local start and end times, for example "9:00 AM – 10:00 AM". */
    public static String displayTimeRange(String startUtc, String endUtc) {
        Date start = parseUtc(startUtc);
        Date end = parseUtc(endUtc);
        if (start == null || end == null) {
            return localDateTime(startUtc) + " – " + localDateTime(endUtc);
        }
        DateFormat formatter = DateFormat.getTimeInstance(DateFormat.SHORT, Locale.getDefault());
        return formatter.format(start) + " – " + formatter.format(end);
    }

    /** Display only: "Thu, 1 Oct · 9:00 AM – 10:00 AM". */
    public static String displaySlot(String startUtc, String endUtc) {
        if (parseUtc(startUtc) == null) {
            return displayTimeRange(startUtc, endUtc);
        }
        return displayShortDate(startUtc) + " · " + displayTimeRange(startUtc, endUtc);
    }

    /**
     * Display only: the same short form the API uses for references (RES- plus the last eight
     * characters, upper case). Requests always keep using the full reservation ID.
     */
    public static String displayReference(String reservationId) {
        if (reservationId == null || reservationId.trim().isEmpty()) {
            return "";
        }
        String id = reservationId.trim();
        String tail = id.length() > 8 ? id.substring(id.length() - 8) : id;
        return "RES-" + tail.toUpperCase(Locale.ROOT);
    }

    public static String energy(double value) {
        return String.format(Locale.getDefault(), "%.2f kWh", value);
    }

    public static String station(String name, String id) {
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
