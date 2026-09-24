package com.solargrid.exchange.ui.reservations;

import android.app.AlertDialog;
import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.TextView;
import android.widget.Toast;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.NavOptions;
import androidx.navigation.Navigation;

import com.solargrid.exchange.R;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.data.model.ReservationStatusHistory;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;

public final class ReservationDetailFragment extends Fragment {
    private ReservationDetailViewModel viewModel;
    private Reservation displayedReservation;

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_reservation_detail, container, false);
        View content = view.findViewById(R.id.reservation_detail_content);
        UiStateView stateView = view.findViewById(R.id.reservation_detail_state);
        Button update = view.findViewById(R.id.reservation_update_button);
        Button cancel = view.findViewById(R.id.reservation_cancel_button);
        viewModel = new ViewModelProvider(this).get(ReservationDetailViewModel.class);

        viewModel.getState().observe(getViewLifecycleOwner(), state -> {
            content.setVisibility(state.getStatus() == UiState.Status.SUCCESS ? View.VISIBLE : View.GONE);
            switch (state.getStatus()) {
                case LOADING:
                    stateView.showLoading(getString(R.string.loading_reservation));
                    break;
                case ERROR:
                    handleError(stateView, state.getError());
                    break;
                case SUCCESS:
                    stateView.hide();
                    displayedReservation = state.getData();
                    bind(view, displayedReservation);
                    break;
                default:
                    break;
            }
        });

        viewModel.getCancellationState().observe(getViewLifecycleOwner(), state -> {
            boolean busy = state.getStatus() == UiState.Status.LOADING;
            update.setEnabled(!busy && displayedReservation != null
                    && displayedReservation.getAllowedActions().canUpdate());
            cancel.setEnabled(!busy && displayedReservation != null
                    && displayedReservation.getAllowedActions().canCancel());
            if (state.getStatus() == UiState.Status.ERROR && state.getError() != null) {
                if (state.getError().isAuthenticationExpired()) {
                    ((MainActivity) requireActivity()).handleAuthenticationExpiry();
                } else {
                    Toast.makeText(requireContext(), state.getError().getMessage(), Toast.LENGTH_LONG).show();
                }
                viewModel.consumeCancellation();
            } else if (state.getStatus() == UiState.Status.SUCCESS && state.getData() != null) {
                Reservation result = state.getData();
                viewModel.consumeCancellation();
                NavOptions options = new NavOptions.Builder()
                        .setPopUpTo(R.id.nav_reservation_detail, true)
                        .build();
                Navigation.findNavController(view).navigate(
                        R.id.nav_reservation_summary,
                        ReservationSummaryFragment.argumentsFor("Reservation cancelled", result),
                        options);
            }
        });

        update.setOnClickListener(ignored -> {
            if (displayedReservation == null) {
                return;
            }
            Bundle arguments = new Bundle();
            arguments.putString("mode", "edit");
            arguments.putString("reservationId", displayedReservation.getId());
            Navigation.findNavController(view).navigate(R.id.nav_reservation_form, arguments);
        });
        cancel.setOnClickListener(ignored -> showCancelDialog());

        String reservationId = getArguments() == null
                ? ""
                : getArguments().getString("reservationId", "");
        viewModel.load(reservationId);
        return view;
    }

    private void bind(View view, Reservation reservation) {
        if (reservation == null) {
            return;
        }
        setText(view, R.id.reservation_detail_status, reservation.getStatus());
        setText(view, R.id.reservation_detail_station,
                ReservationFormatters.station(reservation.getStationName(), reservation.getStationId()));
        setText(view, R.id.reservation_detail_address, reservation.getStationAddress());
        setText(view, R.id.reservation_detail_time,
                ReservationFormatters.localDateTime(reservation.getScheduledStartTimeUtc()) + " - "
                        + ReservationFormatters.localDateTime(reservation.getScheduledEndTimeUtc()));
        setText(view, R.id.reservation_detail_energy,
                ReservationFormatters.energy(reservation.getRequestedEnergyKwh()));
        setText(view, R.id.reservation_detail_reference, reservation.getId());

        StringBuilder history = new StringBuilder();
        for (ReservationStatusHistory item : reservation.getStatusHistory()) {
            if (history.length() > 0) {
                history.append("\n\n");
            }
            history.append(item.getToStatus())
                    .append(" - ")
                    .append(ReservationFormatters.localDateTime(item.getChangedAtUtc()));
            if (item.getReason() != null && !item.getReason().isEmpty()) {
                history.append("\n").append(item.getReason());
            }
        }
        setText(view, R.id.reservation_detail_history,
                history.length() == 0 ? getString(R.string.no_status_history) : history.toString());

        Button update = view.findViewById(R.id.reservation_update_button);
        Button cancel = view.findViewById(R.id.reservation_cancel_button);
        update.setEnabled(reservation.getAllowedActions().canUpdate());
        cancel.setEnabled(reservation.getAllowedActions().canCancel());
        update.setContentDescription(reservation.getAllowedActions().canUpdate()
                ? getString(R.string.modify_reservation)
                : reservation.getAllowedActions().getUpdateUnavailableReason());
        cancel.setContentDescription(reservation.getAllowedActions().canCancel()
                ? getString(R.string.cancel_reservation)
                : reservation.getAllowedActions().getCancelUnavailableReason());
    }

    private void showCancelDialog() {
        if (displayedReservation == null || !displayedReservation.getAllowedActions().canCancel()) {
            return;
        }
        EditText reason = new EditText(requireContext());
        reason.setHint(R.string.cancellation_reason_hint);
        reason.setMaxLines(4);
        int padding = getResources().getDimensionPixelSize(R.dimen.space_lg);
        reason.setPadding(padding, padding / 2, padding, 0);
        new AlertDialog.Builder(requireContext())
                .setTitle(R.string.cancel_reservation)
                .setMessage(R.string.cancel_confirmation)
                .setView(reason)
                .setNegativeButton(android.R.string.cancel, null)
                .setPositiveButton(R.string.confirm_cancellation,
                        (dialog, which) -> viewModel.cancel(
                                displayedReservation, reason.getText().toString()))
                .show();
    }

    private void handleError(UiStateView stateView, com.solargrid.exchange.network.ApiError error) {
        if (error != null && error.isAuthenticationExpired()) {
            ((MainActivity) requireActivity()).handleAuthenticationExpiry();
        } else if (error != null) {
            stateView.showError(error, ignored -> viewModel.refresh());
        }
    }

    private static void setText(View view, int id, String value) {
        ((TextView) view.findViewById(id)).setText(
                value == null || value.isEmpty() ? "-" : value);
    }
}
