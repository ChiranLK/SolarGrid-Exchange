package com.solargrid.exchange.features.auth;

import androidx.annotation.Nullable;

import java.util.regex.Pattern;

/**
 * Field limits copied from the API's RegisterRequestDto / UpdateProfileDto annotations so the
 * user gets immediate feedback. The API re-validates every request and remains authoritative.
 */
public final class AccountFieldRules {
    public static final int MAX_NIC = 12;
    public static final int MAX_FULL_NAME = 100;
    public static final int MAX_EMAIL = 100;
    public static final int MAX_PHONE = 20;
    public static final int MAX_ADDRESS = 200;
    public static final int MIN_PASSWORD = 8;
    public static final int MAX_PASSWORD = 100;

    private static final Pattern NIC = Pattern.compile("^([0-9]{9}[VvXx]|[0-9]{12})$");
    private static final Pattern EMAIL = Pattern.compile("^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$");

    private AccountFieldRules() {
    }

    public static String trim(@Nullable String value) {
        return value == null ? "" : value.trim();
    }

    @Nullable
    public static FormError nic(String trimmed) {
        if (trimmed.isEmpty()) return FormError.of(FormError.Issue.REQUIRED);
        if (trimmed.length() > MAX_NIC || !NIC.matcher(trimmed).matches()) {
            return FormError.of(FormError.Issue.INVALID_NIC);
        }
        return null;
    }

    @Nullable
    public static FormError required(String trimmed, int maxLength) {
        if (trimmed.isEmpty()) return FormError.of(FormError.Issue.REQUIRED);
        if (trimmed.length() > maxLength) return FormError.tooLong(maxLength);
        return null;
    }

    @Nullable
    public static FormError optional(String trimmed, int maxLength) {
        return trimmed.length() > maxLength ? FormError.tooLong(maxLength) : null;
    }

    @Nullable
    public static FormError email(String trimmed) {
        if (trimmed.isEmpty()) return FormError.of(FormError.Issue.REQUIRED);
        if (trimmed.length() > MAX_EMAIL || !EMAIL.matcher(trimmed).matches()) {
            return FormError.of(FormError.Issue.INVALID_EMAIL);
        }
        return null;
    }

    @Nullable
    public static FormError password(String password) {
        if (password.isEmpty()) return FormError.of(FormError.Issue.REQUIRED);
        if (password.length() < MIN_PASSWORD) return FormError.of(FormError.Issue.PASSWORD_TOO_SHORT);
        if (password.length() > MAX_PASSWORD) return FormError.tooLong(MAX_PASSWORD);
        return null;
    }
}
