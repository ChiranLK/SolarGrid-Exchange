package com.solargrid.exchange.ui.reservations;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.PagedReservations;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.features.reservations.ReservationCreationReconciler;
import com.solargrid.exchange.features.reservations.ReservationRepository;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

import java.util.HashSet;
import java.util.Locale;
import java.util.Set;
import java.util.UUID;

public final class BookingReviewViewModel extends AndroidViewModel {
    public enum Phase { IDLE, LOADING_BASELINE, SUBMITTING, RECONCILING, REFRESHING }

    private final ReservationRepository repository;
    private final MutableLiveData<UiState<Reservation>> state =
            new MutableLiveData<>(UiState.idle());
    private final MutableLiveData<Phase> phase = new MutableLiveData<>(Phase.IDLE);
    private final Set<String> baselineReservationIds = new HashSet<>();
    private String requestFingerprint;
    private String idempotencyKey;
    private boolean baselineCaptured;

    public BookingReviewViewModel(@NonNull Application application) {
        super(application);
        repository = ((SolarGridApplication) application)
                .getAppContainer()
                .getReservationRepository();
    }

    public LiveData<UiState<Reservation>> getState() { return state; }
    public LiveData<Phase> getPhase() { return phase; }

    public void submit(String slotId, double requestedEnergyKwh) {
        UiState<Reservation> currentState = state.getValue();
        if (currentState != null && currentState.getStatus() == UiState.Status.LOADING) {
            return;
        }
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
            baselineCaptured = false;
            baselineReservationIds.clear();
        }
        state.setValue(UiState.loading());
        if (!baselineCaptured) {
            captureBaseline(slotId, requestedEnergyKwh);
            return;
        }
        createReservation(slotId, requestedEnergyKwh);
    }

    private void captureBaseline(String slotId, double requestedEnergyKwh) {
        phase.setValue(Phase.LOADING_BASELINE);
        repository.getReservations("All", "", 1, new ApiCallback<>() {
            @Override
            public void onSuccess(PagedReservations value) {
                baselineReservationIds.clear();
                for (Reservation reservation : value.getItems()) {
                    baselineReservationIds.add(reservation.getId());
                }
                baselineCaptured = true;
                createReservation(slotId, requestedEnergyKwh);
            }

            @Override
            public void onError(ApiError error) {
                finishError(explainCreationError(error));
            }
        });
    }

    private void createReservation(String slotId, double requestedEnergyKwh) {
        phase.setValue(Phase.SUBMITTING);
        repository.createOwnReservation(
                slotId,
                requestedEnergyKwh,
                idempotencyKey,
                new ApiCallback<>() {
                    @Override
                    public void onSuccess(Reservation value) {
                        refreshPendingThenFinish(value);
                    }

                    @Override
                    public void onError(ApiError error) {
                        if (error.getKind() == ApiError.Kind.NETWORK) {
                            reconcileUncertainResult(slotId, requestedEnergyKwh);
                        } else {
                            finishError(explainCreationError(error));
                        }
                    }
                });
    }

    private void reconcileUncertainResult(String slotId, double requestedEnergyKwh) {
        phase.setValue(Phase.RECONCILING);
        repository.getReservations("Pending", "", 1, new ApiCallback<>() {
            @Override
            public void onSuccess(PagedReservations value) {
                Reservation match = ReservationCreationReconciler.findNewPendingMatch(
                        value.getItems(),
                        baselineReservationIds,
                        slotId,
                        requestedEnergyKwh);
                if (match != null) {
                    state.setValue(UiState.success(match));
                    return;
                }
                finishError(new ApiError(
                        ApiError.Kind.NETWORK,
                        0,
                        "The API result is still uncertain and no new matching Pending reservation " +
                                "was found. Check your connection and retry; the same request " +
                                "identifier will be reused to prevent a duplicate booking."));
            }

            @Override
            public void onError(ApiError error) {
                if (error.isAuthenticationExpired()) {
                    finishError(error);
                } else {
                    finishError(new ApiError(
                            ApiError.Kind.NETWORK,
                            0,
                            "The connection was interrupted and the reservation list could not " +
                                    "confirm the outcome. Retry when connected; the same request " +
                                    "identifier will be reused."));
                }
            }
        });
    }

    private void refreshPendingThenFinish(Reservation reservation) {
        phase.setValue(Phase.REFRESHING);
        repository.getReservations("Pending", "", 1, new ApiCallback<>() {
            @Override
            public void onSuccess(PagedReservations ignored) {
                state.setValue(UiState.success(reservation));
            }

            @Override
            public void onError(ApiError ignored) {
                // The successful creation response remains authoritative even if list refresh fails.
                state.setValue(UiState.success(reservation));
            }
        });
    }

    private void finishError(ApiError error) {
        phase.setValue(Phase.IDLE);
        state.setValue(UiState.error(error));
    }

    private static ApiError explainCreationError(ApiError error) {
        String detail = error.getMessage() == null ? "" : error.getMessage().trim();
        String lower = detail.toLowerCase(Locale.US);
        String message;
        switch (error.getKind()) {
            case VALIDATION:
                message = lower.contains("day") || lower.contains("horizon") || lower.contains("future")
                        ? "Choose a future slot within the seven-day booking window."
                        : "Check the selected slot and enter a positive energy quantity.";
                break;
            case FORBIDDEN:
                message = "This signed-in account is not allowed to create this reservation.";
                break;
            case NOT_FOUND:
                message = "The selected station or slot no longer exists or is no longer available. " +
                        "Return to the station and refresh its slots.";
                break;
            case CONFLICT:
                if (lower.contains("capacity") || lower.contains("fully") || lower.contains("available")) {
                    message = "The slot no longer has enough available capacity. Refresh the slots " +
                            "and choose a smaller quantity or another time.";
                } else if (lower.contains("duplicate") || lower.contains("same slot")) {
                    message = "A reservation for this slot already exists for your account. " +
                            "Open current bookings before trying again.";
                } else if (lower.contains("overlap")) {
                    message = "This slot overlaps another active reservation on your account.";
                } else {
                    message = "The station, slot, capacity, or account state changed before the " +
                            "booking could be created. Refresh and review the selection.";
                }
                break;
            case SERVER:
                message = "The SolarGrid API could not complete the booking. Try again later.";
                break;
            case NETWORK:
                message = "The SolarGrid API could not be reached. Check the connection and retry.";
                break;
            default:
                return error;
        }
        if (!detail.isEmpty() && !message.contains(detail)) {
            message += " API detail: " + detail;
        }
        return new ApiError(error.getKind(), error.getStatusCode(), message);
    }

    public void consumeResult() {
        state.setValue(UiState.idle());
        phase.setValue(Phase.IDLE);
    }

    public void clearAfterSuccess() {
        requestFingerprint = null;
        idempotencyKey = null;
        baselineCaptured = false;
        baselineReservationIds.clear();
        consumeResult();
    }
}
