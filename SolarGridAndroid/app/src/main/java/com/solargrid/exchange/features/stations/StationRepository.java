package com.solargrid.exchange.features.stations;

import com.solargrid.exchange.data.model.Slot;
import com.solargrid.exchange.data.model.NearbyStation;
import com.solargrid.exchange.data.model.Station;
import com.solargrid.exchange.data.local.ReferenceDataStore;
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
    private final ReferenceDataStore referenceDataStore;

    public StationRepository(ApiClient apiClient, ReferenceDataStore referenceDataStore) {
        this.apiClient = apiClient;
        this.referenceDataStore = referenceDataStore;
    }

    public void getActiveStations(ApiCallback<List<Station>> callback) {
        apiClient.get("stations?isActive=true&page=1&pageSize=100", new ApiCallback<>() {
            @Override
            public void onSuccess(JSONObject value) {
                List<Station> stations = parseStations(value.optJSONArray("items"));
                referenceDataStore.upsertStations(stations);
                callback.onSuccess(stations);
            }

            @Override
            public void onError(ApiError error) {
                List<Station> cached = referenceDataStore.readActiveStations();
                if (isOffline(error) && !cached.isEmpty()) callback.onSuccess(cached);
                else callback.onError(error);
            }
        });
    }

    public void getNearbyStations(
            double latitude,
            double longitude,
            ApiCallback<List<NearbyStation>> callback) {
        String path = String.format(
                Locale.US,
                "stations/nearby?latitude=%.7f&longitude=%.7f&radiusKm=25&maximumResults=50",
                latitude,
                longitude);
        apiClient.getArray(path, new ApiCallback<>() {
            @Override
            public void onSuccess(JSONArray value) {
                List<NearbyStation> nearby = parseNearbyStations(value);
                List<Station> stations = new ArrayList<>();
                for (NearbyStation item : nearby) stations.add(item.getStation());
                referenceDataStore.upsertStations(stations);
                callback.onSuccess(nearby);
            }

            @Override
            public void onError(ApiError error) {
                List<NearbyStation> cached = cachedNearby(latitude, longitude);
                if (isOffline(error) && !cached.isEmpty()) callback.onSuccess(cached);
                else callback.onError(error);
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
                referenceDataStore.upsertStation(station);
                loadAvailableSlots(stationId, new ApiCallback<>() {
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
                Station station = referenceDataStore.readStation(stationId);
                List<Slot> slots = referenceDataStore.readSlots(stationId);
                if (isOffline(error) && station != null) callback.onSuccess(new StationDetailData(station, slots));
                else callback.onError(error);
            }
        });
    }

    public void getAvailableSlots(String stationId, ApiCallback<List<Slot>> callback) {
        loadAvailableSlots(encode(stationId), callback);
    }

    public void getAvailableSlotsPage(
            String stationId,
            int page,
            ApiCallback<AvailableSlotPage> callback) {
        String fromUtc = currentUtcQueryValue();
        apiClient.get("stations/" + encode(stationId) + "/slots/available?fromUtc=" +
                        fromUtc + "&page=" + page + "&pageSize=20", new ApiCallback<>() {
                    @Override
                    public void onSuccess(JSONObject value) {
                        List<Slot> slots = parseSlots(value.optJSONArray("items"));
                        referenceDataStore.replaceSlots(stationId, slots);
                        callback.onSuccess(new AvailableSlotPage(
                                slots,
                                value.optInt("page", page),
                                value.optInt("totalPages", 0)));
                    }

                    @Override
                    public void onError(ApiError error) {
                        List<Slot> cached = referenceDataStore.readSlots(stationId);
                        if (isOffline(error) && !cached.isEmpty()) {
                            callback.onSuccess(new AvailableSlotPage(cached, 1, 1));
                        } else callback.onError(error);
                    }
                });
    }

    private void loadAvailableSlots(String stationId, ApiCallback<List<Slot>> callback) {
        String fromUtc = currentUtcQueryValue();
        apiClient.get(
                "stations/" + encode(stationId) + "/slots/available?fromUtc=" + fromUtc +
                        "&page=1&pageSize=100",
                new ApiCallback<>() {
                    @Override
                    public void onSuccess(JSONObject value) {
                        List<Slot> slots = parseSlots(value.optJSONArray("items"));
                        referenceDataStore.replaceSlots(stationId, slots);
                        callback.onSuccess(slots);
                    }

                    @Override
                    public void onError(ApiError error) {
                        List<Slot> cached = referenceDataStore.readSlots(stationId);
                        if (isOffline(error) && !cached.isEmpty()) callback.onSuccess(cached);
                        else callback.onError(error);
                    }
                });
    }

    private static String currentUtcQueryValue() {
        SimpleDateFormat utcFormat = new SimpleDateFormat(
                "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'",
                Locale.US);
        utcFormat.setTimeZone(TimeZone.getTimeZone("UTC"));
        return encode(utcFormat.format(new Date()));
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

    static List<NearbyStation> parseNearbyStations(JSONArray values) {
        List<NearbyStation> nearby = new ArrayList<>();
        if (values == null) {
            return nearby;
        }
        for (int index = 0; index < values.length(); index++) {
            JSONObject item = values.optJSONObject(index);
            JSONObject stationValue = item == null ? null : item.optJSONObject("station");
            if (stationValue == null || !stationValue.has("latitude") ||
                    !stationValue.has("longitude") || !stationValue.has("id")) {
                continue;
            }
            Station station = parseStation(stationValue);
            if (station.getId().isEmpty() || !Double.isFinite(station.getLatitude()) ||
                    !Double.isFinite(station.getLongitude()) ||
                    station.getLatitude() < -90 || station.getLatitude() > 90 ||
                    station.getLongitude() < -180 || station.getLongitude() > 180) {
                continue;
            }
            double distanceKm = item.optDouble("distanceKm", Double.NaN);
            if (!Double.isFinite(distanceKm) || distanceKm < 0) {
                continue;
            }
            nearby.add(new NearbyStation(station, distanceKm));
        }
        return nearby;
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

    private List<NearbyStation> cachedNearby(double latitude, double longitude) {
        // Recalculate distance from cached coordinates so offline ordering remains meaningful.
        List<NearbyStation> nearby = new ArrayList<>();
        for (Station station : referenceDataStore.readActiveStations()) {
            nearby.add(new NearbyStation(station, distanceKm(
                    latitude, longitude, station.getLatitude(), station.getLongitude())));
        }
        nearby.sort((first, second) -> Double.compare(first.getDistanceKm(), second.getDistanceKm()));
        return nearby.size() <= 50 ? nearby : new ArrayList<>(nearby.subList(0, 50));
    }

    private static boolean isOffline(ApiError error) {
        // Cache fallback is limited to transport/server failures, never authorization failures.
        return error.getKind() == ApiError.Kind.NETWORK || error.getKind() == ApiError.Kind.SERVER;
    }

    private static double distanceKm(double firstLatitude, double firstLongitude,
                                     double secondLatitude, double secondLongitude) {
        // Calculate the great-circle distance used to order cached nearby stations.
        double latitudeDelta = Math.toRadians(secondLatitude - firstLatitude);
        double longitudeDelta = Math.toRadians(secondLongitude - firstLongitude);
        double value = Math.sin(latitudeDelta / 2) * Math.sin(latitudeDelta / 2)
                + Math.cos(Math.toRadians(firstLatitude)) * Math.cos(Math.toRadians(secondLatitude))
                * Math.sin(longitudeDelta / 2) * Math.sin(longitudeDelta / 2);
        return 6371.0088 * 2 * Math.atan2(Math.sqrt(value), Math.sqrt(1 - value));
    }
}
