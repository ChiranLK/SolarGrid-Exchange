package com.solargrid.exchange.network;

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

    public ApiError(Kind kind, int statusCode, String message) {
        this.kind = kind;
        this.statusCode = statusCode;
        this.message = message;
    }

    public Kind getKind() { return kind; }
    public int getStatusCode() { return statusCode; }
    public String getMessage() { return message; }

    public boolean isAuthenticationExpired() {
        return kind == Kind.UNAUTHORIZED;
    }
}
