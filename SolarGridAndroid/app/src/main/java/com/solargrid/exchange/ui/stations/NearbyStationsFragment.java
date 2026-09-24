package com.solargrid.exchange.ui.stations;

import android.content.Intent;
import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ListView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.fragment.NavHostFragment;

import com.solargrid.exchange.R;
import com.solargrid.exchange.data.model.Station;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.auth.LoginActivity;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;

public final class NearbyStationsFragment extends Fragment {
    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_station_list, container, false);
        ListView list = view.findViewById(R.id.station_list);
        UiStateView stateView = view.findViewById(R.id.station_list_state);
        StationListViewModel viewModel = new ViewModelProvider(this).get(StationListViewModel.class);

        viewModel.getState().observe(getViewLifecycleOwner(), state -> {
            list.setVisibility(state.getStatus() == UiState.Status.SUCCESS ? View.VISIBLE : View.GONE);
            switch (state.getStatus()) {
                case LOADING:
                    stateView.showLoading(getString(R.string.loading_stations));
                    break;
                case EMPTY:
                    stateView.showEmpty(getString(R.string.no_stations), getString(R.string.no_stations_message));
                    break;
                case ERROR:
                    if (state.getError() != null && state.getError().isAuthenticationExpired()) {
                        ((MainActivity) requireActivity()).handleAuthenticationExpiry();
                    } else if (state.getError() != null) {
                        stateView.showError(state.getError(), ignored -> viewModel.refresh());
                    }
                    break;
                case SUCCESS:
                    stateView.hide();
                    list.setAdapter(new StationAdapter(requireContext(), state.getData()));
                    break;
                default:
                    break;
            }
        });

        list.setOnItemClickListener((parent, row, position, id) -> {
            Station station = (Station) parent.getItemAtPosition(position);
            Bundle arguments = new Bundle();
            arguments.putString("stationId", station.getId());
            NavHostFragment.findNavController(this)
                    .navigate(R.id.nav_station_detail, arguments);
        });
        viewModel.load();
        return view;
    }
}
