package com.solargrid.exchange.ui.stations;

import android.Manifest;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.location.Location;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ListView;

import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.core.content.ContextCompat;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.fragment.NavHostFragment;

import com.google.android.gms.location.FusedLocationProviderClient;
import com.google.android.gms.location.LocationCallback;
import com.google.android.gms.location.LocationRequest;
import com.google.android.gms.location.LocationResult;
import com.google.android.gms.location.LocationServices;
import com.google.android.gms.location.Priority;
import com.google.android.gms.maps.CameraUpdateFactory;
import com.google.android.gms.maps.GoogleMap;
import com.google.android.gms.maps.SupportMapFragment;
import com.google.android.gms.maps.model.BitmapDescriptorFactory;
import com.google.android.gms.maps.model.LatLng;
import com.google.android.gms.maps.model.LatLngBounds;
import com.google.android.gms.maps.model.Marker;
import com.google.android.gms.maps.model.MarkerOptions;
import com.google.android.gms.tasks.CancellationTokenSource;
import com.solargrid.exchange.BuildConfig;
import com.solargrid.exchange.R;
import com.solargrid.exchange.data.model.NearbyStation;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;

import java.util.Collections;
import java.util.List;

public final class NearbyStationsFragment extends Fragment {
    private final ActivityResultLauncher<String[]> permissionRequest = registerForActivityResult(
            new ActivityResultContracts.RequestMultiplePermissions(), result -> {
                if (hasLocationPermission()) {
                    findLocation();
                    startLiveUpdates();
                } else {
                    showPermissionDenied();
                }
            });

    private StationListViewModel viewModel;
    private UiStateView stateView;
    private ListView list;
    private GoogleMap map;
    private FusedLocationProviderClient locationClient;
    private CancellationTokenSource locationCancellation;
    private List<NearbyStation> displayedStations = Collections.emptyList();
    private LatLng lastDeviceLocation;
    private Location lastLoadedLocation;
    private String fittedSignature;
    private boolean liveUpdatesActive;
    private boolean returningFromSettings;
    private final Handler refreshHandler = new Handler(Looper.getMainLooper());
    private final Runnable periodicRefresh = new Runnable() {
        @Override
        public void run() {
            if (getView() != null && lastLoadedLocation != null) {
                viewModel.refreshNearby(
                        lastLoadedLocation.getLatitude(), lastLoadedLocation.getLongitude());
            }
            refreshHandler.postDelayed(this, PERIODIC_REFRESH_MS);
        }
    };
    private final LocationCallback liveLocationCallback = new LocationCallback() {
        @Override
        public void onLocationResult(@NonNull LocationResult result) {
            Location location = result.getLastLocation();
            if (location == null || getView() == null) return;
            lastDeviceLocation = new LatLng(location.getLatitude(), location.getLongitude());
            boolean movedFarEnough = lastLoadedLocation == null
                    || location.distanceTo(lastLoadedLocation) >= REFRESH_DISTANCE_METERS;
            if (movedFarEnough) {
                lastLoadedLocation = location;
                viewModel.refreshNearby(location.getLatitude(), location.getLongitude());
            }
        }
    };

