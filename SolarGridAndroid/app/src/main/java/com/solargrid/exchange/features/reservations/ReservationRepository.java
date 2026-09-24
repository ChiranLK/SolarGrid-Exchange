package com.solargrid.exchange.features.reservations;

import com.solargrid.exchange.network.ApiClient;

public final class ReservationRepository {
    private final ApiClient apiClient;

    public ReservationRepository(ApiClient apiClient) {
        this.apiClient = apiClient;
    }

    public ApiClient getApiClient() {
        return apiClient;
    }
}
