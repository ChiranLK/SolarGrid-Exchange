package com.solargrid.exchange.features.dashboard;

import com.solargrid.exchange.data.model.DashboardReservation;
import com.solargrid.exchange.data.model.DashboardStatusSummary;
import com.solargrid.exchange.data.model.PagedBookingHistory;
import com.solargrid.exchange.data.model.ProsumerDashboard;
import com.solargrid.exchange.data.model.SharedReservationContract;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiClient;
import com.solargrid.exchange.network.ApiError;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

public final class DashboardRepository {
    private final ApiClient apiClient;

    public DashboardRepository(ApiClient apiClient) {
        this.apiClient = apiClient;
    }

    public void getProsumerDashboard(ApiCallback<ProsumerDashboard> callback) {
        apiClient.get("dashboard?recentLimit=5", new ApiCallback<>() {
            @Override
            public void onSuccess(JSONObject value) {
                try {
                    if (!"Prosumer".equals(value.getString("role"))) {
                        callback.onError(malformed("This dashboard is not available for your account type."));
                        return;
                    }
                    JSONObject summary = value.getJSONObject("statusSummary");
                    callback.onSuccess(new ProsumerDashboard(
                            value.getString("serverNowUtc"),
                            new DashboardStatusSummary(
                                    summary.getLong("pendingTotal"),
                                    summary.getLong("approvedTotal"),
                                    summary.getLong("rejectedTotal"),
                                    summary.getLong("cancelledTotal"),
                                    summary.getLong("completedTotal"),
                                    summary.getLong("currentCount"),
                                    summary.getLong("pendingCount"),
                                    summary.getLong("approvedFutureCount"),
                                    summary.getLong("historyCount")),
                            parseReservations(value.getJSONArray("currentReservations")),
                            parseReservations(value.getJSONArray("pendingReservations")),
                            parseReservations(value.getJSONArray("recentHistory"))));
                } catch (JSONException exception) {
                    callback.onError(malformed("The dashboard response was incomplete."));
                }
            }

            @Override
            public void onError(ApiError error) {
                callback.onError(error);
            }
        });
    }

    public void getHistory(
            BookingHistoryQuery query,
            ApiCallback<PagedBookingHistory> callback) {
        String validation = query.validationMessage();
        if (!validation.isEmpty()) {
            callback.onError(new ApiError(ApiError.Kind.VALIDATION, 0, validation));
            return;
        }
        apiClient.get(query.toRelativePath(), new ApiCallback<>() {
            @Override
            public void onSuccess(JSONObject value) {
                try {
                    callback.onSuccess(new PagedBookingHistory(
                            parseReservations(value.getJSONArray("items")),
                            value.getLong("totalCount"),
                            value.getInt("page"),
                            value.getInt("pageSize"),
                            value.getInt("totalPages")));
                } catch (JSONException exception) {
                    callback.onError(malformed("The booking history response was incomplete."));
                }
            }

            @Override
            public void onError(ApiError error) {
                callback.onError(error);
            }
        });
    }

    private static List<DashboardReservation> parseReservations(JSONArray values)
            throws JSONException {
        List<DashboardReservation> reservations = new ArrayList<>();
        for (int index = 0; index < values.length(); index++) {
            JSONObject item = values.getJSONObject(index);
            String status = item.getString("status");
            if (!SharedReservationContract.isKnownStatus(status)) {
                throw new JSONException("An unknown reservation status was received.");
            }
            reservations.add(new DashboardReservation(
                    item.getString("reservationId"),
                    item.getString("reference"),
                    item.getString("stationId"),
                    optionalString(item, "stationName"),
                    optionalString(item, "stationAddress"),
                    item.getString("scheduledStartTimeUtc"),
                    item.getString("scheduledEndTimeUtc"),
                    item.getDouble("requestedEnergyKwh"),
                    status));
        }
        return reservations;
    }

    private static String optionalString(JSONObject value, String name) {
        return value.isNull(name) ? "" : value.optString(name, "");
    }

    private static ApiError malformed(String message) {
        return new ApiError(ApiError.Kind.UNKNOWN, 0, message);
    }
}
