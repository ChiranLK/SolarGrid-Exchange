package com.solargrid.exchange.ui.reservations;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.PagedReservations;
import com.solargrid.exchange.features.reservations.ReservationRepository;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

public final class ReservationListViewModel extends AndroidViewModel {
    private final ReservationRepository repository;
    private final MutableLiveData<UiState<PagedReservations>> state =
            new MutableLiveData<>(UiState.idle());
    private final MutableLiveData<ApiError> refreshError = new MutableLiveData<>();
    private String selectedView;
    private int requestGeneration;
    private int firstVisiblePosition;
    private int firstVisibleTop;
    private boolean hasSavedScroll;

    public ReservationListViewModel(@NonNull Application application) {
        super(application);
        repository = ((SolarGridApplication) application)
                .getAppContainer()
                .getReservationRepository();
    }

    public LiveData<UiState<PagedReservations>> getState() { return state; }
    public LiveData<ApiError> getRefreshError() { return refreshError; }
    public String getSelectedView() { return selectedView; }
    public boolean hasSavedScroll() { return hasSavedScroll; }
    public int getFirstVisiblePosition() { return firstVisiblePosition; }
    public int getFirstVisibleTop() { return firstVisibleTop; }

    public void load(String view) {
        if (view.equals(selectedView) && state.getValue() != null
                && state.getValue().getStatus() != UiState.Status.IDLE) {
            return;
        }
        boolean changed = !view.equals(selectedView);
        selectedView = view;
        if (changed) {
            hasSavedScroll = false;
        }
        request(true);
    }

    public void refresh() {
        request(true);
    }

    public void refreshSilently() {
        request(false);
    }

    public void rememberScroll(int position, int top) {
        firstVisiblePosition = Math.max(0, position);
        firstVisibleTop = top;
        hasSavedScroll = true;
    }

    private void request(boolean showLoading) {
        if (selectedView == null) {
            return;
        }
        UiState<PagedReservations> current = state.getValue();
        boolean hasVisibleData = current != null && current.getStatus() == UiState.Status.SUCCESS;
        if (showLoading || !hasVisibleData) {
            state.setValue(UiState.loading());
        }
        String requestedView = selectedView;
        int generation = ++requestGeneration;
        repository.getReservations(requestedView, "", 1, new ApiCallback<>() {
            @Override
            public void onSuccess(PagedReservations value) {
                if (generation != requestGeneration || !requestedView.equals(selectedView)) {
                    return;
                }
                refreshError.setValue(null);
                state.setValue(value.getItems().isEmpty()
                        ? UiState.empty()
                        : UiState.success(value));
            }

            @Override
            public void onError(ApiError error) {
                if (generation != requestGeneration || !requestedView.equals(selectedView)) {
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
