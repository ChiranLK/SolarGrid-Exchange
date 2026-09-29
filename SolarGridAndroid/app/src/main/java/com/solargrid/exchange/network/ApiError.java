package com.solargrid.exchange.network;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;

public final class ApiError {
    public enum Kind {
        VALIDATION,
        UNAUTHORIZED,
        FORBIDDEN,
        NOT_FOUND,
        CONFLICT,
        SERVER,
        NETWORK,
        UNKNOWN
    }

    private final Kind kind;
    private final int statusCode;
    private final String message;
    private final Map<String, String> fieldErrors;

    public ApiError(Kind kind, int statusCode, String message) {
        this(kind, statusCode, message, Collections.emptyMap());
    }

    /**
     * @param fieldErrors first validation message per request field, as returned in an ASP.NET
     *                    validation problem. Keys are normalised to lower case without a "$." prefix.
     */
    public ApiError(Kind kind, int statusCode, String message, Map<String, String> fieldErrors) {
        this.kind = kind;
        this.statusCode = statusCode;
        this.message = message;
        Map<String, String> normalised = new LinkedHashMap<>();
        for (Map.Entry<String, String> entry : fieldErrors.entrySet()) {
            normalised.put(normaliseField(entry.getKey()), entry.getValue());
        }
        this.fieldErrors = Collections.unmodifiableMap(normalised);
    }

    public Kind getKind() { return kind; }
    public int getStatusCode() { return statusCode; }
    public String getMessage() { return message; }
    public Map<String, String> getFieldErrors() { return fieldErrors; }

    /** Validation message for one request field (case-insensitive), or null. */
    public String getFieldError(String field) {
        return fieldErrors.get(normaliseField(field));
    }

    public boolean isAuthenticationExpired() {
        return kind == Kind.UNAUTHORIZED;
    }

    static String normaliseField(String key) {
        String trimmed = key == null ? "" : key.trim();
        if (trimmed.startsWith("$.")) {
            trimmed = trimmed.substring(2);
        }
        return trimmed.toLowerCase(Locale.ROOT);
    }
}
