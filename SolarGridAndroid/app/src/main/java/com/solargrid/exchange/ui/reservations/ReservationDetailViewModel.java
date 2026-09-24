package com.solargrid.exchange.ui.reservations;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.features.reservations.ReservationRepository;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

import java.util.UUID;

public final class ReservationDetailViewModel extends AndroidViewModel {
    private final ReservationRepository repository;
    private final MutableLiveData<UiState<Reservation>> state =
            new MutableLiveData<>(UiState.idle());
    private final MutableLiveData<ApiError> refreshError = new MutableLiveData<>();
    private final MutableLiveData<UiState<Reservation>> cancellationState =
            new MutableLiveData<>(UiState.idle());
    private String reservationId;
    private String cancellationFingerprint;
    private String cancellationKey;
    private int requestGeneration;
    private int scrollY;

    public ReservationDetailViewModel(@NonNull Application application) {
        super(application);
        repository = ((SolarGridApplication) application)
                .getAppContainer()
                .getReservationRepository();
    }

    public LiveData<UiState<Reservation>> getState() { return state; }
    public LiveData<ApiError> getRefreshError() { return refreshError; }
    public LiveData<UiState<Reservation>> getCancellationState() { return cancellationState; }
    public int getScrollY() { return scrollY; }
    public boolean isCancellationInProgress() {
        UiState<Reservation> current = cancellationState.getValue();
        return current != null && current.getStatus() == UiState.Status.LOADING;
    }

    public void load(String id) {
        if (id.equals(reservationId) && state.getValue() != null
                && state.getValue().getStatus() != UiState.Status.IDLE) {
            return;
        }
        reservationId = id;
        refresh();
    }

    public void refresh() {
        request(true);
    }

    public void refreshSilently() {
        request(false);
    }

    public void rememberScroll(int value) {
        scrollY = Math.max(0, value);
    }

    private void request(boolean showLoading) {
        if (reservationId == null || reservationId.isEmpty()) {
            state.setValue(UiState.error(new ApiError(
                    ApiError.Kind.NOT_FOUND, 404, "The reservation reference is missing.")));
            return;
        }
        UiState<Reservation> current = state.getValue();
        boolean hasVisibleData = current != null && current.getStatus() == UiState.Status.SUCCESS;
        if (showLoading || !hasVisibleData) {
            state.setValue(UiState.loading());
        }
        String requestedId = reservationId;
        int generation = ++requestGeneration;
        repository.getReservation(requestedId, new ApiCallback<>() {
            @Override
            public void onSuccess(Reservation value) {
                if (generation != requestGeneration || !requestedId.equals(reservationId)) {
                    return;
                }
                refreshError.setValue(null);
                state.setValue(UiState.success(value));
            }

            @Override
            public void onError(ApiError error) {
                if (generation != requestGeneration || !requestedId.equals(reservationId)) {
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

    public void cancel(Reservation reservation, String reason) {
        if (isCancellationInProgress()) {
            return;
        }
        String normalizedReason = reason == null ? "" : reason.trim();
        String fingerprint = reservation.getId() + "|" + reservation.getVersion() + "|" + normalizedReason;
        if (!fingerprint.equals(cancellationFingerprint)) {
            cancellationFingerprint = fingerprint;
            cancellationKey = UUID.randomUUID().toString();
        }
        cancellationState.setValue(UiState.loading());
        repository.cancelReservation(
                reservation.getId(),
                reservation.getVersion(),
                normalizedReason,
                cancellationKey,
                new ApiCallback<>() {
                    @Override
                    public void onSuccess(Reservation value) {
                        cancellationState.setValue(UiState.success(value));
                    }

                    @Override
                    public void onError(ApiError error) {
                        cancellationState.setValue(UiState.error(error));
                    }
                });
    }

    public void consumeCancellation() {
        cancellationState.setValue(UiState.idle());
    }
}
