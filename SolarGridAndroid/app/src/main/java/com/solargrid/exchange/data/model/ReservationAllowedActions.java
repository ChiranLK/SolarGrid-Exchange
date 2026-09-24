package com.solargrid.exchange.data.model;

public final class ReservationAllowedActions {
    private final boolean canUpdate;
    private final String updateUnavailableReason;
    private final boolean canCancel;
    private final String cancelUnavailableReason;
    private final boolean canApprove;
    private final String approveUnavailableReason;
    private final boolean canReject;
    private final String rejectUnavailableReason;
    private final boolean canGetQr;
    private final String getQrUnavailableReason;
    private final boolean canVerifyQr;
    private final String verifyQrUnavailableReason;
    private final boolean canComplete;
    private final String completeUnavailableReason;

    public ReservationAllowedActions(
            boolean canUpdate,
            String updateUnavailableReason,
            boolean canCancel,
            String cancelUnavailableReason,
            boolean canApprove,
            String approveUnavailableReason,
            boolean canReject,
            String rejectUnavailableReason,
            boolean canGetQr,
            String getQrUnavailableReason,
            boolean canVerifyQr,
            String verifyQrUnavailableReason,
            boolean canComplete,
            String completeUnavailableReason) {
        this.canUpdate = canUpdate;
        this.updateUnavailableReason = updateUnavailableReason;
        this.canCancel = canCancel;
        this.cancelUnavailableReason = cancelUnavailableReason;
        this.canApprove = canApprove;
        this.approveUnavailableReason = approveUnavailableReason;
        this.canReject = canReject;
        this.rejectUnavailableReason = rejectUnavailableReason;
        this.canGetQr = canGetQr;
        this.getQrUnavailableReason = getQrUnavailableReason;
        this.canVerifyQr = canVerifyQr;
        this.verifyQrUnavailableReason = verifyQrUnavailableReason;
        this.canComplete = canComplete;
        this.completeUnavailableReason = completeUnavailableReason;
    }

    public boolean canUpdate() { return canUpdate; }
    public String getUpdateUnavailableReason() { return updateUnavailableReason; }
    public boolean canCancel() { return canCancel; }
    public String getCancelUnavailableReason() { return cancelUnavailableReason; }
    public boolean canApprove() { return canApprove; }
    public String getApproveUnavailableReason() { return approveUnavailableReason; }
    public boolean canReject() { return canReject; }
    public String getRejectUnavailableReason() { return rejectUnavailableReason; }
    public boolean canGetQr() { return canGetQr; }
    public String getGetQrUnavailableReason() { return getQrUnavailableReason; }
    public boolean canVerifyQr() { return canVerifyQr; }
    public String getVerifyQrUnavailableReason() { return verifyQrUnavailableReason; }
    public boolean canComplete() { return canComplete; }
    public String getCompleteUnavailableReason() { return completeUnavailableReason; }
}
