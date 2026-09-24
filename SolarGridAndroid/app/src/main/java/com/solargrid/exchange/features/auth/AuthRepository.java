package com.solargrid.exchange.features.auth;

import androidx.annotation.Nullable;

import com.solargrid.exchange.data.local.SessionStore;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiClient;
import com.solargrid.exchange.network.ApiError;

import org.json.JSONException;
import org.json.JSONObject;

public final class AuthRepository {
    private final ApiClient apiClient;
    private final SessionStore sessionStore;

    public AuthRepository(ApiClient apiClient, SessionStore sessionStore) {
        this.apiClient = apiClient;
        this.sessionStore = sessionStore;
    }

    public void login(String email, String password, ApiCallback<SessionUser> callback) {
        JSONObject body = new JSONObject();
        try {
            body.put("email", email.trim());
            body.put("password", password);
        } catch (JSONException exception) {
            callback.onError(new ApiError(ApiError.Kind.VALIDATION, 0, "Login details are invalid."));
            return;
        }

        apiClient.postAnonymous("auth/login", body, new ApiCallback<>() {
            @Override
            public void onSuccess(JSONObject value) {
                SessionUser session = parseLogin(value);
                if (session == null) {
                    callback.onError(new ApiError(
                            ApiError.Kind.UNKNOWN,
                            0,
                            "The login response did not contain a supported SolarGrid identity."));
                    return;
                }
                sessionStore.save(session);
                callback.onSuccess(session);
            }

            @Override
            public void onError(ApiError error) {
                callback.onError(error);
            }
        });
    }

    public void validateStoredSession(ApiCallback<SessionUser> callback) {
        SessionUser stored = sessionStore.read();
        if (stored == null) {
            callback.onError(new ApiError(
                    ApiError.Kind.UNAUTHORIZED,
                    401,
                    "Sign in to continue."));
            return;
        }

        apiClient.get("auth/me", new ApiCallback<>() {
            @Override
            public void onSuccess(JSONObject value) {
                String nic = value.optString("nic", "");
                String email = value.optString("email", stored.getEmail());
                String role = value.optString("role", "");
                if (!stored.getNic().equals(nic) || !isSupportedRole(role)) {
                    sessionStore.clear();
                    callback.onError(new ApiError(
                            ApiError.Kind.UNAUTHORIZED,
                            401,
                            "The stored session no longer matches the authenticated account."));
                    return;
                }
                SessionUser refreshed = stored.withIdentity(email, role);
                sessionStore.save(refreshed);
                callback.onSuccess(refreshed);
            }

            @Override
            public void onError(ApiError error) {
                callback.onError(error);
            }
        });
    }

    @Nullable
    public SessionUser getStoredSession() {
        return sessionStore.read();
    }

    public void logout() {
        sessionStore.clear();
    }

    @Nullable
    private static SessionUser parseLogin(JSONObject value) {
        String token = value.optString("token", "");
        String nic = value.optString("nic", "");
        String fullName = value.optString("fullName", "");
        String role = value.optString("role", "");
        String status = value.optString("status", "");
        if (token.isEmpty() || nic.isEmpty() || fullName.isEmpty() ||
                status.isEmpty() || !isSupportedRole(role)) {
            return null;
        }
        return new SessionUser(token, nic, fullName, "", role, status);
    }

    private static boolean isSupportedRole(String role) {
        return SessionUser.ROLE_PROSUMER.equals(role) ||
                SessionUser.ROLE_GRID_OPERATOR.equals(role) ||
                SessionUser.ROLE_BACKOFFICE.equals(role);
    }
}
