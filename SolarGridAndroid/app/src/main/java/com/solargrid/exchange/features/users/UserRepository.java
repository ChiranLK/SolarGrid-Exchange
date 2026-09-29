package com.solargrid.exchange.features.users;

import com.solargrid.exchange.data.model.ProsumerProfile;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiClient;
import com.solargrid.exchange.network.ApiError;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.Collections;

/** Member 1 Prosumer self-service calls: /api/prosumers/me and its deactivation request. */
public final class UserRepository implements ProfileController.Gateway {
    static final String PROFILE_PATH = "prosumers/me";
    static final String DEACTIVATION_REQUEST_PATH = "prosumers/me/deactivation-request";

    private final ApiClient apiClient;

    public UserRepository(ApiClient apiClient) {
        this.apiClient = apiClient;
    }

    public ApiClient getApiClient() {
        return apiClient;
    }

    @Override
    public void getProfile(ApiCallback<ProsumerProfile> callback) {
        apiClient.get(PROFILE_PATH, profileCallback(callback));
    }

    @Override
    public void updateProfile(ProfileForm form, ApiCallback<ProsumerProfile> callback) {
        JSONObject body;
        try {
            body = form.toRequestJson();
        } catch (JSONException exception) {
            callback.onError(new ApiError(ApiError.Kind.VALIDATION, 0, "Profile details are invalid."));
            return;
        }
        apiClient.put(PROFILE_PATH, body, Collections.emptyMap(), profileCallback(callback));
    }

    @Override
    public void requestDeactivation(ApiCallback<ProsumerProfile> callback) {
        apiClient.post(DEACTIVATION_REQUEST_PATH, new JSONObject(), profileCallback(callback));
    }

    /** Parses ProsumerProfileResponseDto, treating an incomplete body as an unreadable response. */
    static ApiCallback<JSONObject> profileCallback(ApiCallback<ProsumerProfile> callback) {
        return new ApiCallback<JSONObject>() {
            @Override
            public void onSuccess(JSONObject value) {
                ProsumerProfile profile = ProsumerProfile.fromJson(value);
                if (profile == null) {
                    callback.onError(new ApiError(
                            ApiError.Kind.UNKNOWN, 0, "The API returned an incomplete profile."));
                } else {
                    callback.onSuccess(profile);
                }
            }

            @Override
            public void onError(ApiError error) {
                callback.onError(error);
            }
        };
    }
}
