package com.solargrid.exchange.data.model;

public final class DashboardStatusSummary {
    private final long pendingTotal;
    private final long approvedTotal;
    private final long rejectedTotal;
    private final long cancelledTotal;
    private final long completedTotal;
    private final long currentCount;
    private final long pendingCount;
    private final long approvedFutureCount;
    private final long historyCount;

    public DashboardStatusSummary(
            long pendingTotal,
            long approvedTotal,
            long rejectedTotal,
            long cancelledTotal,
            long completedTotal,
            long currentCount,
            long pendingCount,
            long approvedFutureCount,
            long historyCount) {
        this.pendingTotal = pendingTotal;
        this.approvedTotal = approvedTotal;
        this.rejectedTotal = rejectedTotal;
        this.cancelledTotal = cancelledTotal;
        this.completedTotal = completedTotal;
        this.currentCount = currentCount;
        this.pendingCount = pendingCount;
        this.approvedFutureCount = approvedFutureCount;
        this.historyCount = historyCount;
    }

    public long getPendingTotal() { return pendingTotal; }
    public long getApprovedTotal() { return approvedTotal; }
    public long getRejectedTotal() { return rejectedTotal; }
    public long getCancelledTotal() { return cancelledTotal; }
    public long getCompletedTotal() { return completedTotal; }
    public long getCurrentCount() { return currentCount; }
    public long getPendingCount() { return pendingCount; }
    public long getApprovedFutureCount() { return approvedFutureCount; }
    public long getHistoryCount() { return historyCount; }
}
