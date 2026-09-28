package com.solargrid.exchange.features.operations;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

public final class OperatorFlowPolicyTest {
    @Test
    public void duplicateScansAreIgnoredDuringOrAfterVerification() {
        assertTrue(OperatorFlowPolicy.canAcceptScan(false, false));
        assertFalse(OperatorFlowPolicy.canAcceptScan(true, false));
        assertFalse(OperatorFlowPolicy.canAcceptScan(false, true));
    }

    @Test
    public void completionRequiresConfirmationAndPreventsRapidDoubleTap() {
        assertFalse(OperatorFlowPolicy.canSubmitCompletion(false, true, false, false));
        assertFalse(OperatorFlowPolicy.canSubmitCompletion(true, false, false, false));
        assertTrue(OperatorFlowPolicy.canSubmitCompletion(true, true, false, false));
        assertFalse(OperatorFlowPolicy.canSubmitCompletion(true, true, true, false));
        assertFalse(OperatorFlowPolicy.canSubmitCompletion(true, true, false, true));
    }
}
