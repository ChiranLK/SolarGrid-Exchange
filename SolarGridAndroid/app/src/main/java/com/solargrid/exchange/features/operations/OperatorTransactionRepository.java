package com.solargrid.exchange.features.operations;

import com.solargrid.exchange.data.model.CompletedTransaction;
import com.solargrid.exchange.data.model.VerifiedTransaction;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiClient;
import com.solargrid.exchange.network.ApiError;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.UnsupportedEncodingException;
import java.net.URLEncoder;

public final class OperatorTransactionRepository {
    private final ApiClient apiClient;

    public OperatorTransactionRepository(ApiClient apiClient) {
        this.apiClient = apiClient;
    }

    public void verify(String scannedToken, ApiCallback<VerifiedTransaction> callback) {
        JSONObject body = new JSONObject();
        try {
            body.put("token", scannedToken);
        } catch (JSONException exception) {
            callback.onError(malformed("The scanned QR could not be prepared for verification."));
            return;
        }
        apiClient.post("transactions/verify", body, new ApiCallback<>() {
            @Override
            public void onSuccess(JSONObject value) {
                try {
                    callback.onSuccess(new VerifiedTransaction(
                            value.getString("verificationId"),
                            value.getString("reservationId"),
                            value.getString("reservationReference"),
                            value.getString("prosumerReference"),
                            value.getLong("reservationVersion"),
                            value.getString("stationId"),
                            optionalString(value, "stationName"),
                            value.getString("scheduledStartTimeUtc"),
                            value.getString("scheduledEndTimeUtc"),
                            value.getDouble("requestedEnergyKwh"),
                            value.getString("status"),
                            value.getString("verifiedAtUtc"),
                            value.getString("expiresAtUtc")));
                } catch (JSONException exception) {
                    callback.onError(malformed("The verification response was incomplete."));
                }
            }

            @Override
            public void onError(ApiError error) {
                callback.onError(error);
            }
        });
    }

    public void complete(
            VerifiedTransaction verified,
            ApiCallback<CompletedTransaction> callback) {
        JSONObject body = new JSONObject();
        try {
            body.put("verificationId", verified.getVerificationId());
            body.put("expectedVersion", verified.getReservationVersion());
        } catch (JSONException exception) {
            callback.onError(malformed("The completion request could not be prepared."));
            return;
        }
        apiClient.post(
                "transactions/reservations/" + encode(verified.getReservationId()) + "/complete",
                body,
                new ApiCallback<>() {
                    @Override
                    public void onSuccess(JSONObject value) {
                        try {
                            callback.onSuccess(new CompletedTransaction(
                                    value.getString("reservationId"),
                                    value.getString("reservationReference"),
                                    value.getString("stationId"),
                                    value.getString("status"),
                                    value.getLong("version"),
                                    value.getString("completedAtUtc")));
                        } catch (JSONException exception) {
                            callback.onError(malformed("The completion response was incomplete."));
                        }
                    }

                    @Override
                    public void onError(ApiError error) {
                        callback.onError(error);
                    }
                });
    }

    private static String optionalString(JSONObject value, String name) {
        return value.isNull(name) ? "" : value.optString(name, "");
    }

    private static String encode(String value) {
        try {
            return URLEncoder.encode(value, "UTF-8");
        } catch (UnsupportedEncodingException impossible) {
            throw new IllegalStateException("UTF-8 is unavailable.", impossible);
        }
    }

    private static ApiError malformed(String message) {
        return new ApiError(ApiError.Kind.UNKNOWN, 0, message);
    }
}
