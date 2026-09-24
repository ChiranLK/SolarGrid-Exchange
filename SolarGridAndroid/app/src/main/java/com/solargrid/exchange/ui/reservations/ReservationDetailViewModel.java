package com.solargrid.exchange.ui.reservations;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.data.model.PagedReservations;
import com.solargrid.exchange.features.reservations.ReservationMutationReconciler;
import com.solargrid.exchange.features.reservations.ReservationRepository;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

import java.util.UUID;
import java.util.Locale;
import java.util.concurrent.atomic.AtomicInteger;

public final class ReservationDetailViewModel extends AndroidViewModel {
    public enum CancellationPhase { IDLE, SUBMITTING, RECONCILING, REFRESHING }

    private final ReservationRepository repository;
    private final MutableLiveData<UiState<Reservation>> state =
            new MutableLiveData<>(UiState.idle());
    private final MutableLiveData<ApiError> refreshError = new MutableLiveData<>();
    private final MutableLiveData<UiState<Reservation>> cancellationState =
            new MutableLiveData<>(UiState.idle());
    private final MutableLiveData<CancellationPhase> cancellationPhase =
            new MutableLiveData<>(CancellationPhase.IDLE);
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
    public LiveData<CancellationPhase> getCancellationPhase() { return cancellationPhase; }
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
        cancellationPhase.setValue(CancellationPhase.SUBMITTING);
        repository.cancelReservation(
                reservation.getId(),
                reservation.getVersion(),
                normalizedReason,
                cancellationKey,
                new ApiCallback<>() {
                    @Override
                    public void onSuccess(Reservation value) {
                        refreshRelatedViewsThenFinish(value);
                    }

                    @Override
                    public void onError(ApiError error) {
                        if (error.getKind() == ApiError.Kind.NETWORK) {
                            reconcileUncertainCancellation(reservation, normalizedReason);
                        } else if (error.getKind() == ApiError.Kind.CONFLICT) {
                            refreshAfterCancellationConflict(reservation, error);
                        } else {
                            finishCancellationError(explainCancellationError(error));
                        }
                    }
                });
    }

    private void refreshAfterCancellationConflict(Reservation original, ApiError conflict) {
        cancellationPhase.setValue(CancellationPhase.RECONCILING);
        repository.getReservation(original.getId(), new ApiCallback<>() {
            @Override
            public void onSuccess(Reservation current) {
                state.setValue(UiState.success(current));
                if ("Completed".equals(current.getStatus())) {
                    finishCancellationError(new ApiError(
                            ApiError.Kind.CONFLICT,
                            409,
                            "This reservation is Completed on the server and cannot be cancelled."));
                } else {
                    finishCancellationError(explainCancellationError(conflict));
                }
            }

            @Override
            public void onError(ApiError error) {
                finishCancellationError(error.isAuthenticationExpired()
                        ? error
                        : explainCancellationError(conflict));
            }
        });
    }

    private void reconcileUncertainCancellation(Reservation original, String requestedReason) {
        cancellationPhase.setValue(CancellationPhase.RECONCILING);
        repository.getReservation(original.getId(), new ApiCallback<>() {
            @Override
            public void onSuccess(Reservation current) {
                if (ReservationMutationReconciler.matchesCancellation(
                        current, original.getVersion(), requestedReason)) {
                    refreshRelatedViewsThenFinish(current);
                    return;
                }
                if ("Completed".equals(current.getStatus())) {
                    finishCancellationError(new ApiError(
                            ApiError.Kind.CONFLICT,
                            409,
                            "The reservation completed before cancellation could be confirmed. " +
                                    "The Completed server result remains authoritative."));
                    return;
                }
                finishCancellationError(uncertainCancellationError());
            }

            @Override
            public void onError(ApiError error) {
                finishCancellationError(error.isAuthenticationExpired()
                        ? error
                        : uncertainCancellationError());
            }
        });
    }

    private void refreshRelatedViewsThenFinish(Reservation result) {
        cancellationPhase.setValue(CancellationPhase.REFRESHING);
        AtomicInteger remaining = new AtomicInteger(4);
        ApiCallback<PagedReservations> callback = new ApiCallback<>() {
            @Override
            public void onSuccess(PagedReservations ignored) {
                completeRefresh(result, remaining);
            }

            @Override
            public void onError(ApiError ignored) {
                completeRefresh(result, remaining);
            }
        };
        repository.getReservations("Pending", "", 1, callback);
        repository.getReservations("Current", "", 1, callback);
        repository.getReservations("ApprovedFuture", "", 1, callback);
        repository.getReservations("History", "", 1, callback);
    }

    private void completeRefresh(Reservation result, AtomicInteger remaining) {
        if (remaining.decrementAndGet() == 0) {
            cancellationPhase.setValue(CancellationPhase.IDLE);
            cancellationState.setValue(UiState.success(result));
        }
    }

    private void finishCancellationError(ApiError error) {
        cancellationPhase.setValue(CancellationPhase.IDLE);
        cancellationState.setValue(UiState.error(error));
    }

    private static ApiError uncertainCancellationError() {
        return new ApiError(
                ApiError.Kind.NETWORK,
                0,
                "The connection was interrupted and the API could not confirm cancellation. " +
                        "This booking has not been labelled Cancelled locally. Retry when connected; " +
                        "the same request identifier will be reused safely.");
    }

    private static ApiError explainCancellationError(ApiError error) {
        if (error.isAuthenticationExpired()) {
            return error;
        }
        String detail = error.getMessage() == null ? "" : error.getMessage().trim();
        String lower = detail.toLowerCase(Locale.US);
        String message;
        switch (error.getKind()) {
            case CONFLICT:
                if (lower.contains("hour") || lower.contains("notice")) {
                    message = "This reservation is inside the twelve-hour cancellation cutoff " +
                            "and remains active.";
                } else if (lower.contains("changed") || lower.contains("version")
                        || lower.contains("reload") || lower.contains("stale")) {
                    message = "The reservation changed on the server. Reload it before cancelling " +
                            "again; the stale cancellation was not applied.";
                } else if (lower.contains("completed")) {
                    message = "A completed reservation cannot be cancelled.";
                } else if (lower.contains("pending") || lower.contains("approved")
                        || lower.contains("status")) {
                    message = "The current server status no longer permits cancellation.";
                } else if (lower.contains("capacity") || lower.contains("reconcil")) {
                    message = "The server must reconcile held capacity before this reservation " +
                            "can be cancelled. No local cancellation was recorded.";
                } else {
                    message = "Cancellation conflicts with the latest server state. Reload the " +
                            "reservation and review its status.";
                }
                break;
            case VALIDATION:
                message = "The cancellation request was invalid. Review the optional reason.";
                break;
            case NOT_FOUND:
                message = "The reservation no longer exists or is outside this account's scope.";
                break;
            case FORBIDDEN:
                message = "This signed-in account cannot cancel this reservation.";
                break;
            case SERVER:
                message = "The API could not complete cancellation. The booking was not marked " +
                        "Cancelled by this client.";
                break;
            default:
                return error;
        }
        if (!detail.isEmpty() && !message.contains(detail)) {
            message += " API detail: " + detail;
        }
        return new ApiError(error.getKind(), error.getStatusCode(), message);
    }

    public void consumeCancellation() {
        cancellationState.setValue(UiState.idle());
        cancellationPhase.setValue(CancellationPhase.IDLE);
    }
}
