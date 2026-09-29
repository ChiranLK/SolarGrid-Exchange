package com.solargrid.exchange.features.transactions;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

public final class QrTokenPolicyTest {
    @Test
    public void acceptsOnlyOpaqueUrlSafeServerTokens() {
        assertTrue(QrTokenPolicy.isSafeOpaqueToken(
                "YRl2s7h58bj6_K7hGZ0nXaN9W8y5hYp1MgMkxqU-CV4"));
        assertFalse(QrTokenPolicy.isSafeOpaqueToken("short"));
        assertFalse(QrTokenPolicy.isSafeOpaqueToken(
                "eyJhbGciOiJIUzI1NiJ9.payload.signature"));
        assertFalse(QrTokenPolicy.isSafeOpaqueToken(
                "https://solargrid.example/qr?nic=200012345678"));
        assertFalse(QrTokenPolicy.isSafeOpaqueToken(
                "prosumer@example.com________________"));
    }
}
