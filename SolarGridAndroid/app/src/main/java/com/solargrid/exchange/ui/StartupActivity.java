package com.solargrid.exchange.ui;

import android.os.Bundle;
import android.view.View;

import androidx.appcompat.app.AppCompatActivity;
import androidx.lifecycle.ViewModelProvider;

import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.features.auth.AccountRoutePolicy;
import com.solargrid.exchange.features.auth.AuthRepository;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.auth.AccountNavigator;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;

public final class StartupActivity extends AppCompatActivity {
    private boolean routed;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_startup);
        View loading = findViewById(R.id.startup_loading);
        UiStateView stateView = findViewById(R.id.startup_state);
        AuthRepository authRepository =
                ((SolarGridApplication) getApplication()).getAppContainer().getAuthRepository();
        StartupViewModel viewModel = new ViewModelProvider(this).get(StartupViewModel.class);
        viewModel.getState().observe(this, state -> {
            if (routed) {
                return;
            }
            loading.setVisibility(state.getStatus() == UiState.Status.LOADING ||
                    state.getStatus() == UiState.Status.IDLE ? View.VISIBLE : View.GONE);
            if (state.getStatus() == UiState.Status.SUCCESS && state.getData() != null) {
                routed = true;
                if (AccountRoutePolicy.afterAuthentication(state.getData())
                        == AccountRoutePolicy.Destination.BACKOFFICE_WEB_ONLY) {
                    authRepository.logout();
                    AccountNavigator.toBackofficeWebOnly(this);
                } else {
                    AccountNavigator.toMainApp(this);
                }
            } else if (state.getStatus() == UiState.Status.ERROR && state.getError() != null) {
                handleSessionFailure(state.getError(), authRepository, stateView, viewModel);
            } else {
                stateView.hide();
            }
        });
        viewModel.restoreSession();
    }

    private void handleSessionFailure(ApiError error, AuthRepository authRepository,
                                      UiStateView stateView, StartupViewModel viewModel) {
        switch (AccountRoutePolicy.afterSessionFailure(error)) {
            case SIGN_IN:
                routed = true;
                AccountNavigator.toSignIn(this, null);
                break;
            case PENDING_ACTIVATION:
                routed = true;
                authRepository.logout();
                AccountNavigator.toPendingActivation(this, null);
                break;
            case SIGN_IN_WITH_ACCOUNT_NOTICE:
                // The stored token is still valid, but the account was deactivated since sign-in.
                routed = true;
                authRepository.logout();
                AccountNavigator.toSignIn(this, getString(R.string.account_no_longer_active, error.getMessage()));
                break;
            default:
                // Offline or server problem: keep the session and offer a retry.
                stateView.showError(error, ignored -> viewModel.restoreSession());
                break;
        }
    }
}
