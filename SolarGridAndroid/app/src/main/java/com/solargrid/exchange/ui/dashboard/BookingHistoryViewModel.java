package com.solargrid.exchange.ui.dashboard;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;
import androidx.lifecycle.SavedStateHandle;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.PagedBookingHistory;
import com.solargrid.exchange.data.model.Station;
import com.solargrid.exchange.features.dashboard.BookingHistoryQuery;
import com.solargrid.exchange.features.dashboard.DashboardRepository;
import com.solargrid.exchange.features.stations.StationRepository;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

import java.util.Collections;
import java.util.List;

public final class BookingHistoryViewModel extends AndroidViewModel {
    private static final String SEARCH = "history.search";
    private static final String STATUS = "history.status";
    private static final String STATION = "history.station";
    private static final String FROM_UTC = "history.fromUtc";
    private static final String TO_UTC = "history.toUtc";
    private static final String PAGE = "history.page";

    private final DashboardRepository dashboardRepository;
    private final StationRepository stationRepository;
    private final SavedStateHandle savedState;
    private final MutableLiveData<UiState<PagedBookingHistory>> state =
            new MutableLiveData<>(UiState.idle());
    private final MutableLiveData<List<Station>> stations =
            new MutableLiveData<>(Collections.emptyList());
    private final MutableLiveData<ApiError> stationError = new MutableLiveData<>();
    private int requestGeneration;

    public BookingHistoryViewModel(
            @NonNull Application application,
            @NonNull SavedStateHandle savedState) {
        super(application);
        this.savedState = savedState;
        dashboardRepository = ((SolarGridApplication) application)
                .getAppContainer()
                .getDashboardRepository();
        stationRepository = ((SolarGridApplication) application)
                .getAppContainer()
                .getStationRepository();
    }

    public LiveData<UiState<PagedBookingHistory>> getState() { return state; }
    public LiveData<List<Station>> getStations() { return stations; }
    public LiveData<ApiError> getStationError() { return stationError; }
    public String getSearch() { return savedString(SEARCH); }
    public String getStatus() { return savedString(STATUS); }
    public String getStationId() { return savedString(STATION); }
    public String getFromUtc() { return savedString(FROM_UTC); }
    public String getToUtc() { return savedString(TO_UTC); }
    public int getPage() { Integer value = savedState.get(PAGE); return value == null ? 1 : value; }

    public void load() {
        if (state.getValue() == null || state.getValue().getStatus() == UiState.Status.IDLE) {
            request(true);
            loadStations();
        }
    }

    public void applyFilters(
            String search,
            String status,
            String stationId,
            String fromUtc,
            String toUtc) {
        savedState.set(SEARCH, search == null ? "" : search.trim());
        savedState.set(STATUS, status == null ? "" : status.trim());
        savedState.set(STATION, stationId == null ? "" : stationId.trim());
        savedState.set(FROM_UTC, fromUtc == null ? "" : fromUtc.trim());
        savedState.set(TO_UTC, toUtc == null ? "" : toUtc.trim());
        savedState.set(PAGE, 1);
        request(true);
    }

    public void previousPage() {
        if (getPage() > 1) {
            savedState.set(PAGE, getPage() - 1);
            request(true);
        }
    }

    public void nextPage() {
        UiState<PagedBookingHistory> current = state.getValue();
        if (current != null && current.getData() != null
                && getPage() < current.getData().getTotalPages()) {
            savedState.set(PAGE, getPage() + 1);
            request(true);
        }
    }

    public void refresh() { request(true); }

    private void request(boolean showLoading) {
        BookingHistoryQuery query = new BookingHistoryQuery(
                getSearch(), getStatus(), getStationId(), getFromUtc(), getToUtc(), getPage());
        String validation = query.validationMessage();
        if (!validation.isEmpty()) {
            state.setValue(UiState.error(new ApiError(ApiError.Kind.VALIDATION, 0, validation)));
            return;
        }
        if (showLoading) {
            state.setValue(UiState.loading());
        }
        int generation = ++requestGeneration;
        dashboardRepository.getHistory(query, new ApiCallback<>() {
            @Override
            public void onSuccess(PagedBookingHistory value) {
                if (generation != requestGeneration) {
                    return;
                }
                state.setValue(value.getItems().isEmpty()
                        ? UiState.empty()
                        : UiState.success(value));
            }

            @Override
            public void onError(ApiError error) {
                if (generation == requestGeneration) {
                    state.setValue(UiState.error(error));
                }
            }
        });
    }

    private void loadStations() {
        stationRepository.getActiveStations(new ApiCallback<>() {
            @Override
            public void onSuccess(List<Station> value) {
                stationError.setValue(null);
                stations.setValue(value);
            }

            @Override
            public void onError(ApiError error) {
                stationError.setValue(error);
            }
        });
    }

    private String savedString(String key) {
        String value = savedState.get(key);
        return value == null ? "" : value;
    }
}
