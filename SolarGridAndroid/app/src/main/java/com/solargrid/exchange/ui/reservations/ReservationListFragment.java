package com.solargrid.exchange.ui.reservations;

import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.AbsListView;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.ListView;
import android.widget.Spinner;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.Navigation;

import com.solargrid.exchange.R;
import com.solargrid.exchange.data.model.PagedReservations;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;

import java.util.Collections;

public final class ReservationListFragment extends Fragment {
    private static final long REFRESH_INTERVAL_MS = 30_000L;
    private static final String[] ACTIVE_VIEWS = {"Pending", "ApprovedFuture", "Current", "All"};
    private ReservationListViewModel viewModel;
    private final Handler refreshHandler = new Handler(Looper.getMainLooper());
    private final Runnable scheduledRefresh = new Runnable() {
        @Override
        public void run() {
            if (viewModel != null) {
                viewModel.refreshSilently();
                refreshHandler.postDelayed(this, REFRESH_INTERVAL_MS);
            }
        }
    };

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_reservation_list, container, false);
        UiStateView stateView = view.findViewById(R.id.reservation_list_state);
        ListView list = view.findViewById(R.id.reservation_list);
        Spinner filter = view.findViewById(R.id.reservation_view_filter);
        TextView summary = view.findViewById(R.id.reservation_list_summary);
        TextView refreshNotice = view.findViewById(R.id.reservation_refresh_notice);
        viewModel = new ViewModelProvider(this).get(ReservationListViewModel.class);
        ReservationAdapter reservationAdapter = new ReservationAdapter(
                requireContext(), Collections.emptyList());
        list.setAdapter(reservationAdapter);
        list.setOnItemClickListener((parent, item, position, id) -> {
            Reservation reservation = reservationAdapter.getItem(position);
            if (reservation == null) {
                return;
            }
            Bundle arguments = new Bundle();
            arguments.putString("reservationId", reservation.getId());
            Navigation.findNavController(list).navigate(R.id.nav_reservation_detail, arguments);
        });
        list.setOnScrollListener(new AbsListView.OnScrollListener() {
            @Override
            public void onScrollStateChanged(AbsListView view, int scrollState) { }

            @Override
            public void onScroll(AbsListView view, int firstVisibleItem,
                                 int visibleItemCount, int totalItemCount) {
                View first = view.getChildAt(0);
                if (totalItemCount > 0 && first != null) {
                    viewModel.rememberScroll(firstVisibleItem, first.getTop());
                }
            }
        });

        String defaultView = getArguments() == null
                ? "Pending"
                : getArguments().getString("defaultView", "Pending");
        boolean history = "History".equals(defaultView);
        filter.setVisibility(history ? View.GONE : View.VISIBLE);
        if (!history) {
            String[] labels = {
                    getString(R.string.filter_pending),
                    getString(R.string.filter_approved_upcoming),
                    getString(R.string.filter_in_progress),
                    getString(R.string.filter_all)
            };
            ArrayAdapter<String> filterAdapter = new ArrayAdapter<>(
                    requireContext(), android.R.layout.simple_spinner_item, labels);
            filterAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
            filter.setAdapter(filterAdapter);
            String initialView = viewModel.getSelectedView() == null
                    ? defaultView
                    : viewModel.getSelectedView();
            filter.setSelection(indexOfView(initialView), false);
            filter.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
                @Override
                public void onItemSelected(AdapterView<?> parent, View selected, int position, long id) {
                    viewModel.load(ACTIVE_VIEWS[position]);
                }

                @Override
                public void onNothingSelected(AdapterView<?> parent) { }
            });
            viewModel.load(initialView);
        }

        viewModel.getState().observe(getViewLifecycleOwner(), state -> {
            list.setVisibility(state.getStatus() == UiState.Status.SUCCESS
                    ? View.VISIBLE : View.GONE);
            summary.setVisibility(state.getStatus() == UiState.Status.SUCCESS
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
                    bindList(list, reservationAdapter, summary, state.getData());
                    break;
                default:
                    break;
            }
        });

        viewModel.getRefreshError().observe(getViewLifecycleOwner(), error -> {
            if (error == null) {
                refreshNotice.setVisibility(View.GONE);
            } else if (error.isAuthenticationExpired()) {
                ((MainActivity) requireActivity()).handleAuthenticationExpiry();
            } else {
                refreshNotice.setText(getString(R.string.reservation_refresh_failed, error.getMessage()));
                refreshNotice.setVisibility(View.VISIBLE);
            }
        });

        if (history) {
            viewModel.load("History");
        }
        return view;
    }

    @Override
    public void onStart() {
        super.onStart();
        if (viewModel != null && viewModel.getState().getValue() != null) {
            UiState.Status status = viewModel.getState().getValue().getStatus();
            if (status != UiState.Status.IDLE && status != UiState.Status.LOADING) {
                viewModel.refreshSilently();
            }
        }
        refreshHandler.postDelayed(scheduledRefresh, REFRESH_INTERVAL_MS);
    }

    @Override
    public void onStop() {
        refreshHandler.removeCallbacks(scheduledRefresh);
        super.onStop();
    }

    private void bindList(ListView list, ReservationAdapter adapter, TextView summary,
                          PagedReservations page) {
        if (page == null) {
            return;
        }
        boolean restoreScroll = viewModel.hasSavedScroll();
        int savedPosition = viewModel.getFirstVisiblePosition();
        int savedTop = viewModel.getFirstVisibleTop();
        adapter.replace(page.getItems());
        summary.setText(getResources().getQuantityString(
                R.plurals.reservation_result_count,
                (int) Math.min(Integer.MAX_VALUE, page.getTotalCount()),
                page.getTotalCount()));
        if (restoreScroll) {
            list.post(() -> list.setSelectionFromTop(
                    Math.min(savedPosition, Math.max(0, adapter.getCount() - 1)),
                    savedTop));
        } else {
            list.setSelection(0);
        }
    }

    private static int indexOfView(String view) {
        for (int index = 0; index < ACTIVE_VIEWS.length; index++) {
            if (ACTIVE_VIEWS[index].equals(view)) {
                return index;
            }
        }
        return 0;
    }
}
