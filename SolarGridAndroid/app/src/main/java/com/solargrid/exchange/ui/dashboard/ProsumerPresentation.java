package com.solargrid.exchange.ui.dashboard;

import com.solargrid.exchange.data.model.ProsumerDashboard;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.network.ApiError;

public final class ProsumerPresentation {
    private ProsumerPresentation() { }

    public static boolean isDashboardEmpty(ProsumerDashboard dashboard) {
        return dashboard.getCurrentReservations().isEmpty()
                && dashboard.getPendingReservations().isEmpty()
                && dashboard.getRecentHistory().isEmpty();
    }

    public static boolean canOfferQr(Reservation reservation) {
        return reservation != null
                && reservation.getAllowedActions() != null
                && reservation.getAllowedActions().canGetQr();
    }

    public static boolean isRetryable(ApiError error) {
        return error != null && (error.getKind() == ApiError.Kind.NETWORK
                || error.getKind() == ApiError.Kind.SERVER
                || error.getKind() == ApiError.Kind.UNKNOWN);
    }
}
