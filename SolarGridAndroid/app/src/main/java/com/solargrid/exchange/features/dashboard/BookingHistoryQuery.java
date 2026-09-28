package com.solargrid.exchange.features.dashboard;

import java.io.UnsupportedEncodingException;
import java.net.URLEncoder;

public final class BookingHistoryQuery {
    public static final int PAGE_SIZE = 20;

    private final String search;
    private final String status;
    private final String stationId;
    private final String fromUtc;
    private final String toUtc;
    private final int page;

    public BookingHistoryQuery(
            String search,
            String status,
            String stationId,
            String fromUtc,
            String toUtc,
            int page) {
        this.search = trim(search);
        this.status = trim(status);
        this.stationId = trim(stationId);
        this.fromUtc = trim(fromUtc);
        this.toUtc = trim(toUtc);
        this.page = page;
    }

    public String validationMessage() {
        if (page < 1) {
            return "Page must be at least 1.";
        }
        if (search.length() > 100) {
            return "Search text cannot exceed 100 characters.";
        }
        if (!fromUtc.isEmpty() && !toUtc.isEmpty() && fromUtc.compareTo(toUtc) > 0) {
            return "The from date must not be after the to date.";
        }
        return "";
    }

    public String toRelativePath() {
        String validation = validationMessage();
        if (!validation.isEmpty()) {
            throw new IllegalArgumentException(validation);
        }
        StringBuilder path = new StringBuilder("dashboard/history?page=")
                .append(page)
                .append("&pageSize=")
                .append(PAGE_SIZE);
        append(path, "search", search);
        append(path, "status", status);
        append(path, "stationId", stationId);
        append(path, "fromUtc", fromUtc);
        append(path, "toUtc", toUtc);
        return path.toString();
    }

    public int getPage() { return page; }

    private static void append(StringBuilder path, String name, String value) {
        if (!value.isEmpty()) {
            path.append('&').append(name).append('=').append(encode(value));
        }
    }

    private static String trim(String value) {
        return value == null ? "" : value.trim();
    }

    private static String encode(String value) {
        try {
            return URLEncoder.encode(value, "UTF-8");
        } catch (UnsupportedEncodingException impossible) {
            throw new IllegalStateException("UTF-8 is unavailable.", impossible);
        }
    }
}
