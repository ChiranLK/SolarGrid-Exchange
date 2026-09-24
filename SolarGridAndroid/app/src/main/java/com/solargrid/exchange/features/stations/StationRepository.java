package com.solargrid.exchange.features.stations;

import com.solargrid.exchange.data.model.Slot;
import com.solargrid.exchange.data.model.Station;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiClient;
import com.solargrid.exchange.network.ApiError;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.UnsupportedEncodingException;
import java.net.URLEncoder;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;
import java.util.TimeZone;

public final class StationRepository {
    private final ApiClient apiClient;

    public StationRepository(ApiClient apiClient) {
        this.apiClient = apiClient;
    }

    public void getActiveStations(ApiCallback<List<Station>> callback) {
        apiClient.get("stations?isActive=true&page=1&pageSize=100", new ApiCallback<>() {
            @Override
            public void onSuccess(JSONObject value) {
                callback.onSuccess(parseStations(value.optJSONArray("items")));
            }

            @Override
            public void onError(ApiError error) {
                callback.onError(error);
            }
        });
    }

    public void getStationWithAvailableSlots(
            String stationId,
            ApiCallback<StationDetailData> callback) {
        String encodedStationId = encode(stationId);
        apiClient.get("stations/" + encodedStationId, new ApiCallback<>() {
            @Override
            public void onSuccess(JSONObject value) {
                Station station = parseStation(value);
                loadAvailableSlots(encodedStationId, new ApiCallback<>() {
                    @Override
                    public void onSuccess(List<Slot> slots) {
                        callback.onSuccess(new StationDetailData(station, slots));
                    }

                    @Override
                    public void onError(ApiError error) {
                        callback.onError(error);
                    }
                });
            }

            @Override
            public void onError(ApiError error) {
                callback.onError(error);
            }
        });
    }

    private void loadAvailableSlots(String encodedStationId, ApiCallback<List<Slot>> callback) {
        SimpleDateFormat utcFormat = new SimpleDateFormat(
                "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'",
                Locale.US);
        utcFormat.setTimeZone(TimeZone.getTimeZone("UTC"));
        String fromUtc = encode(utcFormat.format(new Date()));
        apiClient.get(
                "stations/" + encodedStationId + "/slots/available?fromUtc=" + fromUtc +
                        "&page=1&pageSize=100",
                new ApiCallback<>() {
                    @Override
                    public void onSuccess(JSONObject value) {
                        callback.onSuccess(parseSlots(value.optJSONArray("items")));
                    }

                    @Override
                    public void onError(ApiError error) {
                        callback.onError(error);
                    }
                });
    }

    private static String encode(String value) {
        try {
            return URLEncoder.encode(value, "UTF-8");
        } catch (UnsupportedEncodingException impossible) {
            throw new IllegalStateException("UTF-8 is not supported on this device.", impossible);
        }
    }

    private static List<Station> parseStations(JSONArray values) {
        List<Station> stations = new ArrayList<>();
        if (values == null) {
            return stations;
        }
        for (int index = 0; index < values.length(); index++) {
            JSONObject item = values.optJSONObject(index);
            if (item != null) {
                stations.add(parseStation(item));
            }
        }
        return stations;
    }

    private static Station parseStation(JSONObject value) {
        return new Station(
                value.optString("id", ""),
                value.optString("name", "Unknown station"),
                value.optString("description", ""),
                value.optString("address", ""),
                value.optDouble("latitude", 0),
                value.optDouble("longitude", 0),
                value.optDouble("energyGenerationCapacityKw", 0),
                value.optDouble("batteryStorageCapacityKwh", 0),
                value.optBoolean("isActive", false));
    }

    private static List<Slot> parseSlots(JSONArray values) {
        List<Slot> slots = new ArrayList<>();
        if (values == null) {
            return slots;
        }
        for (int index = 0; index < values.length(); index++) {
            JSONObject item = values.optJSONObject(index);
            if (item != null) {
                slots.add(new Slot(
                        item.optString("id", ""),
                        item.optString("stationId", ""),
                        item.optString("startTimeUtc", ""),
                        item.optString("endTimeUtc", ""),
                        item.optDouble("availableCapacityKwh", 0),
                        item.optString("availabilityStatus", "")));
            }
        }
        return slots;
    }
}
