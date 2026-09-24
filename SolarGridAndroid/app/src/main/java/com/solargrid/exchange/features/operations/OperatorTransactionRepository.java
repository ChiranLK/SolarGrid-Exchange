package com.solargrid.exchange.features.operations;

import com.solargrid.exchange.network.ApiClient;

public final class OperatorTransactionRepository {
    private final ApiClient apiClient;

    public OperatorTransactionRepository(ApiClient apiClient) {
        this.apiClient = apiClient;
    }

    public ApiClient getApiClient() {
        return apiClient;
    }
}
