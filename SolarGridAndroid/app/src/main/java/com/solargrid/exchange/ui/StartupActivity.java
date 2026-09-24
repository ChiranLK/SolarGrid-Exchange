package com.solargrid.exchange.ui;

import android.content.Intent;
import android.os.Bundle;
import android.view.View;

import androidx.appcompat.app.AppCompatActivity;
import androidx.lifecycle.ViewModelProvider;

import com.solargrid.exchange.R;
import com.solargrid.exchange.ui.auth.LoginActivity;
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
        StartupViewModel viewModel = new ViewModelProvider(this).get(StartupViewModel.class);
        viewModel.getState().observe(this, state -> {
            if (routed) {
                return;
            }
            loading.setVisibility(state.getStatus() == UiState.Status.LOADING ||
                    state.getStatus() == UiState.Status.IDLE ? View.VISIBLE : View.GONE);
            if (state.getStatus() == UiState.Status.SUCCESS) {
                routeTo(MainActivity.class);
            } else if (state.getStatus() == UiState.Status.ERROR && state.getError() != null) {
                if (state.getError().isAuthenticationExpired()) {
                    routeTo(LoginActivity.class);
                } else {
                    stateView.showError(state.getError(), ignored -> viewModel.restoreSession());
                }
            } else {
                stateView.hide();
            }
        });
        viewModel.restoreSession();
    }

    private void routeTo(Class<?> activityClass) {
        routed = true;
        startActivity(new Intent(this, activityClass));
        finish();
    }
}
