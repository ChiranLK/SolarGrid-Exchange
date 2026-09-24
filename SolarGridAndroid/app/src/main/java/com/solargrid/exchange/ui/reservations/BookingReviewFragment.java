package com.solargrid.exchange.ui.reservations;

import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.TextView;
import android.widget.Toast;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.NavOptions;
import androidx.navigation.Navigation;

import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;

public final class BookingReviewFragment extends Fragment {
    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_booking_review, container, false);
        Bundle arguments = getArguments() == null ? Bundle.EMPTY : getArguments();
        BookingReviewViewModel viewModel =
                new ViewModelProvider(this).get(BookingReviewViewModel.class);
        Button edit = view.findViewById(R.id.booking_review_edit_button);
        Button confirm = view.findViewById(R.id.booking_review_confirm_button);

        SessionUser session = ((SolarGridApplication) requireActivity().getApplication())
                .getAppContainer()
                .getSessionStore()
                .read();
        if (session == null) {
            ((MainActivity) requireActivity()).handleAuthenticationExpiry();
            return view;
        }
        if (!session.isProsumer()) {
            Navigation.findNavController(view).popBackStack();
            return view;
        }

        setText(view, R.id.booking_review_prosumer,
                getString(R.string.prosumer_identity_format, session.getFullName(), session.getNic()));
        setText(view, R.id.booking_review_station, arguments.getString("stationName", ""));
        setText(view, R.id.booking_review_slot, arguments.getString("slotId", ""));
        setText(view, R.id.booking_review_date,
                ReservationFormatters.localDate(arguments.getString("startUtc", "")));
        setText(view, R.id.booking_review_time,
                ReservationFormatters.localTimeRange(
                        arguments.getString("startUtc", ""),
                        arguments.getString("endUtc", "")));
        setText(view, R.id.booking_review_capacity,
                ReservationFormatters.energy(arguments.getDouble("availableCapacity", 0)));
        setText(view, R.id.booking_review_quantity,
                ReservationFormatters.energy(arguments.getDouble("requestedEnergy", 0)));
        setText(view, R.id.booking_review_status, getString(R.string.pending_status));

        edit.setOnClickListener(ignored -> Navigation.findNavController(view).popBackStack());
        confirm.setOnClickListener(ignored -> viewModel.submit(
                arguments.getString("slotId", ""),
                arguments.getDouble("requestedEnergy", 0)));

        viewModel.getState().observe(getViewLifecycleOwner(), state -> {
            boolean loading = state.getStatus() == UiState.Status.LOADING;
            edit.setEnabled(!loading);
            confirm.setEnabled(!loading);
            confirm.setText(loading ? R.string.saving_reservation : R.string.confirm_booking);
            if (state.getStatus() == UiState.Status.ERROR && state.getError() != null) {
                if (state.getError().isAuthenticationExpired()) {
                    ((MainActivity) requireActivity()).handleAuthenticationExpiry();
                } else {
                    Toast.makeText(requireContext(), state.getError().getMessage(), Toast.LENGTH_LONG).show();
                }
                viewModel.consumeResult();
            } else if (state.getStatus() == UiState.Status.SUCCESS && state.getData() != null) {
                Reservation result = state.getData();
                viewModel.consumeResult();
                NavOptions options = new NavOptions.Builder()
                        .setPopUpTo(R.id.nav_reservation_form, true)
                        .build();
                Navigation.findNavController(view).navigate(
                        R.id.nav_reservation_summary,
                        ReservationSummaryFragment.argumentsFor("Booking request created", result),
                        options);
            }
        });
        return view;
    }

    private static void setText(View view, int id, String value) {
        ((TextView) view.findViewById(id)).setText(value);
    }
}
