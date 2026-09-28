package com.solargrid.exchange.features.auth;

import androidx.annotation.Nullable;

import java.util.Objects;

/**
 * One field problem. Client-side checks carry an {@link Issue} that the UI turns into a string
 * resource; problems reported by the API carry the API's own message instead.
 */
public final class FormError {
    public enum Issue {
        REQUIRED,
        INVALID_NIC,
        INVALID_EMAIL,
        TOO_LONG,
        PASSWORD_TOO_SHORT,
        PASSWORDS_DO_NOT_MATCH,
        SERVER
    }

    private final Issue issue;
    private final int limit;
    @Nullable private final String serverMessage;

    private FormError(Issue issue, int limit, @Nullable String serverMessage) {
        this.issue = issue;
        this.limit = limit;
        this.serverMessage = serverMessage;
    }

    public static FormError of(Issue issue) {
        return new FormError(issue, 0, null);
    }

    public static FormError tooLong(int limit) {
        return new FormError(Issue.TOO_LONG, limit, null);
    }

    public static FormError fromServer(String message) {
        return new FormError(Issue.SERVER, 0, message);
    }

    public Issue getIssue() { return issue; }
    public int getLimit() { return limit; }
    @Nullable public String getServerMessage() { return serverMessage; }

    @Override
    public boolean equals(Object other) {
        if (this == other) return true;
        if (!(other instanceof FormError)) return false;
        FormError that = (FormError) other;
        return limit == that.limit && issue == that.issue && Objects.equals(serverMessage, that.serverMessage);
    }

    @Override
    public int hashCode() {
        return Objects.hash(issue, limit, serverMessage);
    }

    @Override
    public String toString() {
        return serverMessage != null ? issue + ":" + serverMessage : issue + (limit > 0 ? ":" + limit : "");
    }
}