    private static final float REFRESH_DISTANCE_METERS = 100f;
    private static final long LIVE_LOCATION_INTERVAL_MS = 10_000L;
    private static final long PERIODIC_REFRESH_MS = 60_000L;

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        return inflater.inflate(R.layout.fragment_station_list, container, false);
    }

    @Override
    public void onViewCreated(@NonNull View view, @Nullable Bundle savedInstanceState) {
        super.onViewCreated(view, savedInstanceState);
        list = view.findViewById(R.id.station_list);
        stateView = view.findViewById(R.id.station_list_state);
        locationClient = LocationServices.getFusedLocationProviderClient(requireActivity());
        viewModel = new ViewModelProvider(this).get(StationListViewModel.class);
        view.findViewById(R.id.station_location_refresh).setOnClickListener(ignored -> requestLocation());
        list.setOnItemClickListener((parent, row, position, id) ->
                openStation(((NearbyStation) parent.getItemAtPosition(position)).getStation().getId()));

        view.findViewById(R.id.station_map_container).setVisibility(
                BuildConfig.MAPS_API_KEY_CONFIGURED ? View.VISIBLE : View.GONE);
        view.findViewById(R.id.station_map_unavailable).setVisibility(
                BuildConfig.MAPS_API_KEY_CONFIGURED ? View.GONE : View.VISIBLE);
        if (BuildConfig.MAPS_API_KEY_CONFIGURED) {
            SupportMapFragment mapFragment = (SupportMapFragment) getChildFragmentManager()
                    .findFragmentById(R.id.station_map_container);
            if (mapFragment == null) {
                mapFragment = SupportMapFragment.newInstance();
                getChildFragmentManager().beginTransaction()
                        .replace(R.id.station_map_container, mapFragment).commitNow();
            }
            mapFragment.getMapAsync(googleMap -> {
                if (getView() == null) return;
                map = googleMap;
                map.getUiSettings().setZoomControlsEnabled(true);
                map.getUiSettings().setMapToolbarEnabled(false);
                enableMyLocationLayer();
                map.setOnInfoWindowClickListener(marker -> {
                    Object stationId = marker.getTag();
                    if (stationId instanceof String) openStation((String) stationId);
                });
                renderMarkers();
            });
        }

        viewModel.getState().observe(getViewLifecycleOwner(), state -> {
            list.setVisibility(state.getStatus() == UiState.Status.SUCCESS ? View.VISIBLE : View.GONE);
            switch (state.getStatus()) {
                case LOADING:
                    stateView.showLoading(getString(R.string.loading_stations));
                    break;
                case EMPTY:
                    displayedStations = Collections.emptyList();
                    renderMarkers();
                    stateView.showEmpty(getString(R.string.no_nearby_stations),
                            getString(R.string.no_nearby_stations_message), ignored -> requestLocation());
                    break;
                case ERROR:
                    if (state.getError() != null && state.getError().isAuthenticationExpired()) {
                        ((MainActivity) requireActivity()).handleAuthenticationExpiry();
                    } else if (state.getError() != null) {
                        stateView.showError(state.getError(), ignored -> requestLocation());
                    }
                    break;
                case SUCCESS:
                    stateView.hide();
                    displayedStations = state.getData() == null
                            ? Collections.emptyList() : state.getData();
                    list.setAdapter(new StationAdapter(requireContext(), displayedStations));
                    renderMarkers();
                    break;
                default:
                    break;
            }
        });
        requestLocation();
    }

    @Override
    public void onResume() {
        super.onResume();
        if (returningFromSettings && getView() != null) {
            returningFromSettings = false;
            requestLocation();
        }
        startLiveUpdates();
    }

    @Override
    public void onPause() {
        stopLiveUpdates();
        super.onPause();
    }

    @Override
    public void onDestroyView() {
        stopLiveUpdates();
        if (locationCancellation != null) locationCancellation.cancel();
        fittedSignature = null;
        map = null;
        stateView = null;
        list = null;
        super.onDestroyView();
    }

    private void requestLocation() {
        if (!hasLocationPermission()) {
            permissionRequest.launch(new String[]{
                    Manifest.permission.ACCESS_FINE_LOCATION,
                    Manifest.permission.ACCESS_COARSE_LOCATION});
            return;
        }
        findLocation();
    }

    private boolean hasLocationPermission() {
        return ContextCompat.checkSelfPermission(requireContext(), Manifest.permission.ACCESS_FINE_LOCATION)
                == PackageManager.PERMISSION_GRANTED ||
                ContextCompat.checkSelfPermission(requireContext(), Manifest.permission.ACCESS_COARSE_LOCATION)
                        == PackageManager.PERMISSION_GRANTED;
    }

    private void findLocation() {
        if (stateView == null) return;
        list.setVisibility(View.GONE);
        displayedStations = Collections.emptyList();
        renderMarkers();
        stateView.showLoading(getString(R.string.finding_device_location));
        if (locationCancellation != null) locationCancellation.cancel();
        locationCancellation = new CancellationTokenSource();
        try {
            locationClient.getCurrentLocation(Priority.PRIORITY_BALANCED_POWER_ACCURACY,
                            locationCancellation.getToken())
                    .addOnSuccessListener(location -> {
                        if (getView() == null) return;
                        if (location != null) loadNearby(location);
                        else tryLastLocation();
                    })
                    .addOnFailureListener(ignored -> { if (getView() != null) tryLastLocation(); });
        } catch (SecurityException exception) {
            showPermissionDenied();
        }
    }

    private void tryLastLocation() {
        try {
            locationClient.getLastLocation()
                    .addOnSuccessListener(location -> {
                        if (getView() == null) return;
                        if (location == null) showLocationUnavailable();
                        else loadNearby(location);
                    })
                    .addOnFailureListener(ignored -> {
                        if (getView() != null) showLocationUnavailable();
                    });
        } catch (SecurityException exception) {
            showPermissionDenied();
        }
    }

    private void showLocationUnavailable() {
        displayedStations = Collections.emptyList();
        renderMarkers();
        stateView.showEmpty(getString(R.string.location_unavailable),
                getString(R.string.location_unavailable_message), ignored -> requestLocation());
    }

    private void startLiveUpdates() {
        if (liveUpdatesActive || locationClient == null || !hasLocationPermission()) return;
        LocationRequest request = new LocationRequest.Builder(
                Priority.PRIORITY_BALANCED_POWER_ACCURACY, LIVE_LOCATION_INTERVAL_MS)
                .setMinUpdateDistanceMeters(50f)
                .build();
        try {
            locationClient.requestLocationUpdates(request, liveLocationCallback, Looper.getMainLooper());
            liveUpdatesActive = true;
            refreshHandler.postDelayed(periodicRefresh, PERIODIC_REFRESH_MS);
        } catch (SecurityException ignored) {
            // Permission was revoked; the one-off lookup flow reports it to the user.
        }
    }

    private void stopLiveUpdates() {
        refreshHandler.removeCallbacks(periodicRefresh);
        if (!liveUpdatesActive || locationClient == null) return;
        locationClient.removeLocationUpdates(liveLocationCallback);
        liveUpdatesActive = false;
    }

    private void loadNearby(Location location) {
        lastLoadedLocation = location;
        lastDeviceLocation = new LatLng(location.getLatitude(), location.getLongitude());
        enableMyLocationLayer();
        if (map != null) map.moveCamera(CameraUpdateFactory.newLatLngZoom(
                lastDeviceLocation, 12f));
        viewModel.loadNearby(location.getLatitude(), location.getLongitude());
    }

    private void showPermissionDenied() {
        if (stateView == null) return;
        list.setVisibility(View.GONE);
        displayedStations = Collections.emptyList();
        lastDeviceLocation = null;
        renderMarkers();
        stateView.showEmpty(getString(R.string.location_permission_required),
                getString(R.string.location_permission_message),
                getString(R.string.open_app_settings), ignored -> {
                    returningFromSettings = true;
                    startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                            Uri.fromParts("package", requireContext().getPackageName(), null)));
                });
    }

    private void enableMyLocationLayer() {
        if (map == null || !hasLocationPermission()) return;
        try {
            map.setMyLocationEnabled(true);
            map.getUiSettings().setMyLocationButtonEnabled(true);
        } catch (SecurityException ignored) {
            // Permission was revoked between the check and the call; the map still shows stations.
        }
    }

    private void renderMarkers() {
        if (map == null) return;
        map.clear();
        StringBuilder signature = new StringBuilder();
        for (NearbyStation nearby : displayedStations) {
            signature.append(nearby.getStation().getId()).append(',');
        }
        if (lastDeviceLocation != null) {
            signature.append(Math.round(lastDeviceLocation.latitude * 1000)).append(':')
                    .append(Math.round(lastDeviceLocation.longitude * 1000));
        }
        boolean refitCamera = !signature.toString().equals(fittedSignature);
        fittedSignature = signature.toString();
        if (displayedStations.isEmpty()) {
            if (refitCamera && lastDeviceLocation != null) {
                map.moveCamera(CameraUpdateFactory.newLatLngZoom(lastDeviceLocation, 12f));
            }
            return;
        }
        LatLngBounds.Builder bounds = new LatLngBounds.Builder();
        if (lastDeviceLocation != null) bounds.include(lastDeviceLocation);
        for (NearbyStation nearby : displayedStations) {
            LatLng position = new LatLng(nearby.getStation().getLatitude(),
                    nearby.getStation().getLongitude());
            bounds.include(position);
            Marker marker = map.addMarker(new MarkerOptions()
                    .position(position)
                    .title(nearby.getStation().getName())
                    .snippet(StationAdapter.formatDistance(requireContext(), nearby.getDistanceKm()))
                    .icon(BitmapDescriptorFactory.defaultMarker(BitmapDescriptorFactory.HUE_GREEN)));
            if (marker != null) marker.setTag(nearby.getStation().getId());
        }
        if (!refitCamera) return;
        View mapView = getView() == null ? null : getView().findViewById(R.id.station_map_container);
        int padding = (int) (48 * getResources().getDisplayMetrics().density);
        if (mapView != null && mapView.getWidth() > 0 && mapView.getHeight() > 0) {
            LatLngBounds area = bounds.build();
            boolean tinyArea = Math.abs(area.northeast.latitude - area.southwest.latitude) < 0.003
                    && Math.abs(area.northeast.longitude - area.southwest.longitude) < 0.003;
            map.animateCamera(tinyArea
                    ? CameraUpdateFactory.newLatLngZoom(area.getCenter(), 15f)
                    : CameraUpdateFactory.newLatLngBounds(
                            area, mapView.getWidth(), mapView.getHeight(), padding));
        } else if (lastDeviceLocation != null) {
            map.animateCamera(CameraUpdateFactory.newLatLngZoom(lastDeviceLocation, 12f));
        }
    }

    private void openStation(String stationId) {
        Bundle arguments = new Bundle();
        arguments.putString("stationId", stationId);
        NavHostFragment.findNavController(this).navigate(R.id.nav_station_detail, arguments);
    }
}
