package com.solargrid.exchange.ui.stations;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.NearbyStation;
import com.solargrid.exchange.features.stations.StationRepository;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

import java.util.List;

public final class StationListViewModel extends AndroidViewModel {
    private final StationRepository repository;
    private final MutableLiveData<UiState<List<NearbyStation>>> state =
            new MutableLiveData<>(UiState.idle());
    private int requestGeneration;

    public StationListViewModel(@NonNull Application application) {
        super(application);
        repository = ((SolarGridApplication) application).getAppContainer().getStationRepository();
    }

    public LiveData<UiState<List<NearbyStation>>> getState() { return state; }

    public void loadNearby(double latitude, double longitude) {
        int generation = ++requestGeneration;
        state.setValue(UiState.loading());
        repository.getNearbyStations(latitude, longitude, new ApiCallback<>() {
            @Override
            public void onSuccess(List<NearbyStation> value) {
                if (generation == requestGeneration) {
                    state.setValue(value.isEmpty() ? UiState.empty() : UiState.success(value));
                }
            }

            @Override
            public void onError(ApiError error) {
                if (generation == requestGeneration) {
                    state.setValue(UiState.error(error));
                }
            }
        });
    }
}
