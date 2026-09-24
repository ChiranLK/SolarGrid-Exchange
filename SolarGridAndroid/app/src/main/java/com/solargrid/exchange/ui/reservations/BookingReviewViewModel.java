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

public final class BookingReviewViewModel extends AndroidViewModel {
    private final ReservationRepository repository;
    private final MutableLiveData<UiState<Reservation>> state =
            new MutableLiveData<>(UiState.idle());
    private String requestFingerprint;
    private String idempotencyKey;

    public BookingReviewViewModel(@NonNull Application application) {
        super(application);
        repository = ((SolarGridApplication) application)
                .getAppContainer()
                .getReservationRepository();
    }

    public LiveData<UiState<Reservation>> getState() { return state; }

    public void submit(String slotId, double requestedEnergyKwh) {
        if (slotId == null || slotId.isEmpty()
                || !Double.isFinite(requestedEnergyKwh)
                || requestedEnergyKwh <= 0) {
            state.setValue(UiState.error(new ApiError(
                    ApiError.Kind.VALIDATION,
                    0,
                    "The selected slot or requested energy is invalid.")));
            return;
        }
        String fingerprint = slotId + "|" + requestedEnergyKwh;
        if (!fingerprint.equals(requestFingerprint)) {
            requestFingerprint = fingerprint;
            idempotencyKey = UUID.randomUUID().toString();
        }
        state.setValue(UiState.loading());
        repository.createOwnReservation(
                slotId,
                requestedEnergyKwh,
                idempotencyKey,
                new ApiCallback<>() {
                    @Override
                    public void onSuccess(Reservation value) {
                        state.setValue(UiState.success(value));
                    }

                    @Override
                    public void onError(ApiError error) {
                        state.setValue(UiState.error(error));
                    }
                });
    }

    public void consumeResult() {
        state.setValue(UiState.idle());
    }
}
