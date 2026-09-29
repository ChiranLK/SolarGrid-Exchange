package com.solargrid.exchange.data.model;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public final class ProsumerDashboard {
    private final String serverNowUtc;
    private final DashboardStatusSummary statusSummary;
    private final List<DashboardReservation> currentReservations;
    private final List<DashboardReservation> pendingReservations;
    private final List<DashboardReservation> recentHistory;

    public ProsumerDashboard(
            String serverNowUtc,
            DashboardStatusSummary statusSummary,
            List<DashboardReservation> currentReservations,
            List<DashboardReservation> pendingReservations,
            List<DashboardReservation> recentHistory) {
        this.serverNowUtc = serverNowUtc;
        this.statusSummary = statusSummary;
        this.currentReservations = immutable(currentReservations);
        this.pendingReservations = immutable(pendingReservations);
        this.recentHistory = immutable(recentHistory);
    }

    private static List<DashboardReservation> immutable(List<DashboardReservation> values) {
        return Collections.unmodifiableList(new ArrayList<>(values));
    }

    public String getServerNowUtc() { return serverNowUtc; }
    public DashboardStatusSummary getStatusSummary() { return statusSummary; }
    public List<DashboardReservation> getCurrentReservations() { return currentReservations; }
    public List<DashboardReservation> getPendingReservations() { return pendingReservations; }
    public List<DashboardReservation> getRecentHistory() { return recentHistory; }
}
