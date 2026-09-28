package com.solargrid.exchange.features.auth;

import com.solargrid.exchange.network.ApiError;

import java.util.Locale;

/**
 * Recognises the API's account-status refusals. The API reports them as 403 with a fixed
 * message (AuthService login, [RequireActiveAccount] and ProsumerService); these constants must
 * stay in step with those messages. No account rule is decided here.
 */
public final class AccountStatusPolicy {
    public enum AccountState { PENDING_ACTIVATION, DEACTIVATED, NOT_ACTIVE, NONE }

    static final String PENDING_MESSAGE = "This account is awaiting activation.";
    static final String DEACTIVATED_MESSAGE = "This account is deactivated. Please contact Backoffice.";
    static final String NOT_ACTIVE_MESSAGE = "This account is not active. Please contact Backoffice.";

    private AccountStatusPolicy() {
    }

    public static AccountState classify(ApiError error) {
        if (error == null || error.getKind() != ApiError.Kind.FORBIDDEN || error.getMessage() == null) {
            return AccountState.NONE;
        }
        String message = error.getMessage().trim().toLowerCase(Locale.ROOT);
        if (message.equals(PENDING_MESSAGE.toLowerCase(Locale.ROOT))) {
            return AccountState.PENDING_ACTIVATION;
        }
        if (message.equals(DEACTIVATED_MESSAGE.toLowerCase(Locale.ROOT))) {
            return AccountState.DEACTIVATED;
        }
        if (message.equals(NOT_ACTIVE_MESSAGE.toLowerCase(Locale.ROOT))) {
            return AccountState.NOT_ACTIVE;
        }
        return AccountState.NONE;
    }
}
