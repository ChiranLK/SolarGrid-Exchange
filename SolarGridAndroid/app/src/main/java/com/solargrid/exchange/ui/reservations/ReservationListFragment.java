package com.solargrid.exchange.ui.reservations;

import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.ListView;
import android.widget.Spinner;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.Navigation;

import com.solargrid.exchange.R;
import com.solargrid.exchange.data.model.PagedReservations;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiStateView;

public final class ReservationListFragment extends Fragment {
    private static final String[] ACTIVE_VIEWS = {"All", "Pending", "Current", "ApprovedFuture"};
    private ReservationListViewModel viewModel;

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_reservation_list, container, false);
        UiStateView stateView = view.findViewById(R.id.reservation_list_state);
        ListView list = view.findViewById(R.id.reservation_list);
        Spinner filter = view.findViewById(R.id.reservation_view_filter);
        viewModel = new ViewModelProvider(this).get(ReservationListViewModel.class);

        String defaultView = getArguments() == null
                ? "All"
                : getArguments().getString("defaultView", "All");
        boolean history = "History".equals(defaultView);
        filter.setVisibility(history ? View.GONE : View.VISIBLE);
        if (!history) {
            ArrayAdapter<String> filterAdapter = new ArrayAdapter<>(
                    requireContext(), android.R.layout.simple_spinner_item, ACTIVE_VIEWS);
            filterAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
            filter.setAdapter(filterAdapter);
            filter.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
                @Override
                public void onItemSelected(AdapterView<?> parent, View selected, int position, long id) {
                    viewModel.load(ACTIVE_VIEWS[position]);
                }

                @Override
                public void onNothingSelected(AdapterView<?> parent) { }
            });
        }

        viewModel.getState().observe(getViewLifecycleOwner(), state -> {
            list.setVisibility(state.getStatus() == com.solargrid.exchange.ui.common.UiState.Status.SUCCESS
                    ? View.VISIBLE : View.GONE);
            switch (state.getStatus()) {
                case LOADING:
                    stateView.showLoading(getString(R.string.loading_reservations));
                    break;
                case EMPTY:
                    stateView.showEmpty(
                            getString(R.string.no_reservations),
                            getString(history ? R.string.no_history_message : R.string.no_reservations_message));
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
                    bindList(list, state.getData());
                    break;
                default:
                    break;
            }
        });

        if (history) {
            viewModel.load("History");
        }
        return view;
    }

    @Override
    public void onResume() {
        super.onResume();
        if (viewModel != null && viewModel.getState().getValue() != null
                && viewModel.getState().getValue().getStatus()
                == com.solargrid.exchange.ui.common.UiState.Status.SUCCESS) {
            viewModel.refresh();
        }
    }

    private void bindList(ListView list, PagedReservations page) {
        if (page == null) {
            return;
        }
        ReservationAdapter adapter = new ReservationAdapter(requireContext(), page.getItems());
        list.setAdapter(adapter);
        list.setOnItemClickListener((parent, item, position, id) -> {
            Reservation reservation = adapter.getItem(position);
            if (reservation == null) {
                return;
            }
            Bundle arguments = new Bundle();
            arguments.putString("reservationId", reservation.getId());
            Navigation.findNavController(list).navigate(R.id.nav_reservation_detail, arguments);
        });
    }
}
