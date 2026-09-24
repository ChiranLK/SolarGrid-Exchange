package com.solargrid.exchange.ui.reservations;

import android.app.AlertDialog;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.activity.OnBackPressedCallback;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.NavOptions;
import androidx.navigation.Navigation;

import com.solargrid.exchange.R;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.data.model.ReservationAllowedActions;
import com.solargrid.exchange.data.model.ReservationStatusHistory;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;

public final class ReservationDetailFragment extends Fragment {
    private static final long REFRESH_INTERVAL_MS = 30_000L;
    private ReservationDetailViewModel viewModel;
    private Reservation displayedReservation;
    private ScrollView detailScroll;
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
        View view = inflater.inflate(R.layout.fragment_reservation_detail, container, false);
        View content = view.findViewById(R.id.reservation_detail_content);
        detailScroll = (ScrollView) content;
        UiStateView stateView = view.findViewById(R.id.reservation_detail_state);
        TextView refreshNotice = view.findViewById(R.id.reservation_detail_refresh_notice);
        TextView cancellationProgress = view.findViewById(R.id.reservation_cancellation_progress);
        Button update = view.findViewById(R.id.reservation_update_button);
        Button cancel = view.findViewById(R.id.reservation_cancel_button);
        viewModel = new ViewModelProvider(this).get(ReservationDetailViewModel.class);
        OnBackPressedCallback processingBackGuard = new OnBackPressedCallback(false) {
            @Override
            public void handleOnBackPressed() {
                Toast.makeText(
                        requireContext(),
                        R.string.reservation_mutation_wait,
                        Toast.LENGTH_SHORT).show();
            }
        };
        requireActivity().getOnBackPressedDispatcher().addCallback(
                getViewLifecycleOwner(), processingBackGuard);
        detailScroll.setOnScrollChangeListener((scrollView, scrollX, scrollY, oldScrollX, oldScrollY) ->
                viewModel.rememberScroll(scrollY));

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
                    detailScroll.post(() -> detailScroll.scrollTo(0, viewModel.getScrollY()));
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

        viewModel.getCancellationState().observe(getViewLifecycleOwner(), state -> {
            boolean busy = state.getStatus() == UiState.Status.LOADING;
            processingBackGuard.setEnabled(busy);
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
                        ReservationSummaryFragment.argumentsForCancellation(result),
                        options);
            }
        });

        viewModel.getCancellationPhase().observe(getViewLifecycleOwner(), phase -> {
            int message;
            switch (phase) {
                case SUBMITTING:
                    message = R.string.cancellation_progress_submitting;
                    break;
                case RECONCILING:
                    message = R.string.cancellation_progress_reconciling;
                    break;
                case REFRESHING:
                    message = R.string.cancellation_progress_refreshing;
                    break;
                default:
                    cancellationProgress.setVisibility(View.GONE);
                    return;
            }
            cancellationProgress.setText(message);
            cancellationProgress.setVisibility(View.VISIBLE);
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
        if (viewModel != null && detailScroll != null) {
            viewModel.rememberScroll(detailScroll.getScrollY());
        }
        super.onStop();
    }

    @Override
    public void onDestroyView() {
        detailScroll = null;
        super.onDestroyView();
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
        setText(view, R.id.reservation_detail_prosumer,
                getString(R.string.prosumer_identity_format,
                        valueOrFallback(reservation.getProsumerFullName(), getString(R.string.current_user)),
                        reservation.getProsumerNic()));
        setText(view, R.id.reservation_detail_slot, reservation.getSlotId());
        setText(view, R.id.reservation_detail_created,
                ReservationFormatters.localDateTime(reservation.getCreatedAtUtc()));
        setText(view, R.id.reservation_detail_updated,
                ReservationFormatters.localDateTime(reservation.getUpdatedAtUtc()));
        bindOptionalReason(view, R.id.reservation_cancellation_reason_group,
                R.id.reservation_detail_cancellation_reason, reservation.getCancellationReason());
        bindOptionalReason(view, R.id.reservation_rejection_reason_group,
                R.id.reservation_detail_rejection_reason, reservation.getRejectionReason());

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
        boolean mutationBusy = viewModel.isCancellationInProgress();
        update.setEnabled(!mutationBusy && reservation.getAllowedActions().canUpdate());
        cancel.setEnabled(!mutationBusy && reservation.getAllowedActions().canCancel());
        update.setContentDescription(reservation.getAllowedActions().canUpdate()
                ? getString(R.string.modify_reservation)
                : valueOrFallback(reservation.getAllowedActions().getUpdateUnavailableReason(),
                        getString(R.string.action_unavailable_fallback)));
        cancel.setContentDescription(reservation.getAllowedActions().canCancel()
                ? getString(R.string.cancel_reservation)
                : valueOrFallback(reservation.getAllowedActions().getCancelUnavailableReason(),
                        getString(R.string.action_unavailable_fallback)));
        setText(view, R.id.reservation_detail_allowed_actions,
                buildAllowedActions(reservation.getAllowedActions()));
    }

    private String buildAllowedActions(ReservationAllowedActions actions) {
        StringBuilder result = new StringBuilder();
        appendAction(result, getString(R.string.modify_reservation), actions.canUpdate(),
                actions.getUpdateUnavailableReason());
        appendAction(result, getString(R.string.cancel_reservation), actions.canCancel(),
                actions.getCancelUnavailableReason());
        appendAction(result, getString(R.string.qr_access), actions.canGetQr(),
                actions.getGetQrUnavailableReason());
        if (actions.canGetQr()) {
            result.append("\n").append(getString(R.string.qr_destination_dependency));
        }
        return result.toString();
    }

    private void appendAction(StringBuilder result, String label, boolean allowed, String reason) {
        if (result.length() > 0) {
            result.append("\n\n");
        }
        result.append(label).append(": ")
                .append(allowed ? getString(R.string.action_available) : getString(R.string.action_unavailable));
        if (!allowed) {
            result.append("\n").append(valueOrFallback(
                    reason, getString(R.string.action_unavailable_fallback)));
        }
    }

    private static void bindOptionalReason(View view, int groupId, int valueId, String value) {
        View group = view.findViewById(groupId);
        boolean visible = value != null && !value.trim().isEmpty();
        group.setVisibility(visible ? View.VISIBLE : View.GONE);
        if (visible) {
            setText(view, valueId, value);
        }
    }

    private static String valueOrFallback(String value, String fallback) {
        return value == null || value.trim().isEmpty() ? fallback : value;
    }

    private void showCancelDialog() {
        if (displayedReservation == null || !displayedReservation.getAllowedActions().canCancel()) {
            return;
        }
        View dialogView = LayoutInflater.from(requireContext()).inflate(
                R.layout.dialog_cancel_reservation, null, false);
        EditText reason = dialogView.findViewById(R.id.cancel_dialog_reason);
        setText(dialogView, R.id.cancel_dialog_reference, displayedReservation.getId());
        setText(dialogView, R.id.cancel_dialog_station,
                ReservationFormatters.station(
                        displayedReservation.getStationName(), displayedReservation.getStationId()));
        setText(dialogView, R.id.cancel_dialog_time,
                ReservationFormatters.localDateTime(displayedReservation.getScheduledStartTimeUtc()) + " - "
                        + ReservationFormatters.localDateTime(
                                displayedReservation.getScheduledEndTimeUtc()));
        new AlertDialog.Builder(requireContext())
                .setTitle(R.string.cancel_reservation)
                .setMessage(R.string.cancel_confirmation)
                .setView(dialogView)
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
