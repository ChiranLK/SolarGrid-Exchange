package com.solargrid.exchange.ui.stations;

import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ListView;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.Navigation;

import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.data.model.Slot;
import com.solargrid.exchange.data.model.Station;
import com.solargrid.exchange.features.stations.StationDetailData;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;

import java.util.Locale;

public final class StationDetailFragment extends Fragment {
    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_station_detail, container, false);
        View content = view.findViewById(R.id.station_detail_content);
        UiStateView stateView = view.findViewById(R.id.station_detail_state);
        StationDetailViewModel viewModel = new ViewModelProvider(this).get(StationDetailViewModel.class);

        viewModel.getState().observe(getViewLifecycleOwner(), state -> {
            content.setVisibility(state.getStatus() == UiState.Status.SUCCESS ? View.VISIBLE : View.GONE);
            switch (state.getStatus()) {
                case LOADING:
                    stateView.showLoading(getString(R.string.loading_station_details));
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
                    bind(view, state.getData());
                    break;
                default:
                    break;
            }
        });

        String stationId = getArguments() == null ? "" : getArguments().getString("stationId", "");
        viewModel.load(stationId);
        return view;
    }

    private void bind(View view, StationDetailData data) {
        if (data == null) {
            return;
        }
        Station station = data.getStation();
        setText(view, R.id.station_detail_name, station.getName());
        setText(view, R.id.station_detail_address, station.getAddress());
        setText(view, R.id.station_detail_description,
                station.getDescription().isEmpty() ? getString(R.string.no_description) : station.getDescription());
        setText(view, R.id.station_detail_capacity, String.format(
                Locale.getDefault(),
                getString(R.string.station_capacity_format),
                station.getGenerationCapacityKw(),
                station.getStorageCapacityKwh()));
        TextView slotLabel = view.findViewById(R.id.station_slot_label);
        ListView slots = view.findViewById(R.id.station_slot_list);
        if (data.getAvailableSlots().isEmpty()) {
            slotLabel.setText(R.string.no_available_slots);
            slots.setVisibility(View.GONE);
        } else {
            slotLabel.setText(R.string.available_slots_live);
            slots.setVisibility(View.VISIBLE);
            SlotAdapter adapter = new SlotAdapter(requireContext(), data.getAvailableSlots());
            slots.setAdapter(adapter);
            SessionUser session = ((SolarGridApplication) requireActivity().getApplication())
                    .getAppContainer()
                    .getSessionStore()
                    .read();
            boolean canBook = session != null && session.isProsumer();
            slots.setOnItemClickListener(canBook ? (parent, item, position, id) -> {
                Slot slot = adapter.getItem(position);
                if (slot == null) {
                    return;
                }
                Bundle arguments = new Bundle();
                arguments.putString("mode", "create");
                arguments.putString("stationId", station.getId());
                arguments.putString("stationName", station.getName());
                arguments.putString("slotId", slot.getId());
                arguments.putString("startUtc", slot.getStartTimeUtc());
                arguments.putString("endUtc", slot.getEndTimeUtc());
                arguments.putDouble("availableCapacity", slot.getAvailableCapacityKwh());
                Navigation.findNavController(view).navigate(R.id.nav_reservation_form, arguments);
            } : null);
        }
    }

    private static void setText(View view, int id, String value) {
        ((TextView) view.findViewById(id)).setText(value);
    }
}
