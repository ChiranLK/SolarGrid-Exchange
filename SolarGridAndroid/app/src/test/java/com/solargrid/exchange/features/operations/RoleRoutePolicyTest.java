package com.solargrid.exchange.features.operations;

import static org.junit.Assert.assertEquals;

import com.solargrid.exchange.data.model.SessionUser;

import org.junit.Test;

public final class RoleRoutePolicyTest {
    @Test
    public void resolveRoutesOnlyGridOperatorToOperatorHome() {
        SessionUser prosumer = user("Prosumer");
        SessionUser operator = user("GridOperator");
        SessionUser backoffice = user("Backoffice");

        assertEquals(RoleRoutePolicy.HomeMode.PROSUMER, RoleRoutePolicy.resolve(prosumer));
        assertEquals(RoleRoutePolicy.HomeMode.GRID_OPERATOR, RoleRoutePolicy.resolve(operator));
        assertEquals(RoleRoutePolicy.HomeMode.PROFILE, RoleRoutePolicy.resolve(backoffice));
    }

    private static SessionUser user(String role) {
        return new SessionUser(
                "test-token", "000000000000", "Test User", "", role, "Active");
    }
}
