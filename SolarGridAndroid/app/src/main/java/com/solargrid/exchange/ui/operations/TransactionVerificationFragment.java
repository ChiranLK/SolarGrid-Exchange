package com.solargrid.exchange.ui.operations;

import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.TextView;

import androidx.activity.OnBackPressedCallback;
import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.appcompat.app.AlertDialog;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.NavController;
import androidx.navigation.Navigation;

import com.solargrid.exchange.R;
import com.solargrid.exchange.ui.common.StatusStyles;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.data.model.VerifiedTransaction;
import com.solargrid.exchange.features.operations.TransactionErrorMapper;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;
import com.solargrid.exchange.ui.reservations.ReservationFormatters;

public final class TransactionVerificationFragment extends Fragment {
    private OperatorTransactionViewModel viewModel;
    private VerifiedTransaction displayed;

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_transaction_verification, container, false);
        View content = view.findViewById(R.id.transaction_verification_content);
        UiStateView stateView = view.findViewById(R.id.transaction_verification_state);
        Button complete = view.findViewById(R.id.verification_complete);
        Button retry = view.findViewById(R.id.verification_retry_completion);
        TextView progress = view.findViewById(R.id.verification_completion_progress);
        TextView error = view.findViewById(R.id.verification_completion_error);

        SessionUser session = ((SolarGridApplication) requireActivity().getApplication())
                .getAppContainer()
                .getAuthRepository()
                .getStoredSession();
        if (session == null) {
            ((MainActivity) requireActivity()).handleAuthenticationExpiry();
            return view;
        }
        if (!session.isGridOperator()) {
            stateView.showError(new com.solargrid.exchange.network.ApiError(
                    com.solargrid.exchange.network.ApiError.Kind.FORBIDDEN,
                    403,
                    getString(R.string.operator_only_screen)), null);
            return view;
        }

        viewModel = new ViewModelProvider(requireActivity())
                .get(OperatorTransactionViewModel.class);
        complete.setOnClickListener(ignored -> showCompletionConfirmation());
        retry.setOnClickListener(ignored -> showCompletionConfirmation());
        view.findViewById(R.id.verification_scan_again)
                .setOnClickListener(ignored -> returnToScanner(view));
        requireActivity().getOnBackPressedDispatcher().addCallback(
                getViewLifecycleOwner(),
                new OnBackPressedCallback(true) {
                    @Override
                    public void handleOnBackPressed() {
                        returnToScanner(view);
                    }
                });

        viewModel.getVerificationState().observe(getViewLifecycleOwner(), state -> {
            boolean success = state.getStatus() == UiState.Status.SUCCESS && state.getData() != null;
            content.setVisibility(success ? View.VISIBLE : View.GONE);
            if (success) {
                stateView.hide();
                displayed = state.getData();
                bindVerification(view, displayed);
            } else if (state.getStatus() == UiState.Status.ERROR && state.getError() != null) {
                displayed = null;
                if (state.getError().isAuthenticationExpired()) {
                    ((MainActivity) requireActivity()).handleAuthenticationExpiry();
                } else {
                    stateView.showError(state.getError(), ignored -> returnToScanner(view));
                }
            } else if (state.getStatus() != UiState.Status.LOADING) {
                displayed = null;
                stateView.showEmpty(
                        getString(R.string.verification_not_available),
                        getString(R.string.verification_restart_message),
                        ignored -> returnToScanner(view));
            } else {
                stateView.showLoading(getString(R.string.verifying_qr));
            }
        });

        viewModel.getCompletionState().observe(getViewLifecycleOwner(), state -> {
            boolean busy = state.getStatus() == UiState.Status.LOADING;
            complete.setEnabled(!busy && displayed != null);
            progress.setVisibility(busy ? View.VISIBLE : View.GONE);
            if (state.getStatus() == UiState.Status.ERROR && state.getError() != null) {
                if (state.getError().isAuthenticationExpired()) {
                    ((MainActivity) requireActivity()).handleAuthenticationExpiry();
                    return;
                }
                TransactionErrorMapper.Presentation presentation =
                        TransactionErrorMapper.map(state.getError());
                error.setText(presentation.getMessage());
                error.setVisibility(View.VISIBLE);
                retry.setVisibility(presentation.isRetryable() ? View.VISIBLE : View.GONE);
            } else {
                error.setVisibility(View.GONE);
                retry.setVisibility(View.GONE);
            }
            if (state.getStatus() == UiState.Status.SUCCESS) {
                navigateToCompletion(view);
            }
        });
        return view;
    }

    private void bindVerification(View view, VerifiedTransaction value) {
        ((TextView) view.findViewById(R.id.verification_status)).setText(value.getStatus());
        StatusStyles.apply(view.findViewById(R.id.verification_status), value.getStatus());
        ((TextView) view.findViewById(R.id.verification_summary)).setText(getString(
                R.string.verification_summary,
                value.getReservationReference(),
                value.getProsumerReference(),
                ReservationFormatters.station(value.getStationName(), value.getStationId()),
                ReservationFormatters.localDateTime(value.getScheduledStartTimeUtc()),
                ReservationFormatters.localDateTime(value.getScheduledEndTimeUtc()),
                ReservationFormatters.energy(value.getRequestedEnergyKwh())));
        ((TextView) view.findViewById(R.id.verification_expiry)).setText(getString(
                R.string.verification_expires,
                ReservationFormatters.localDateTime(value.getExpiresAtUtc())));
    }

    private void showCompletionConfirmation() {
        if (displayed == null) {
            return;
        }
        new AlertDialog.Builder(requireContext())
                .setTitle(R.string.confirm_transfer_completion)
                .setMessage(getString(
                        R.string.completion_confirmation_message,
                        displayed.getReservationReference()))
                .setNegativeButton(android.R.string.cancel, null)
                .setPositiveButton(R.string.complete_transfer,
                        (dialog, which) -> viewModel.complete(true))
                .show();
    }

    private void returnToScanner(View view) {
        viewModel.prepareForScan();
        Navigation.findNavController(view).popBackStack();
    }

    private void navigateToCompletion(View view) {
        NavController controller = Navigation.findNavController(view);
        if (controller.getCurrentDestination() != null
                && controller.getCurrentDestination().getId() == R.id.nav_transaction_verification) {
            controller.navigate(R.id.nav_transaction_completion);
        }
    }
}
