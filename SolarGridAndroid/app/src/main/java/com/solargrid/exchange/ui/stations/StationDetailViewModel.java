package com.solargrid.exchange.ui.stations;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.features.stations.StationDetailData;
import com.solargrid.exchange.features.stations.StationRepository;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

public final class StationDetailViewModel extends AndroidViewModel {
    private final StationRepository repository;
    private final MutableLiveData<UiState<StationDetailData>> state =
            new MutableLiveData<>(UiState.idle());
    private String loadedStationId;

    public StationDetailViewModel(@NonNull Application application) {
        super(application);
        repository = ((SolarGridApplication) application).getAppContainer().getStationRepository();
    }

    public LiveData<UiState<StationDetailData>> getState() { return state; }

    public void load(String stationId) {
        if (stationId.equals(loadedStationId)) {
            return;
        }
        loadedStationId = stationId;
        refresh();
    }

    public void refresh() {
        if (loadedStationId == null || loadedStationId.isEmpty()) {
            state.setValue(UiState.error(new ApiError(
                    ApiError.Kind.NOT_FOUND, 404, "The station reference is missing.")));
            return;
        }
        state.setValue(UiState.loading());
        repository.getStationWithAvailableSlots(loadedStationId, new ApiCallback<>() {
            @Override
            public void onSuccess(StationDetailData value) {
                state.setValue(UiState.success(value));
            }

            @Override
            public void onError(ApiError error) {
                state.setValue(UiState.error(error));
            }
        });
    }
}
