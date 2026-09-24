package com.solargrid.exchange.data.model;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

public final class SessionUserTest {
    @Test
    public void exactBackendRolesDriveNativeRouting() {
        SessionUser prosumer = new SessionUser(
                "token", "200012345678", "Prosumer", "", "Prosumer", "Active");
        SessionUser operator = new SessionUser(
                "token", "199912345678", "Operator", "", "GridOperator", "Active");
        SessionUser backoffice = new SessionUser(
                "token", "198812345678", "Backoffice", "", "Backoffice", "Active");

        assertTrue(prosumer.isProsumer());
        assertFalse(prosumer.isStaff());
        assertTrue(operator.isStaff());
        assertTrue(backoffice.isStaff());
    }
}
