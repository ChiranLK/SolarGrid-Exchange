package com.solargrid.exchange.ui.dashboard;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import com.solargrid.exchange.data.model.DashboardReservation;
import com.solargrid.exchange.data.model.DashboardStatusSummary;
import com.solargrid.exchange.data.model.ProsumerDashboard;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.data.model.ReservationAllowedActions;
import com.solargrid.exchange.network.ApiError;

import org.junit.Test;

import java.util.Collections;

public final class ProsumerPresentationTest {
    @Test
    public void dashboardEmptyStateDependsOnlyOnReturnedServerLists() {
        DashboardStatusSummary summary = new DashboardStatusSummary(0, 0, 0, 0, 0, 0, 0, 0, 0);
        ProsumerDashboard empty = new ProsumerDashboard(
                "2026-09-27T10:00:00Z", summary,
                Collections.emptyList(), Collections.emptyList(), Collections.emptyList());
        DashboardReservation reservation = new DashboardReservation(
                "id", "RES-ID", "station", "Station", "Address",
                "2026-09-28T10:00:00Z", "2026-09-28T11:00:00Z", 2, "Pending");
        ProsumerDashboard pending = new ProsumerDashboard(
                "2026-09-27T10:00:00Z", summary,
                Collections.emptyList(), Collections.singletonList(reservation), Collections.emptyList());

        assertTrue(ProsumerPresentation.isDashboardEmpty(empty));
        assertFalse(ProsumerPresentation.isDashboardEmpty(pending));
    }

    @Test
    public void qrActionUsesTheApiAllowedActionInsteadOfLocalTimingRules() {
        assertTrue(ProsumerPresentation.canOfferQr(reservation(true)));
        assertFalse(ProsumerPresentation.canOfferQr(reservation(false)));
    }

    @Test
    public void retryStateDistinguishesConnectivityFromAuthoritativeRejection() {
        assertTrue(ProsumerPresentation.isRetryable(
                new ApiError(ApiError.Kind.NETWORK, 0, "offline")));
        assertTrue(ProsumerPresentation.isRetryable(
                new ApiError(ApiError.Kind.UNKNOWN, 0, "malformed")));
        assertFalse(ProsumerPresentation.isRetryable(
                new ApiError(ApiError.Kind.CONFLICT, 409, "ineligible")));
    }

    private static Reservation reservation(boolean canGetQr) {
        ReservationAllowedActions actions = new ReservationAllowedActions(
                false, "", false, "", false, "", false, "",
                canGetQr, canGetQr ? "" : "Not eligible", false, "", false, "");
        return new Reservation(
                "reservation", "owner", "Prosumer", "station", "Station", "Address",
                "slot", "Held", "2026-09-27T10:00:00Z", "2026-09-27T11:00:00Z",
                3, "Approved", 1, canGetQr, actions,
                "2026-09-26T10:00:00Z", "2026-09-26T10:00:00Z", "", "", "",
                Collections.emptyList());
    }
}
