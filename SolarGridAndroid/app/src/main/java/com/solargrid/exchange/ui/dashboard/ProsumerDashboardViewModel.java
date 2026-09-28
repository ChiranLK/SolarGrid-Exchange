package com.solargrid.exchange.ui.dashboard;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.ProsumerDashboard;
import com.solargrid.exchange.features.dashboard.DashboardRepository;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

public final class ProsumerDashboardViewModel extends AndroidViewModel {
    private final DashboardRepository repository;
    private final MutableLiveData<UiState<ProsumerDashboard>> state =
            new MutableLiveData<>(UiState.idle());
    private final MutableLiveData<ApiError> refreshError = new MutableLiveData<>();
    private int requestGeneration;

    public ProsumerDashboardViewModel(@NonNull Application application) {
        super(application);
        repository = ((SolarGridApplication) application)
                .getAppContainer()
                .getDashboardRepository();
    }

    public LiveData<UiState<ProsumerDashboard>> getState() { return state; }
    public LiveData<ApiError> getRefreshError() { return refreshError; }

    public void load() {
        UiState<ProsumerDashboard> current = state.getValue();
        if (current == null || current.getStatus() == UiState.Status.IDLE) {
            request(true);
        }
    }

    public void refresh() { request(true); }

    public void refreshSilently() { request(false); }

    private void request(boolean showLoading) {
        UiState<ProsumerDashboard> current = state.getValue();
        boolean hasVisibleData = current != null && current.getStatus() == UiState.Status.SUCCESS;
        if (showLoading || !hasVisibleData) {
            state.setValue(UiState.loading());
        }
        int generation = ++requestGeneration;
        repository.getProsumerDashboard(new ApiCallback<>() {
            @Override
            public void onSuccess(ProsumerDashboard value) {
                if (generation != requestGeneration) {
                    return;
                }
                refreshError.setValue(null);
                state.setValue(UiState.success(value));
            }

            @Override
            public void onError(ApiError error) {
                if (generation != requestGeneration) {
                    return;
                }
                if (!showLoading && hasVisibleData) {
                    refreshError.setValue(error);
                } else {
                    state.setValue(UiState.error(error));
                }
            }
        });
    }
}
