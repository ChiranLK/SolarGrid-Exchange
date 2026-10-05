package com.solargrid.exchange.features.auth;

import static org.junit.Assert.assertEquals;

import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.features.auth.AccountRoutePolicy.Destination;
import com.solargrid.exchange.network.ApiError;

import org.junit.Test;

public final class AccountRoutePolicyTest {
    private static SessionUser user(String role) {
        return new SessionUser("test-token", "200000000001", "Test User", "", role, "Active");
    }

    private static ApiError forbidden(String message) {
        return new ApiError(ApiError.Kind.FORBIDDEN, 403, message);
    }

    @Test
    public void prosumerAndGridOperatorEnterTheAppButBackofficeIsWebOnly() {
        assertEquals(Destination.MAIN_APP, AccountRoutePolicy.afterAuthentication(user("Prosumer")));
        assertEquals(Destination.MAIN_APP, AccountRoutePolicy.afterAuthentication(user("GridOperator")));
        assertEquals(Destination.BACKOFFICE_WEB_ONLY, AccountRoutePolicy.afterAuthentication(user("Backoffice")));
        assertEquals(Destination.SIGN_IN, AccountRoutePolicy.afterAuthentication(user("Unknown")));
    }

    @Test
    public void pendingLoginGoesToThePendingScreen() {
        assertEquals(Destination.PENDING_ACTIVATION,
                AccountRoutePolicy.afterLoginFailure(forbidden(AccountStatusPolicy.PENDING_MESSAGE)));
    }

    @Test
    public void otherLoginFailuresStayOnTheLoginScreenWithTheApiMessage() {
        assertEquals(Destination.SHOW_ERROR,
                AccountRoutePolicy.afterLoginFailure(forbidden(AccountStatusPolicy.DEACTIVATED_MESSAGE)));
        assertEquals(Destination.SHOW_ERROR, AccountRoutePolicy.afterLoginFailure(
                new ApiError(ApiError.Kind.UNAUTHORIZED, 401, "Invalid email or password.")));
        assertEquals(Destination.SHOW_ERROR, AccountRoutePolicy.afterLoginFailure(
                new ApiError(ApiError.Kind.NETWORK, 0, "Unable to reach SolarGrid.")));
        assertEquals(Destination.SHOW_ERROR, AccountRoutePolicy.afterLoginFailure(
                new ApiError(ApiError.Kind.SERVER, 500, "An unexpected error occurred.")));
        assertEquals(Destination.SHOW_ERROR, AccountRoutePolicy.afterLoginFailure(
                new ApiError(ApiError.Kind.UNKNOWN, 0, "The login response did not contain a supported SolarGrid identity.")));
    }

    @Test
    public void sessionFailuresRouteByAccountState() {
        assertEquals(Destination.SIGN_IN, AccountRoutePolicy.afterSessionFailure(
                new ApiError(ApiError.Kind.UNAUTHORIZED, 401, "Your session has expired.")));
        assertEquals(Destination.PENDING_ACTIVATION,
                AccountRoutePolicy.afterSessionFailure(forbidden(AccountStatusPolicy.PENDING_MESSAGE)));
        assertEquals(Destination.SIGN_IN_WITH_ACCOUNT_NOTICE,
                AccountRoutePolicy.afterSessionFailure(forbidden(AccountStatusPolicy.DEACTIVATED_MESSAGE)));
        assertEquals(Destination.SIGN_IN_WITH_ACCOUNT_NOTICE,
                AccountRoutePolicy.afterSessionFailure(forbidden(AccountStatusPolicy.NOT_ACTIVE_MESSAGE)));
    }

    @Test
    public void offlineServerAndUnrelatedForbiddenKeepTheSessionForRetry() {
        assertEquals(Destination.SHOW_ERROR, AccountRoutePolicy.afterSessionFailure(
                new ApiError(ApiError.Kind.NETWORK, 0, "offline")));
        assertEquals(Destination.SHOW_ERROR, AccountRoutePolicy.afterSessionFailure(
                new ApiError(ApiError.Kind.SERVER, 503, "down")));
        assertEquals(Destination.SHOW_ERROR, AccountRoutePolicy.afterSessionFailure(
                forbidden("Only Prosumer accounts can use this profile.")));
    }

    @Test
    public void statusMessagesAreMatchedExactlyIgnoringCaseAndSpacing() {
        assertEquals(AccountStatusPolicy.AccountState.PENDING_ACTIVATION,
                AccountStatusPolicy.classify(forbidden("  this account is AWAITING activation. ")));
        assertEquals(AccountStatusPolicy.AccountState.NONE,
                AccountStatusPolicy.classify(new ApiError(ApiError.Kind.CONFLICT, 409, AccountStatusPolicy.PENDING_MESSAGE)));
        assertEquals(AccountStatusPolicy.AccountState.NONE, AccountStatusPolicy.classify(null));
    }
}
