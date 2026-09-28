package com.solargrid.exchange.ui.operations;

import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;

import androidx.activity.OnBackPressedCallback;
import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.NavController;
import androidx.navigation.NavOptions;
import androidx.navigation.Navigation;

import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.CompletedTransaction;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;
import com.solargrid.exchange.ui.reservations.ReservationFormatters;

public final class TransactionCompletionFragment extends Fragment {
    private OperatorTransactionViewModel viewModel;

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_transaction_completion, container, false);
        View content = view.findViewById(R.id.transaction_completion_content);
        UiStateView stateView = view.findViewById(R.id.transaction_completion_state);

        SessionUser session = ((SolarGridApplication) requireActivity().getApplication())
                .getAppContainer()
                .getAuthRepository()
                .getStoredSession();
        if (session == null) {
            ((MainActivity) requireActivity()).handleAuthenticationExpiry();
            return view;
        }
        if (!session.isGridOperator()) {
            stateView.showError(new ApiError(
                    ApiError.Kind.FORBIDDEN, 403, getString(R.string.operator_only_screen)), null);
            return view;
        }

        viewModel = new ViewModelProvider(requireActivity())
                .get(OperatorTransactionViewModel.class);
        view.findViewById(R.id.transaction_scan_another)
                .setOnClickListener(ignored -> openScanner(view));
        view.findViewById(R.id.transaction_operator_home)
                .setOnClickListener(ignored -> openOperatorHome(view));
        requireActivity().getOnBackPressedDispatcher().addCallback(
                getViewLifecycleOwner(),
                new OnBackPressedCallback(true) {
                    @Override
                    public void handleOnBackPressed() {
                        openOperatorHome(view);
                    }
                });

        viewModel.getCompletionState().observe(getViewLifecycleOwner(), state -> {
            boolean success = state.getStatus() == UiState.Status.SUCCESS && state.getData() != null;
            content.setVisibility(success ? View.VISIBLE : View.GONE);
            if (success) {
                stateView.hide();
                bind(view, state.getData());
            } else {
                stateView.showEmpty(
                        getString(R.string.completion_not_available),
                        getString(R.string.completion_restart_message),
                        ignored -> openScanner(view));
            }
        });
        return view;
    }

    private void bind(View view, CompletedTransaction value) {
        ((TextView) view.findViewById(R.id.transaction_completion_summary)).setText(getString(
                R.string.completion_summary,
                value.getReservationReference(),
                value.getStatus(),
                ReservationFormatters.localDateTime(value.getCompletedAtUtc())));
    }

    private void openScanner(View view) {
        viewModel.prepareForScan();
        NavController controller = Navigation.findNavController(view);
        NavOptions options = new NavOptions.Builder()
                .setPopUpTo(R.id.nav_qr_operations, true)
                .build();
        controller.navigate(R.id.nav_qr_operations, null, options);
    }

    private void openOperatorHome(View view) {
        viewModel.clearSensitiveState();
        NavController controller = Navigation.findNavController(view);
        NavOptions options = new NavOptions.Builder()
                .setPopUpTo(R.id.nav_operator_home, true)
                .build();
        controller.navigate(R.id.nav_operator_home, null, options);
    }
}
