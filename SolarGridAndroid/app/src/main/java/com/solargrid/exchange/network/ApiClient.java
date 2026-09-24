package com.solargrid.exchange.network;

import android.os.Handler;
import android.os.Looper;

import androidx.annotation.Nullable;

import com.solargrid.exchange.BuildConfig;
import com.solargrid.exchange.data.local.SessionStore;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.Iterator;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class ApiClient {
    private static final int CONNECT_TIMEOUT_MS = 15_000;
    private static final int READ_TIMEOUT_MS = 20_000;

    private final SessionStore sessionStore;
    private final ExecutorService executor = Executors.newFixedThreadPool(3);
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final String baseUrl;

    public ApiClient(SessionStore sessionStore) {
        this.sessionStore = sessionStore;
        String configured = BuildConfig.API_BASE_URL.trim();
        baseUrl = configured.endsWith("/") ? configured : configured + "/";
    }

    public void get(String relativePath, ApiCallback<JSONObject> callback) {
        request("GET", relativePath, null, true, callback);
    }

    public void postAnonymous(String relativePath, JSONObject body, ApiCallback<JSONObject> callback) {
        request("POST", relativePath, body, false, callback);
    }

    public void post(String relativePath, JSONObject body, ApiCallback<JSONObject> callback) {
        request("POST", relativePath, body, true, callback);
    }

    private void request(
            String method,
            String relativePath,
            @Nullable JSONObject body,
            boolean authenticated,
            ApiCallback<JSONObject> callback) {
        executor.execute(() -> executeRequest(method, relativePath, body, authenticated, callback));
    }

    private void executeRequest(
            String method,
            String relativePath,
            @Nullable JSONObject body,
            boolean authenticated,
            ApiCallback<JSONObject> callback) {
        HttpURLConnection connection = null;
        try {
            String normalizedPath = relativePath.startsWith("/")
                    ? relativePath.substring(1)
                    : relativePath;
            connection = (HttpURLConnection) new URL(baseUrl + normalizedPath).openConnection();
            connection.setRequestMethod(method);
            connection.setConnectTimeout(CONNECT_TIMEOUT_MS);
            connection.setReadTimeout(READ_TIMEOUT_MS);
            connection.setRequestProperty("Accept", "application/json");

            if (authenticated) {
                String token = sessionStore.readToken();
                if (token == null || token.isEmpty()) {
                    deliverError(callback, new ApiError(
                            ApiError.Kind.UNAUTHORIZED,
                            401,
                            "Your session has expired. Please sign in again."));
                    return;
                }
                connection.setRequestProperty("Authorization", "Bearer " + token);
            }

            if (body != null) {
                byte[] payload = body.toString().getBytes(StandardCharsets.UTF_8);
                connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                connection.setFixedLengthStreamingMode(payload.length);
                try (OutputStream output = connection.getOutputStream()) {
                    output.write(payload);
                }
            }

            int statusCode = connection.getResponseCode();
            String responseBody = readResponse(
                    statusCode >= 200 && statusCode < 300
                            ? connection.getInputStream()
                            : connection.getErrorStream());

            if (statusCode >= 200 && statusCode < 300) {
                JSONObject payload = responseBody.isEmpty() ? new JSONObject() : new JSONObject(responseBody);
                deliverSuccess(callback, payload);
                return;
            }

            ApiError error = mapHttpError(statusCode, responseBody);
            if (error.isAuthenticationExpired()) {
                sessionStore.clear();
            }
            deliverError(callback, error);
        } catch (IOException exception) {
            deliverError(callback, new ApiError(
                    ApiError.Kind.NETWORK,
                    0,
                    "Unable to reach the SolarGrid API. Check the connection and API address."));
        } catch (JSONException exception) {
            deliverError(callback, new ApiError(
                    ApiError.Kind.UNKNOWN,
                    0,
                    "The API returned an unreadable response."));
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }

    private static ApiError mapHttpError(int statusCode, String responseBody) {
        String message = extractMessage(responseBody);
        ApiError.Kind kind;
        switch (statusCode) {
            case 400:
                kind = ApiError.Kind.VALIDATION;
                break;
            case 401:
                kind = ApiError.Kind.UNAUTHORIZED;
                break;
            case 403:
                kind = ApiError.Kind.FORBIDDEN;
                break;
            case 404:
                kind = ApiError.Kind.NOT_FOUND;
                break;
            case 409:
                kind = ApiError.Kind.CONFLICT;
                break;
            default:
                kind = statusCode >= 500 ? ApiError.Kind.SERVER : ApiError.Kind.UNKNOWN;
        }
        return new ApiError(kind, statusCode, message.isEmpty() ? defaultMessage(kind) : message);
    }

    private static String extractMessage(String responseBody) {
        if (responseBody == null || responseBody.trim().isEmpty()) {
            return "";
        }
        try {
            JSONObject problem = new JSONObject(responseBody);
            String direct = problem.optString("message", problem.optString("title", ""));
            if (!direct.isEmpty()) {
                return direct;
            }
            JSONObject errors = problem.optJSONObject("errors");
            if (errors != null) {
                Iterator<String> keys = errors.keys();
                if (keys.hasNext()) {
                    JSONArray messages = errors.optJSONArray(keys.next());
                    if (messages != null && messages.length() > 0) {
                        return messages.optString(0, "");
                    }
                }
            }
        } catch (JSONException ignored) {
            // The status-specific fallback remains safe and does not expose raw server output.
        }
        return "";
    }

    private static String defaultMessage(ApiError.Kind kind) {
        switch (kind) {
            case VALIDATION:
                return "Check the submitted information and try again.";
            case UNAUTHORIZED:
                return "Your session has expired. Please sign in again.";
            case FORBIDDEN:
                return "You do not have permission to perform this action.";
            case NOT_FOUND:
                return "The requested information could not be found.";
            case CONFLICT:
                return "The request conflicts with the current server state. Refresh and try again.";
            case SERVER:
                return "The SolarGrid API encountered an error. Please try again later.";
            default:
                return "The request could not be completed.";
        }
    }

    private static String readResponse(@Nullable InputStream input) throws IOException {
        if (input == null) {
            return "";
        }
        StringBuilder content = new StringBuilder();
        try (BufferedReader reader = new BufferedReader(
                new InputStreamReader(input, StandardCharsets.UTF_8))) {
            String line;
            while ((line = reader.readLine()) != null) {
                content.append(line);
            }
        }
        return content.toString();
    }

    private <T> void deliverSuccess(ApiCallback<T> callback, T value) {
        mainHandler.post(() -> callback.onSuccess(value));
    }

    private <T> void deliverError(ApiCallback<T> callback, ApiError error) {
        mainHandler.post(() -> callback.onError(error));
    }
}
