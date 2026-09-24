package com.solargrid.exchange.ui.reservations;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.data.model.Slot;
import com.solargrid.exchange.features.reservations.ReservationFormData;
import com.solargrid.exchange.features.reservations.ReservationRepository;
import com.solargrid.exchange.features.stations.StationRepository;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.UUID;

public final class ReservationFormViewModel extends AndroidViewModel {
    private final ReservationRepository reservationRepository;
    private final StationRepository stationRepository;
    private final MutableLiveData<UiState<ReservationFormData>> formState =
            new MutableLiveData<>(UiState.idle());
    private final MutableLiveData<UiState<Reservation>> mutationState =
            new MutableLiveData<>(UiState.idle());
    private ReservationFormData formData;
    private String configuredKey;
    private String requestFingerprint;
    private String idempotencyKey;

    public ReservationFormViewModel(@NonNull Application application) {
        super(application);
        SolarGridApplication app = (SolarGridApplication) application;
        reservationRepository = app.getAppContainer().getReservationRepository();
        stationRepository = app.getAppContainer().getStationRepository();
    }

    public LiveData<UiState<ReservationFormData>> getFormState() { return formState; }
    public LiveData<UiState<Reservation>> getMutationState() { return mutationState; }

    public void configureCreate(
            String stationName,
            String stationId,
            String slotId,
            String startUtc,
            String endUtc,
            double availableCapacity) {
        String key = "create|" + slotId;
        if (key.equals(configuredKey)) {
            return;
        }
        configuredKey = key;
        Slot slot = new Slot(slotId, stationId, startUtc, endUtc, availableCapacity, "Available");
        formData = new ReservationFormData(false, stationName, null, Collections.singletonList(slot));
        formState.setValue(UiState.success(formData));
    }

    public void configureEdit(String reservationId) {
        String key = "edit|" + reservationId;
        if (key.equals(configuredKey)) {
            return;
        }
        configuredKey = key;
        formState.setValue(UiState.loading());
        reservationRepository.getReservation(reservationId, new ApiCallback<>() {
            @Override
            public void onSuccess(Reservation reservation) {
                loadEditSlots(reservation);
            }

            @Override
            public void onError(ApiError error) {
                formState.setValue(UiState.error(error));
            }
        });
    }

    public void retry() {
        String key = configuredKey;
        configuredKey = null;
        if (key == null) {
            return;
        }
        if (key.startsWith("edit|")) {
            configureEdit(key.substring(5));
        }
    }

    private void loadEditSlots(Reservation reservation) {
        stationRepository.getAvailableSlots(reservation.getStationId(), new ApiCallback<>() {
            @Override
            public void onSuccess(List<Slot> availableSlots) {
                List<Slot> slots = new ArrayList<>(availableSlots);
                boolean containsCurrent = false;
                for (Slot slot : slots) {
                    containsCurrent |= reservation.getSlotId().equals(slot.getId());
                }
                if (!containsCurrent) {
                    slots.add(0, new Slot(
                            reservation.getSlotId(),
                            reservation.getStationId(),
                            reservation.getScheduledStartTimeUtc(),
                            reservation.getScheduledEndTimeUtc(),
                            reservation.getRequestedEnergyKwh(),
                            "Current reservation"));
                }
                formData = new ReservationFormData(
                        true,
                        ReservationFormatters.station(
                                reservation.getStationName(), reservation.getStationId()),
                        reservation,
                        slots);
                formState.setValue(UiState.success(formData));
            }

            @Override
            public void onError(ApiError error) {
                formState.setValue(UiState.error(error));
            }
        });
    }

    public void submit(Slot slot, double requestedEnergyKwh) {
        if (formData == null || slot == null) {
            mutationState.setValue(UiState.error(new ApiError(
                    ApiError.Kind.VALIDATION, 0, "Select an available slot.")));
            return;
        }
        if (!Double.isFinite(requestedEnergyKwh) || requestedEnergyKwh <= 0) {
            mutationState.setValue(UiState.error(new ApiError(
                    ApiError.Kind.VALIDATION, 0, "Requested energy must be greater than zero.")));
            return;
        }

        Reservation current = formData.getReservation();
        String fingerprint = (formData.isEditing() ? current.getId() + "|" + current.getVersion() : "create")
                + "|" + slot.getId() + "|" + requestedEnergyKwh;
        if (!fingerprint.equals(requestFingerprint)) {
            requestFingerprint = fingerprint;
            idempotencyKey = UUID.randomUUID().toString();
        }

        mutationState.setValue(UiState.loading());
        ApiCallback<Reservation> callback = new ApiCallback<>() {
            @Override
            public void onSuccess(Reservation value) {
                mutationState.setValue(UiState.success(value));
            }

            @Override
            public void onError(ApiError error) {
                mutationState.setValue(UiState.error(error));
            }
        };
        if (formData.isEditing()) {
            reservationRepository.updateReservation(
                    current.getId(),
                    slot.getId(),
                    requestedEnergyKwh,
                    current.getVersion(),
                    idempotencyKey,
                    callback);
        } else {
            reservationRepository.createOwnReservation(
                    slot.getId(), requestedEnergyKwh, idempotencyKey, callback);
        }
    }

    public void consumeMutation() {
        mutationState.setValue(UiState.idle());
    }
}
