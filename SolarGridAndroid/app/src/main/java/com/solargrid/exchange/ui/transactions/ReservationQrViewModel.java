package com.solargrid.exchange.ui.transactions;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.QrTransactionToken;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.features.reservations.ReservationRepository;
import com.solargrid.exchange.features.transactions.QrTransactionRepository;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

public final class ReservationQrViewModel extends AndroidViewModel {
    private final ReservationRepository reservationRepository;
    private final QrTransactionRepository transactionRepository;
    private final MutableLiveData<UiState<QrDisplayData>> state =
            new MutableLiveData<>(UiState.idle());
    private String reservationId;
    private int requestGeneration;

    public ReservationQrViewModel(@NonNull Application application) {
        super(application);
        reservationRepository = ((SolarGridApplication) application)
                .getAppContainer()
                .getReservationRepository();
        transactionRepository = ((SolarGridApplication) application)
                .getAppContainer()
                .getQrTransactionRepository();
    }

    public LiveData<UiState<QrDisplayData>> getState() { return state; }

    public void load(String id) {
        if (id == null || id.trim().isEmpty()) {
            state.setValue(UiState.error(new ApiError(
                    ApiError.Kind.NOT_FOUND, 404, "The reservation reference is missing.")));
            return;
        }
        if (id.equals(reservationId) && state.getValue() != null
                && state.getValue().getStatus() != UiState.Status.IDLE) {
            return;
        }
        reservationId = id;
        request();
    }

    public void reissue() { request(); }

    private void request() {
        if (reservationId == null || reservationId.isEmpty()) {
            return;
        }
        state.setValue(UiState.loading());
        String requestedId = reservationId;
        int generation = ++requestGeneration;
        reservationRepository.getReservation(requestedId, new ApiCallback<>() {
            @Override
            public void onSuccess(Reservation reservation) {
                if (generation != requestGeneration || !requestedId.equals(reservationId)) {
                    return;
                }
                issue(requestedId, reservation, generation);
            }

            @Override
            public void onError(ApiError error) {
                finishError(requestedId, generation, error);
            }
        });
    }

    private void issue(String requestedId, Reservation reservation, int generation) {
        transactionRepository.issue(requestedId, new ApiCallback<>() {
            @Override
            public void onSuccess(QrTransactionToken token) {
                if (generation == requestGeneration && requestedId.equals(reservationId)) {
                    state.setValue(UiState.success(new QrDisplayData(reservation, token)));
                }
            }

            @Override
            public void onError(ApiError error) {
                finishError(requestedId, generation, error);
            }
        });
    }

    private void finishError(String requestedId, int generation, ApiError error) {
        if (generation == requestGeneration && requestedId.equals(reservationId)) {
            state.setValue(UiState.error(error));
        }
    }

    @Override
    protected void onCleared() {
        state.setValue(UiState.idle());
        super.onCleared();
    }
}
