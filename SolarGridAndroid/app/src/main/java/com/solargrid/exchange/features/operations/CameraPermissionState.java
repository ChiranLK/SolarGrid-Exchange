package com.solargrid.exchange.features.operations;

public enum CameraPermissionState {
    GRANTED,
    EXPLANATION,
    DENIED_CAN_ASK,
    PERMANENTLY_DENIED;

    public static CameraPermissionState resolve(
            boolean granted,
            boolean requestedBefore,
            boolean shouldShowRationale) {
        if (granted) {
            return GRANTED;
        }
        if (!requestedBefore) {
            return EXPLANATION;
        }
        return shouldShowRationale ? DENIED_CAN_ASK : PERMANENTLY_DENIED;
    }
}
