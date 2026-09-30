package com.solargrid.exchange.ui.home;

import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.LinearLayout;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.Navigation;

import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.DashboardReservation;
import com.solargrid.exchange.data.model.DashboardStatusSummary;
import com.solargrid.exchange.data.model.ProsumerDashboard;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.DisplayFormats;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;
import com.solargrid.exchange.ui.dashboard.DashboardReservationAdapter;
import com.solargrid.exchange.ui.dashboard.ProsumerDashboardViewModel;

import java.util.List;

public final class ProsumerHomeFragment extends Fragment {
    private static final long REFRESH_INTERVAL_MS = 30_000L;
    private final Handler refreshHandler = new Handler(Looper.getMainLooper());
    private ProsumerDashboardViewModel viewModel;
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
        View view = inflater.inflate(R.layout.fragment_prosumer_dashboard, container, false);
        View content = view.findViewById(R.id.prosumer_dashboard_content);
        UiStateView stateView = view.findViewById(R.id.prosumer_dashboard_state);
        TextView refreshNotice = view.findViewById(R.id.prosumer_dashboard_refresh_notice);
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
        // Presentation only: personal greeting from the stored session name.
        ((TextView) view.findViewById(R.id.prosumer_dashboard_greeting)).setText(
                getString(R.string.sg_dashboard_greeting, DisplayFormats.firstName(session.getFullName())));
        viewModel = new ViewModelProvider(this).get(ProsumerDashboardViewModel.class);

        view.findViewById(R.id.prosumer_dashboard_refresh)
                .setOnClickListener(ignored -> viewModel.refresh());
        view.findViewById(R.id.prosumer_dashboard_history)
                .setOnClickListener(ignored -> Navigation.findNavController(view)
                        .navigate(R.id.nav_booking_history));

        viewModel.getState().observe(getViewLifecycleOwner(), state -> {
            content.setVisibility(state.getStatus() == UiState.Status.SUCCESS
                    ? View.VISIBLE : View.GONE);
            switch (state.getStatus()) {
                case LOADING:
                    stateView.showLoading(getString(R.string.loading_dashboard));
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

        viewModel.getRefreshError().observe(getViewLifecycleOwner(), error -> {
            if (error == null) {
                refreshNotice.setVisibility(View.GONE);
            } else if (error.isAuthenticationExpired()) {
                ((MainActivity) requireActivity()).handleAuthenticationExpiry();
            } else {
                refreshNotice.setText(getString(R.string.dashboard_refresh_failed, error.getMessage()));
                refreshNotice.setVisibility(View.VISIBLE);
            }
        });
        viewModel.load();
        return view;
    }

    @Override
    public void onStart() {
        super.onStart();
        if (viewModel != null && viewModel.getState().getValue() != null
                && viewModel.getState().getValue().getStatus() == UiState.Status.SUCCESS) {
            viewModel.refreshSilently();
        }
        refreshHandler.postDelayed(scheduledRefresh, REFRESH_INTERVAL_MS);
    }

    @Override
    public void onStop() {
        refreshHandler.removeCallbacks(scheduledRefresh);
        super.onStop();
    }

    private void bind(View root, ProsumerDashboard dashboard) {
        if (dashboard == null) {
            return;
        }
        DashboardStatusSummary summary = dashboard.getStatusSummary();
        ((TextView) root.findViewById(R.id.prosumer_dashboard_pending_count))
                .setText(String.valueOf(summary.getPendingCount()));
        ((TextView) root.findViewById(R.id.prosumer_dashboard_future_count))
                .setText(String.valueOf(summary.getApprovedFutureCount()));
        ((TextView) root.findViewById(R.id.prosumer_dashboard_status_summary)).setText(getString(
                R.string.sg_dashboard_summary,
                summary.getCurrentCount(),
                summary.getPendingTotal(),
                summary.getApprovedTotal(),
                summary.getRejectedTotal(),
                summary.getCancelledTotal(),
                summary.getCompletedTotal()));
        bindRows(root, R.id.prosumer_dashboard_current_list,
                R.id.prosumer_dashboard_current_empty, dashboard.getCurrentReservations());
        bindRows(root, R.id.prosumer_dashboard_pending_list,
                R.id.prosumer_dashboard_pending_empty, dashboard.getPendingReservations());
        bindRows(root, R.id.prosumer_dashboard_history_list,
                R.id.prosumer_dashboard_history_empty, dashboard.getRecentHistory());
    }

    private void bindRows(
            View root,
            int containerId,
            int emptyId,
            List<DashboardReservation> reservations) {
        LinearLayout container = root.findViewById(containerId);
        container.removeAllViews();
        root.findViewById(emptyId).setVisibility(reservations.isEmpty() ? View.VISIBLE : View.GONE);
        LayoutInflater inflater = LayoutInflater.from(requireContext());
        for (DashboardReservation reservation : reservations) {
            View row = inflater.inflate(R.layout.item_reservation, container, false);
            DashboardReservationAdapter.bind(row, reservation);
            row.setFocusable(true);
            row.setClickable(true);
            row.setOnClickListener(ignored -> openReservation(root, reservation.getReservationId()));
            container.addView(row);
        }
    }

    private void openReservation(View root, String reservationId) {
        Bundle arguments = new Bundle();
        arguments.putString("reservationId", reservationId);
        Navigation.findNavController(root).navigate(R.id.nav_reservation_detail, arguments);
    }
}
