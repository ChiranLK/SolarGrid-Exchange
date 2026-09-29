package com.solargrid.exchange.ui.dashboard;

import android.app.DatePickerDialog;
import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.EditorInfo;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ListView;
import android.widget.Spinner;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.Navigation;

import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.DashboardReservation;
import com.solargrid.exchange.data.model.PagedBookingHistory;
import com.solargrid.exchange.data.model.Station;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;

import java.util.ArrayList;
import java.util.Calendar;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

public final class BookingHistoryFragment extends Fragment {
    private static final String[] STATUS_VALUES = {
            "", "Pending", "Approved", "Rejected", "Cancelled", "Completed"
    };

    private BookingHistoryViewModel viewModel;
    private DashboardReservationAdapter adapter;
    private final List<Station> stationChoices = new ArrayList<>();
    private String fromUtc = "";
    private String toUtc = "";

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_booking_history, container, false);
        EditText search = view.findViewById(R.id.history_search);
        Spinner status = view.findViewById(R.id.history_status_filter);
        Spinner station = view.findViewById(R.id.history_station_filter);
        Button from = view.findViewById(R.id.history_from_date);
        Button to = view.findViewById(R.id.history_to_date);
        ListView list = view.findViewById(R.id.history_list);
        UiStateView stateView = view.findViewById(R.id.history_state);
        TextView summary = view.findViewById(R.id.history_result_summary);
        View pagination = view.findViewById(R.id.history_pagination);
        Button previous = view.findViewById(R.id.history_previous);
        Button next = view.findViewById(R.id.history_next);

        SessionUser session = ((SolarGridApplication) requireActivity().getApplication())
                .getAppContainer()
                .getAuthRepository()
                .getStoredSession();
        if (session == null) {
            ((MainActivity) requireActivity()).handleAuthenticationExpiry();
            return view;
        }
        if (!session.isProsumer()) {
            stateView.showError(new ApiError(
                    ApiError.Kind.FORBIDDEN, 403, getString(R.string.prosumer_screen_only)), null);
            return view;
        }

        viewModel = new ViewModelProvider(this).get(BookingHistoryViewModel.class);
        adapter = new DashboardReservationAdapter(requireContext(), Collections.emptyList());
        list.setAdapter(adapter);
        list.setOnItemClickListener((parent, row, position, id) -> {
            DashboardReservation reservation = adapter.getItem(position);
            if (reservation != null) {
                Bundle arguments = new Bundle();
                arguments.putString("reservationId", reservation.getReservationId());
                Navigation.findNavController(view).navigate(R.id.nav_reservation_detail, arguments);
            }
        });

        configureStatusSpinner(status);
        search.setText(viewModel.getSearch());
        status.setSelection(indexOfStatus(viewModel.getStatus()), false);
        fromUtc = viewModel.getFromUtc();
        toUtc = viewModel.getToUtc();
        updateDateLabel(from, R.string.from_date, fromUtc);
        updateDateLabel(to, R.string.to_date, toUtc);

        from.setOnClickListener(ignored -> pickDate(true, from));
        to.setOnClickListener(ignored -> pickDate(false, to));
        View.OnClickListener apply = ignored -> viewModel.applyFilters(
                search.getText().toString(),
                STATUS_VALUES[status.getSelectedItemPosition()],
                selectedStationId(station),
                fromUtc,
                toUtc);
        view.findViewById(R.id.history_apply_filters).setOnClickListener(apply);
        search.setOnEditorActionListener((field, actionId, event) -> {
            if (actionId == EditorInfo.IME_ACTION_SEARCH) {
                apply.onClick(field);
                return true;
            }
            return false;
        });
        view.findViewById(R.id.history_clear_filters).setOnClickListener(ignored -> {
            search.setText("");
            status.setSelection(0);
            station.setSelection(0);
            fromUtc = "";
            toUtc = "";
            updateDateLabel(from, R.string.from_date, fromUtc);
            updateDateLabel(to, R.string.to_date, toUtc);
            viewModel.applyFilters("", "", "", "", "");
        });
        view.findViewById(R.id.history_refresh).setOnClickListener(ignored -> viewModel.refresh());
        previous.setOnClickListener(ignored -> viewModel.previousPage());
        next.setOnClickListener(ignored -> viewModel.nextPage());

        viewModel.getStations().observe(getViewLifecycleOwner(), values ->
                bindStations(station, values, viewModel.getStationId()));
        viewModel.getStationError().observe(getViewLifecycleOwner(), error -> {
            TextView notice = view.findViewById(R.id.history_station_notice);
            if (error == null) {
                notice.setVisibility(View.GONE);
            } else if (error.isAuthenticationExpired()) {
                ((MainActivity) requireActivity()).handleAuthenticationExpiry();
            } else {
                notice.setText(R.string.station_filter_unavailable);
                notice.setVisibility(View.VISIBLE);
            }
        });
        viewModel.getState().observe(getViewLifecycleOwner(), state -> {
            boolean success = state.getStatus() == UiState.Status.SUCCESS;
            list.setVisibility(success ? View.VISIBLE : View.GONE);
            summary.setVisibility(success ? View.VISIBLE : View.GONE);
            pagination.setVisibility(success ? View.VISIBLE : View.GONE);
            switch (state.getStatus()) {
                case LOADING:
                    stateView.showLoading(getString(R.string.loading_history));
                    break;
                case EMPTY:
                    stateView.showEmpty(
                            getString(R.string.no_history_results),
                            getString(R.string.no_history_filter_results),
                            ignored -> viewModel.refresh());
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
                    bindPage(state.getData(), summary, previous, next,
                            view.findViewById(R.id.history_page_label));
                    break;
                default:
                    break;
            }
        });
        viewModel.load();
        return view;
    }

    @Override
    public void onStart() {
        super.onStart();
        // Coming back from a reservation detail, cancel or update: show the server's current history.
        if (viewModel != null) {
            viewModel.refreshSilently();
        }
    }

    private void configureStatusSpinner(Spinner spinner) {
        String[] labels = {
                getString(R.string.all_statuses), "Pending", "Approved", "Rejected", "Cancelled", "Completed"
        };
        ArrayAdapter<String> values = new ArrayAdapter<>(
                requireContext(), android.R.layout.simple_spinner_item, labels);
        values.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        spinner.setAdapter(values);
    }

    private void bindStations(Spinner spinner, List<Station> stations, String selectedId) {
        stationChoices.clear();
        stationChoices.addAll(stations);
        List<String> labels = new ArrayList<>();
        labels.add(getString(R.string.all_stations));
        int selected = 0;
        for (int index = 0; index < stationChoices.size(); index++) {
            Station item = stationChoices.get(index);
            labels.add(item.getName());
            if (item.getId().equals(selectedId)) {
                selected = index + 1;
            }
        }
        ArrayAdapter<String> values = new ArrayAdapter<>(
                requireContext(), android.R.layout.simple_spinner_item, labels);
        values.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        spinner.setAdapter(values);
        spinner.setSelection(selected, false);
    }

    private String selectedStationId(Spinner spinner) {
        int index = spinner.getSelectedItemPosition() - 1;
        return index >= 0 && index < stationChoices.size() ? stationChoices.get(index).getId() : "";
    }

    private void pickDate(boolean start, Button button) {
        Calendar today = Calendar.getInstance();
        new DatePickerDialog(
                requireContext(),
                (picker, year, month, day) -> {
                    String date = String.format(Locale.US, "%04d-%02d-%02d", year, month + 1, day);
                    if (start) {
                        fromUtc = date + "T00:00:00.000Z";
                        updateDateLabel(button, R.string.from_date, fromUtc);
                    } else {
                        toUtc = date + "T23:59:59.999Z";
                        updateDateLabel(button, R.string.to_date, toUtc);
                    }
                },
                today.get(Calendar.YEAR),
                today.get(Calendar.MONTH),
                today.get(Calendar.DAY_OF_MONTH)).show();
    }

    private void updateDateLabel(Button button, int label, String value) {
        button.setText(value.isEmpty()
                ? getString(label)
                : getString(R.string.date_filter_value, getString(label), value.substring(0, 10)));
    }

    private void bindPage(
            PagedBookingHistory page,
            TextView summary,
            Button previous,
            Button next,
            TextView pageLabel) {
        if (page == null) {
            return;
        }
        adapter.replace(page.getItems());
        summary.setText(getResources().getQuantityString(
                R.plurals.history_result_count,
                (int) Math.min(Integer.MAX_VALUE, page.getTotalCount()),
                page.getTotalCount()));
        pageLabel.setText(getString(
                R.string.page_of_pages,
                page.getPage(),
                Math.max(page.getTotalPages(), 1)));
        previous.setEnabled(page.getPage() > 1);
        next.setEnabled(page.getPage() < page.getTotalPages());
    }

    private static int indexOfStatus(String value) {
        for (int index = 0; index < STATUS_VALUES.length; index++) {
            if (STATUS_VALUES[index].equals(value)) {
                return index;
            }
        }
        return 0;
    }
}
