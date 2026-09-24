package com.solargrid.exchange.ui.stations;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.Station;
import com.solargrid.exchange.features.stations.StationRepository;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

import java.util.List;

public final class StationListViewModel extends AndroidViewModel {
    private final StationRepository repository;
    private final MutableLiveData<UiState<List<Station>>> state =
            new MutableLiveData<>(UiState.idle());
    private boolean loaded;

    public StationListViewModel(@NonNull Application application) {
        super(application);
        repository = ((SolarGridApplication) application).getAppContainer().getStationRepository();
    }

    public LiveData<UiState<List<Station>>> getState() { return state; }

    public void load() {
        if (loaded) {
            return;
        }
        refresh();
    }

    public void refresh() {
        loaded = true;
        state.setValue(UiState.loading());
        repository.getActiveStations(new ApiCallback<>() {
            @Override
            public void onSuccess(List<Station> value) {
                state.setValue(value.isEmpty() ? UiState.empty() : UiState.success(value));
            }

            @Override
            public void onError(ApiError error) {
                state.setValue(UiState.error(error));
            }
        });
    }
}
