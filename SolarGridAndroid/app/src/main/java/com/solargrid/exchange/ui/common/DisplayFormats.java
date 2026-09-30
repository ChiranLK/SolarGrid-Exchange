package com.solargrid.exchange.ui.common;

import android.content.Context;
import android.graphics.Typeface;
import android.text.SpannableString;
import android.text.SpannableStringBuilder;
import android.text.Spanned;
import android.text.style.RelativeSizeSpan;
import android.text.style.StyleSpan;

import androidx.annotation.Nullable;

import com.solargrid.exchange.R;

import java.text.ParseException;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.TimeZone;

/**
 * Display-only formatting used by the redesign: 24-hour local times, the device time-zone label,
 * energy figures with a small unit, masked NICs and avatar initials. Server values and request
 * payloads are never changed by these helpers.
 */
public final class DisplayFormats {
    private DisplayFormats() { }

    @Nullable
    static Date parseUtc(@Nullable String value) {
        if (value == null || value.trim().isEmpty()) {
            return null;
        }
        String normalized = value.trim();
        int decimal = normalized.indexOf('.');
        int zone = normalized.indexOf('Z', Math.max(decimal, 0));
        if (decimal >= 0 && zone > decimal + 4) {
            normalized = normalized.substring(0, decimal + 4) + normalized.substring(zone);
        }
        String[] patterns = {"yyyy-MM-dd'T'HH:mm:ss.SSSX", "yyyy-MM-dd'T'HH:mm:ssX"};
        for (String pattern : patterns) {
            try {
                SimpleDateFormat parser = new SimpleDateFormat(pattern, Locale.US);
                parser.setLenient(false);
                parser.setTimeZone(TimeZone.getTimeZone("UTC"));
                Date parsed = parser.parse(normalized);
                if (parsed != null) {
                    return parsed;
                }
            } catch (ParseException ignored) {
                // Try the next ISO-8601 shape.
            }
        }
        return null;
    }

    private static String format(Date date, String pattern) {
        return new SimpleDateFormat(pattern, Locale.getDefault()).format(date);
    }

    /** "SL time" / "Sri Lanka time" in Asia/Colombo, otherwise the device zone's short name. */
    public static String zoneLabel(Context context, boolean longForm) {
        TimeZone zone = TimeZone.getDefault();
        if ("Asia/Colombo".equals(zone.getID())) {
            return context.getString(longForm ? R.string.sg_zone_sl_long : R.string.sg_zone_sl_short);
        }
        return zone.getDisplayName(zone.inDaylightTime(new Date()), TimeZone.SHORT, Locale.getDefault());
    }

    /** "09:00–10:00". Falls back to the raw values if the server timestamps are unreadable. */
    public static String timeRange(String startUtc, String endUtc) {
        Date start = parseUtc(startUtc);
        Date end = parseUtc(endUtc);
        if (start == null || end == null) {
            return (startUtc == null ? "" : startUtc) + " – " + (endUtc == null ? "" : endUtc);
        }
        return format(start, "HH:mm") + "–" + format(end, "HH:mm");
    }

    /** Row form: "1 Oct · 09:00–10:00 · SL time". */
    public static String compactSlot(Context context, String startUtc, String endUtc) {
        Date start = parseUtc(startUtc);
        if (start == null) {
            return timeRange(startUtc, endUtc);
        }
        return format(start, "d MMM") + " · " + timeRange(startUtc, endUtc) + " · " + zoneLabel(context, false);
    }

    /** Slot card secondary line: "Thu, 1 Oct · Sri Lanka time". */
    public static String slotDay(Context context, String startUtc) {
        Date start = parseUtc(startUtc);
        return start == null ? "" : format(start, "EEE, d MMM") + " · " + zoneLabel(context, true);
    }

    /** Spinner label: "Thu, 1 Oct · 09:00–10:00". */
    public static String slotLabel(String startUtc, String endUtc) {
        Date start = parseUtc(startUtc);
        return start == null
                ? timeRange(startUtc, endUtc)
                : format(start, "EEE, d MMM") + " · " + timeRange(startUtc, endUtc);
    }

    /** Detail block: bold "Thu, 1 Oct 2026" then "09:00–10:00 · Sri Lanka time". */
    public static CharSequence scheduleBlock(Context context, String startUtc, String endUtc) {
        Date start = parseUtc(startUtc);
        if (start == null) {
            return timeRange(startUtc, endUtc);
        }
        String day = format(start, "EEE, d MMM yyyy");
        SpannableStringBuilder text = new SpannableStringBuilder(day);
        text.setSpan(new StyleSpan(Typeface.BOLD), 0, day.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        String second = "\n" + timeRange(startUtc, endUtc) + " · " + zoneLabel(context, true);
        int from = text.length();
        text.append(second);
        text.setSpan(new RelativeSizeSpan(0.9f), from, text.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        return text;
    }

    /** Timestamp row: "30 Sep 2026 · 00:10". */
    public static String dateTime(@Nullable String utc) {
        Date value = parseUtc(utc);
        return value == null ? (utc == null ? "" : utc) : format(value, "d MMM yyyy · HH:mm");
    }

    /** Big number with a small unit, for example "12.50" + " kWh". */
    public static CharSequence energy(double kwh) {
        String number = String.format(Locale.getDefault(), "%.2f", kwh);
        SpannableString text = new SpannableString(number + " kWh");
        text.setSpan(new RelativeSizeSpan(0.45f), number.length(), text.length(),
                Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        return text;
    }

    /** Display-only NIC mask: "2000••••••97". The canonical NIC is never modified. */
    public static String maskNic(@Nullable String nic) {
        if (nic == null) {
            return "";
        }
        String value = nic.trim();
        if (value.length() <= 6) {
            return value;
        }
        StringBuilder masked = new StringBuilder(value.substring(0, 4));
        for (int index = 4; index < value.length() - 2; index++) {
            masked.append('•');
        }
        return masked.append(value.substring(value.length() - 2)).toString();
    }

    public static String initials(@Nullable String fullName) {
        if (fullName == null) {
            return "";
        }
        StringBuilder initials = new StringBuilder();
        for (String part : fullName.trim().split("\\s+")) {
            if (!part.isEmpty() && initials.length() < 2) {
                initials.appendCodePoint(Character.toUpperCase(part.codePointAt(0)));
            }
        }
        return initials.toString();
    }

    public static String firstName(@Nullable String fullName) {
        if (fullName == null || fullName.trim().isEmpty()) {
            return "";
        }
        return fullName.trim().split("\\s+")[0];
    }

    public static String roleLabel(Context context, @Nullable String role) {
        return "GridOperator".equals(role) ? context.getString(R.string.sg_role_grid_operator) : role;
    }
}
