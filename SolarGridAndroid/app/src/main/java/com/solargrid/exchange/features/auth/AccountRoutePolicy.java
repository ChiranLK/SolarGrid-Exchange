package com.solargrid.exchange.features.auth;

import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.network.ApiError;

/**
 * Member 1 entry routing, applied before MainActivity. Backoffice administration is a web
 * responsibility, so a Backoffice session never enters the mobile app. Inside MainActivity the
 * existing RoleRoutePolicy still chooses the Prosumer or Grid Operator home.
 */
public final class AccountRoutePolicy {
    public enum Destination {
        MAIN_APP,
        BACKOFFICE_WEB_ONLY,
        PENDING_ACTIVATION,
        SIGN_IN,
        SIGN_IN_WITH_ACCOUNT_NOTICE,
        SHOW_ERROR
    }

    private AccountRoutePolicy() {
    }

    /** After a successful login or a validated stored session. */
    public static Destination afterAuthentication(SessionUser session) {
        if (session.isBackoffice()) {
            return Destination.BACKOFFICE_WEB_ONLY;
        }
        if (session.isProsumer() || session.isGridOperator()) {
            return Destination.MAIN_APP;
        }
        return Destination.SIGN_IN;
    }

    /** After POST /api/auth/login fails. Deactivated accounts see the API message on the login screen. */
    public static Destination afterLoginFailure(ApiError error) {
        return AccountStatusPolicy.classify(error) == AccountStatusPolicy.AccountState.PENDING_ACTIVATION
                ? Destination.PENDING_ACTIVATION
                : Destination.SHOW_ERROR;
    }

    /** After a protected call for an existing session fails (start-up /auth/me, profile, etc.). */
    public static Destination afterSessionFailure(ApiError error) {
        if (error.isAuthenticationExpired()) {
            return Destination.SIGN_IN;
        }
        switch (AccountStatusPolicy.classify(error)) {
            case PENDING_ACTIVATION:
                return Destination.PENDING_ACTIVATION;
            case DEACTIVATED:
            case NOT_ACTIVE:
                return Destination.SIGN_IN_WITH_ACCOUNT_NOTICE;
            default:
                return Destination.SHOW_ERROR;
        }
    }
}
