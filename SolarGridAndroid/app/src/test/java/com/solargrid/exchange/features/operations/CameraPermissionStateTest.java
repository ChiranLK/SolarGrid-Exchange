package com.solargrid.exchange.features.operations;

import static org.junit.Assert.assertEquals;

import org.junit.Test;

public final class CameraPermissionStateTest {
    @Test
    public void mapsExplanationDenialPermanentDenialAndGrant() {
        assertEquals(
                CameraPermissionState.EXPLANATION,
                CameraPermissionState.resolve(false, false, false));
        assertEquals(
                CameraPermissionState.DENIED_CAN_ASK,
                CameraPermissionState.resolve(false, true, true));
        assertEquals(
                CameraPermissionState.PERMANENTLY_DENIED,
                CameraPermissionState.resolve(false, true, false));
        assertEquals(
                CameraPermissionState.GRANTED,
                CameraPermissionState.resolve(true, true, false));
    }
}
