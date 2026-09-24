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
    private String selectedView;

    public ReservationListViewModel(@NonNull Application application) {
        super(application);
        repository = ((SolarGridApplication) application)
                .getAppContainer()
                .getReservationRepository();
    }

    public LiveData<UiState<PagedReservations>> getState() { return state; }

    public void load(String view) {
        if (view.equals(selectedView) && state.getValue() != null
                && state.getValue().getStatus() != UiState.Status.IDLE) {
            return;
        }
        selectedView = view;
        refresh();
    }

    public void refresh() {
        if (selectedView == null) {
            return;
        }
        state.setValue(UiState.loading());
        repository.getReservations(selectedView, "", 1, new ApiCallback<>() {
            @Override
            public void onSuccess(PagedReservations value) {
                state.setValue(value.getItems().isEmpty()
                        ? UiState.empty()
                        : UiState.success(value));
            }

            @Override
            public void onError(ApiError error) {
                state.setValue(UiState.error(error));
            }
        });
    }
}
