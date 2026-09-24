package com.solargrid.exchange.features.users;

import com.solargrid.exchange.network.ApiClient;

public final class UserRepository {
    private final ApiClient apiClient;

    public UserRepository(ApiClient apiClient) {
        this.apiClient = apiClient;
    }

    public ApiClient getApiClient() {
        return apiClient;
    }
}
