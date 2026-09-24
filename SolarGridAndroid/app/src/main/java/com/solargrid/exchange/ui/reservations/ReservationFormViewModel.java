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
import com.solargrid.exchange.features.reservations.ReservationMutationReconciler;
import com.solargrid.exchange.features.reservations.ReservationRepository;
import com.solargrid.exchange.features.stations.StationRepository;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicInteger;

public final class ReservationFormViewModel extends AndroidViewModel {
    public enum Phase { IDLE, SUBMITTING, RECONCILING, REFRESHING }

    private final ReservationRepository reservationRepository;
    private final StationRepository stationRepository;
    private final MutableLiveData<UiState<ReservationFormData>> formState =
            new MutableLiveData<>(UiState.idle());
    private final MutableLiveData<UiState<Reservation>> mutationState =
            new MutableLiveData<>(UiState.idle());
    private final MutableLiveData<Phase> phase = new MutableLiveData<>(Phase.IDLE);
    private ReservationFormData formData;
    private String configuredKey;
    private String requestFingerprint;
    private String idempotencyKey;
    private String createStationName;
    private String createStationId;
    private String preferredSlotId;
    private String draftSelectedSlotId;
    private String draftEnergyText;

    public ReservationFormViewModel(@NonNull Application application) {
        super(application);
        SolarGridApplication app = (SolarGridApplication) application;
        reservationRepository = app.getAppContainer().getReservationRepository();
        stationRepository = app.getAppContainer().getStationRepository();
    }

    public LiveData<UiState<ReservationFormData>> getFormState() { return formState; }
    public LiveData<UiState<Reservation>> getMutationState() { return mutationState; }
    public LiveData<Phase> getPhase() { return phase; }

    public void configureCreate(
            String stationName,
            String stationId,
            String selectedSlotId) {
        String key = "create|" + stationId;
        if (key.equals(configuredKey)) {
            return;
        }
        configuredKey = key;
        createStationName = stationName;
        createStationId = stationId;
        preferredSlotId = selectedSlotId;
        if (draftSelectedSlotId == null || draftSelectedSlotId.isEmpty()) {
            draftSelectedSlotId = selectedSlotId;
        }
        formState.setValue(UiState.loading());
        stationRepository.getAvailableSlots(stationId, new ApiCallback<>() {
            @Override
            public void onSuccess(List<Slot> availableSlots) {
                if (availableSlots.isEmpty()) {
                    formData = null;
                    formState.setValue(UiState.empty());
                    return;
                }
                formData = new ReservationFormData(
                        false, createStationName, null, availableSlots);
                formState.setValue(UiState.success(formData));
            }

            @Override
            public void onError(ApiError error) {
                formState.setValue(UiState.error(error));
            }
        });
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
        } else if (key.startsWith("create|")) {
            configureCreate(createStationName, createStationId, preferredSlotId);
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
                draftSelectedSlotId = reservation.getSlotId();
                draftEnergyText = String.valueOf(reservation.getRequestedEnergyKwh());
                formState.setValue(UiState.success(formData));
            }

