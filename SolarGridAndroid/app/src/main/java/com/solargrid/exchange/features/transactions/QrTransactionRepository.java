package com.solargrid.exchange.features.transactions;

import com.solargrid.exchange.data.model.QrTransactionToken;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiClient;
import com.solargrid.exchange.network.ApiError;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.UnsupportedEncodingException;
import java.net.URLEncoder;

public final class QrTransactionRepository {
    private final ApiClient apiClient;

    public QrTransactionRepository(ApiClient apiClient) {
        this.apiClient = apiClient;
    }

    public void issue(String reservationId, ApiCallback<QrTransactionToken> callback) {
        apiClient.post(
                "transactions/reservations/" + encode(reservationId) + "/qr",
                new JSONObject(),
                new ApiCallback<>() {
                    @Override
                    public void onSuccess(JSONObject value) {
                        try {
                            QrTransactionToken token = new QrTransactionToken(
                                    value.getString("reservationId"),
                                    value.getLong("reservationVersion"),
                                    value.getString("qrToken"),
                                    value.getString("issuedAtUtc"),
                                    value.getString("expiresAtUtc"));
                            if (!QrTokenPolicy.isSafeOpaqueToken(token.getQrToken())) {
                                callback.onError(new ApiError(
                                        ApiError.Kind.UNKNOWN,
                                        0,
                                        "The API returned an unsafe QR payload."));
                                return;
                            }
                            callback.onSuccess(token);
                        } catch (JSONException exception) {
                            callback.onError(new ApiError(
                                    ApiError.Kind.UNKNOWN,
                                    0,
                                    "The QR response was incomplete."));
                        }
                    }

                    @Override
                    public void onError(ApiError error) {
                        callback.onError(error);
                    }
                });
    }

    private static String encode(String value) {
        try {
            return URLEncoder.encode(value, "UTF-8");
        } catch (UnsupportedEncodingException impossible) {
            throw new IllegalStateException("UTF-8 is unavailable.", impossible);
        }
    }
}
