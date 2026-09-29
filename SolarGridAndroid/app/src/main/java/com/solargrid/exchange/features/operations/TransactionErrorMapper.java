package com.solargrid.exchange.features.operations;

import com.solargrid.exchange.network.ApiError;

import java.util.Locale;

public final class TransactionErrorMapper {
    public enum Category {
        INVALID,
        EXPIRED,
        ALREADY_VERIFIED,
        ALREADY_COMPLETED,
        WRONG_STATION,
        INELIGIBLE,
        UNAUTHORIZED,
        OFFLINE,
        SERVER,
        UNKNOWN
    }

    public static final class Presentation {
        private final Category category;
        private final String message;
        private final boolean retryable;

        Presentation(Category category, String message, boolean retryable) {
            this.category = category;
            this.message = message;
            this.retryable = retryable;
        }

        public Category getCategory() { return category; }
        public String getMessage() { return message; }
        public boolean isRetryable() { return retryable; }
    }

    private TransactionErrorMapper() { }

    public static Presentation map(ApiError error) {
        String detail = error == null || error.getMessage() == null
                ? ""
                : error.getMessage().trim();
        String lower = detail.toLowerCase(Locale.US);
        if (error == null) {
            return new Presentation(Category.UNKNOWN, "The transaction could not be completed.", true);
        }
        if (error.isAuthenticationExpired()) {
            return new Presentation(
                    Category.UNAUTHORIZED, "Your session has expired. Sign in again.", false);
        }
        if (error.getKind() == ApiError.Kind.NETWORK) {
            return new Presentation(
                    Category.OFFLINE,
                    "The server could not be reached. Check the connection and retry; no success was recorded locally.",
                    true);
        }
        if (error.getKind() == ApiError.Kind.SERVER) {
            return new Presentation(
                    Category.SERVER,
                    "The server could not complete the transaction. Retry without assuming success.",
                    true);
        }
        if (lower.contains("different operator station") || lower.contains("assigned station")) {
            return new Presentation(
                    Category.WRONG_STATION,
                    "This QR belongs to a different operator station.",
                    false);
        }
        if (lower.contains("already been completed") || lower.contains("already completed")) {
            return new Presentation(
                    Category.ALREADY_COMPLETED,
                    "This transfer has already been completed. It cannot be completed again.",
                    false);
        }
        if (lower.contains("already been verified") || lower.contains("already verified")) {
            return new Presentation(
                    Category.ALREADY_VERIFIED,
                    "This QR has already been scanned and cannot be verified again.",
                    false);
        }
        if (lower.contains("expired")) {
            return new Presentation(
                    Category.EXPIRED,
                    "This QR or verification receipt has expired. Ask the Prosumer to refresh it.",
                    false);
        }
        if (lower.contains("eligible") || lower.contains("approved") || lower.contains("active")) {
            return new Presentation(
                    Category.INELIGIBLE,
                    "The reservation is no longer eligible for this transfer.",
                    false);
        }
        if (error.getKind() == ApiError.Kind.NOT_FOUND
                || error.getKind() == ApiError.Kind.VALIDATION) {
            return new Presentation(
                    Category.INVALID,
                    "The scanned QR is invalid or no longer usable.",
                    false);
        }
        return new Presentation(
                Category.UNKNOWN,
                detail.isEmpty() ? "The transaction could not be completed." : detail,
                false);
    }

    public static ApiError toDisplayError(ApiError source) {
        Presentation presentation = map(source);
        return new ApiError(
                source == null ? ApiError.Kind.UNKNOWN : source.getKind(),
                source == null ? 0 : source.getStatusCode(),
                presentation.getMessage());
    }
}