            @Override
            public void onError(ApiError error) {
                formState.setValue(UiState.error(error));
            }
        });
    }

    public void saveSelectedSlot(Slot slot) {
        draftSelectedSlotId = slot == null ? "" : slot.getId();
    }

    public void saveEnergyText(String energyText) {
        draftEnergyText = energyText;
    }

    public String getDraftSelectedSlotId() {
        return draftSelectedSlotId == null ? "" : draftSelectedSlotId;
    }

    public String getDraftEnergyText() {
        return draftEnergyText == null ? "" : draftEnergyText;
    }

    public void submitUpdate(Slot slot, double requestedEnergyKwh) {
        UiState<Reservation> currentMutation = mutationState.getValue();
        if (currentMutation != null && currentMutation.getStatus() == UiState.Status.LOADING) {
            return;
        }
        if (formData == null || !formData.isEditing() || slot == null) {
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
        String fingerprint = current.getId() + "|" + current.getVersion()
                + "|" + slot.getId() + "|" + requestedEnergyKwh;
        if (!fingerprint.equals(requestFingerprint)) {
            requestFingerprint = fingerprint;
            idempotencyKey = UUID.randomUUID().toString();
        }

        mutationState.setValue(UiState.loading());
        phase.setValue(Phase.SUBMITTING);
        ApiCallback<Reservation> callback = new ApiCallback<>() {
            @Override
            public void onSuccess(Reservation value) {
                refreshRelatedViewsThenFinish(value);
            }

            @Override
            public void onError(ApiError error) {
                if (error.getKind() == ApiError.Kind.NETWORK) {
                    reconcileUncertainUpdate(current, slot, requestedEnergyKwh);
                } else {
                    finishMutationError(explainUpdateError(error));
                }
            }
        };
        reservationRepository.updateReservation(
                current.getId(),
                slot.getId(),
                requestedEnergyKwh,
                current.getVersion(),
                idempotencyKey,
                callback);
    }

    private void reconcileUncertainUpdate(
            Reservation original,
            Slot requestedSlot,
            double requestedEnergyKwh) {
        phase.setValue(Phase.RECONCILING);
        reservationRepository.getReservation(original.getId(), new ApiCallback<>() {
            @Override
            public void onSuccess(Reservation current) {
                if (ReservationMutationReconciler.matchesUpdate(
                        current,
                        original.getVersion(),
                        original.getStatus(),
                        original.getSlotId(),
                        original.getRequestedEnergyKwh(),
                        requestedSlot.getId(),
                        requestedEnergyKwh)) {
                    refreshRelatedViewsThenFinish(current);
                    return;
                }
                if ("Completed".equals(current.getStatus())) {
                    finishMutationError(new ApiError(
                            ApiError.Kind.CONFLICT,
                            409,
                            "The reservation is now Completed and cannot be modified. " +
                                    "The completed server record was not changed."));
                    return;
                }
                finishMutationError(uncertainUpdateError());
            }

            @Override
            public void onError(ApiError error) {
                finishMutationError(error.isAuthenticationExpired() ? error : uncertainUpdateError());
            }
        });
    }

    private void refreshRelatedViewsThenFinish(Reservation result) {
        phase.setValue(Phase.REFRESHING);
        AtomicInteger remaining = new AtomicInteger(4);
        ApiCallback<com.solargrid.exchange.data.model.PagedReservations> callback =
                new ApiCallback<>() {
                    @Override
                    public void onSuccess(com.solargrid.exchange.data.model.PagedReservations ignored) {
                        completeRefresh(result, remaining);
                    }

                    @Override
                    public void onError(ApiError ignored) {
                        completeRefresh(result, remaining);
                    }
                };
        reservationRepository.getReservations("Pending", "", 1, callback);
        reservationRepository.getReservations("Current", "", 1, callback);
        reservationRepository.getReservations("ApprovedFuture", "", 1, callback);
        reservationRepository.getReservations("History", "", 1, callback);
    }

    private void completeRefresh(Reservation result, AtomicInteger remaining) {
        if (remaining.decrementAndGet() == 0) {
            phase.setValue(Phase.IDLE);
            mutationState.setValue(UiState.success(result));
        }
    }

    private void finishMutationError(ApiError error) {
        phase.setValue(Phase.IDLE);
        mutationState.setValue(UiState.error(error));
    }

    private static ApiError uncertainUpdateError() {
        return new ApiError(
                ApiError.Kind.NETWORK,
                0,
                "The connection was interrupted and the API could not confirm this update. " +
                        "The displayed booking has not been treated as updated. Retry when connected; " +
                        "the same request identifier will be reused safely.");
    }

    private static ApiError explainUpdateError(ApiError error) {
        if (error.isAuthenticationExpired()) {
            return error;
        }
        String detail = error.getMessage() == null ? "" : error.getMessage().trim();
        String lower = detail.toLowerCase(Locale.US);
        String message;
        switch (error.getKind()) {
            case CONFLICT:
                if (lower.contains("hour") || lower.contains("notice")) {
                    message = "This reservation is inside the twelve-hour change cutoff. " +
                            "The original booking remains unchanged.";
                } else if (lower.contains("capacity") || lower.contains("available")
                        || lower.contains("energy")) {
                    message = "The selected slot no longer has enough capacity. " +
                            "The original booking and its capacity remain unchanged.";
                } else if (lower.contains("changed") || lower.contains("version")
                        || lower.contains("reload") || lower.contains("stale")) {
                    message = "The reservation changed on the server. Reload its details before " +
                            "editing again; this stale update was not applied.";
                } else if (lower.contains("pending") || lower.contains("approved")
                        || lower.contains("completed") || lower.contains("cancel")) {
                    message = "The current server status no longer allows modification. " +
                            "No replacement booking was created.";
                } else if (lower.contains("overlap") || lower.contains("duplicate")) {
                    message = "The requested time conflicts with another active reservation. " +
                            "The original booking remains unchanged.";
                } else {
                    message = "The update conflicts with the latest server state. " +
                            "The original booking remains unchanged; reload and review it.";
                }
                break;
            case VALIDATION:
                message = "Check the selected slot and positive energy quantity. " +
                        "The original booking remains unchanged.";
                break;
            case NOT_FOUND:
                message = "The reservation, station, or slot is no longer available. " +
                        "Reload the reservation before trying again.";
                break;
            case FORBIDDEN:
                message = "This signed-in account cannot modify this reservation.";
                break;
            case SERVER:
                message = "The API could not apply the update. The original booking remains unchanged.";
                break;
            default:
                return error;
        }
        if (!detail.isEmpty() && !message.contains(detail)) {
            message += " API detail: " + detail;
        }
        return new ApiError(error.getKind(), error.getStatusCode(), message);
    }

    public void consumeMutation() {
        mutationState.setValue(UiState.idle());
        phase.setValue(Phase.IDLE);
    }
}
