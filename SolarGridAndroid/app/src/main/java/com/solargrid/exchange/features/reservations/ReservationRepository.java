package com.solargrid.exchange.features.reservations;

import com.solargrid.exchange.data.model.PagedReservations;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.data.model.ReservationAllowedActions;
import com.solargrid.exchange.data.model.ReservationStatusHistory;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiClient;
import com.solargrid.exchange.network.ApiError;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.UnsupportedEncodingException;
import java.net.URLEncoder;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Map;

public final class ReservationRepository {
    private final ApiClient apiClient;

    public ReservationRepository(ApiClient apiClient) {
        this.apiClient = apiClient;
    }

    public void getReservations(
            String view,
            String search,
            int page,
            ApiCallback<PagedReservations> callback) {
        StringBuilder path = new StringBuilder("reservations?view=")
                .append(encode(view))
                .append("&page=")
                .append(page)
                .append("&pageSize=50");
        if (search != null && !search.trim().isEmpty()) {
            path.append("&search=").append(encode(search.trim()));
        }
        apiClient.get(path.toString(), map(callback, value -> {
            List<Reservation> items = parseReservations(value.optJSONArray("items"));
            return new PagedReservations(
                    items,
                    value.optLong("totalCount", items.size()),
                    value.optInt("page", page),
                    value.optInt("totalPages", items.isEmpty() ? 0 : 1));
        }));
    }

    public void getReservation(String reservationId, ApiCallback<Reservation> callback) {
        apiClient.get("reservations/" + encode(reservationId), map(callback, this::parseReservation));
    }

    public void createOwnReservation(
            String slotId,
            double requestedEnergyKwh,
            String idempotencyKey,
            ApiCallback<Reservation> callback) {
        JSONObject body = new JSONObject();
        try {
            body.put("slotId", slotId);
            body.put("requestedEnergyKwh", requestedEnergyKwh);
        } catch (JSONException exception) {
            callback.onError(requestEncodingError());
            return;
        }
        apiClient.post(
                "reservations",
                body,
                idempotencyHeader(idempotencyKey),
                map(callback, this::parseReservation));
    }

    public void updateReservation(
            String reservationId,
            String slotId,
            double requestedEnergyKwh,
            long expectedVersion,
            String idempotencyKey,
            ApiCallback<Reservation> callback) {
        JSONObject body = new JSONObject();
        try {
            body.put("slotId", slotId);
            body.put("requestedEnergyKwh", requestedEnergyKwh);
            body.put("expectedVersion", expectedVersion);
        } catch (JSONException exception) {
            callback.onError(requestEncodingError());
            return;
        }
        apiClient.put(
                "reservations/" + encode(reservationId),
                body,
                idempotencyHeader(idempotencyKey),
                map(callback, this::parseReservation));
    }

    public void cancelReservation(
            String reservationId,
            long expectedVersion,
            String reason,
            String idempotencyKey,
            ApiCallback<Reservation> callback) {
        JSONObject body = new JSONObject();
        try {
            body.put("expectedVersion", expectedVersion);
            body.put("reason", reason == null || reason.trim().isEmpty()
                    ? JSONObject.NULL
                    : reason.trim());
        } catch (JSONException exception) {
            callback.onError(requestEncodingError());
            return;
        }
        apiClient.post(
                "reservations/" + encode(reservationId) + "/cancel",
                body,
                idempotencyHeader(idempotencyKey),
                map(callback, this::parseReservation));
    }

    private Reservation parseReservation(JSONObject value) {
        JSONObject actions = value.optJSONObject("allowedActions");
        ReservationAllowedActions allowedActions = new ReservationAllowedActions(
                actions != null && actions.optBoolean("canUpdate", false),
                optionalString(actions, "updateUnavailableReason"),
                actions != null && actions.optBoolean("canCancel", false),
                optionalString(actions, "cancelUnavailableReason"),
                actions != null && actions.optBoolean("canApprove", false),
                optionalString(actions, "approveUnavailableReason"),
                actions != null && actions.optBoolean("canReject", false),
                optionalString(actions, "rejectUnavailableReason"),
                actions != null && actions.optBoolean("canGetQr", false),
                optionalString(actions, "getQrUnavailableReason"),
                actions != null && actions.optBoolean("canVerifyQr", false),
                optionalString(actions, "verifyQrUnavailableReason"),
                actions != null && actions.optBoolean("canComplete", false),
                optionalString(actions, "completeUnavailableReason"));

        List<ReservationStatusHistory> history = new ArrayList<>();
        JSONArray historyValues = value.optJSONArray("statusHistory");
        if (historyValues != null) {
            for (int index = 0; index < historyValues.length(); index++) {
                JSONObject item = historyValues.optJSONObject(index);
                if (item != null) {
                    history.add(new ReservationStatusHistory(
                            optionalString(item, "fromStatus"),
                            item.optString("toStatus", ""),
                            item.optString("changedAtUtc", ""),
                            item.optString("actorNic", ""),
                            item.optString("actorRole", ""),
                            item.optLong("version", 0),
                            optionalString(item, "reason")));
                }
            }
        }

        return new Reservation(
                value.optString("id", ""),
                value.optString("prosumerNic", ""),
                optionalString(value, "prosumerFullName"),
                value.optString("stationId", ""),
                optionalString(value, "stationName"),
                optionalString(value, "stationAddress"),
                value.optString("slotId", ""),
                optionalString(value, "slotAvailabilityStatus"),
                value.optString("scheduledStartTimeUtc", ""),
                value.optString("scheduledEndTimeUtc", ""),
                value.optDouble("requestedEnergyKwh", 0),
                value.optString("status", ""),
                value.optLong("version", 0),
                value.optBoolean("qrEligible", false),
                allowedActions,
                value.optString("createdAtUtc", ""),
                value.optString("updatedAtUtc", ""),
                optionalString(value, "cancelledAtUtc"),
                optionalString(value, "cancellationReason"),
                optionalString(value, "rejectionReason"),
                history);
    }

    private List<Reservation> parseReservations(JSONArray values) {
        List<Reservation> reservations = new ArrayList<>();
        if (values == null) {
            return reservations;
        }
        for (int index = 0; index < values.length(); index++) {
            JSONObject item = values.optJSONObject(index);
            if (item != null) {
                reservations.add(parseReservation(item));
            }
        }
        return reservations;
    }

    private static Map<String, String> idempotencyHeader(String key) {
        return Collections.singletonMap("Idempotency-Key", key);
    }

    private static String optionalString(JSONObject value, String name) {
        return value == null || value.isNull(name) ? "" : value.optString(name, "");
    }

    private static ApiError requestEncodingError() {
        return new ApiError(
                ApiError.Kind.VALIDATION,
                0,
                "The reservation request could not be prepared.");
    }

    private static String encode(String value) {
        try {
            return URLEncoder.encode(value, "UTF-8");
        } catch (UnsupportedEncodingException impossible) {
            throw new IllegalStateException("UTF-8 is not supported on this device.", impossible);
        }
    }

    private static <T> ApiCallback<JSONObject> map(
            ApiCallback<T> callback,
            JsonMapper<T> mapper) {
        return new ApiCallback<>() {
            @Override
            public void onSuccess(JSONObject value) {
                callback.onSuccess(mapper.map(value));
            }

            @Override
            public void onError(ApiError error) {
                callback.onError(error);
            }
        };
    }

    private interface JsonMapper<T> {
        T map(JSONObject value);
    }
}
