package com.solargrid.exchange.features.operations;

public final class OperatorFlowPolicy {
    private OperatorFlowPolicy() { }

    public static boolean canAcceptScan(boolean verificationInFlight, boolean hasVerifiedResult) {
        return !verificationInFlight && !hasVerifiedResult;
    }

    public static boolean canSubmitCompletion(
            boolean confirmed,
            boolean hasVerifiedResult,
            boolean completionInFlight,
            boolean alreadyCompleted) {
        return confirmed && hasVerifiedResult && !completionInFlight && !alreadyCompleted;
    }
}
