package com.solargrid.exchange.ui.auth;

import android.content.Intent;
import android.os.Bundle;
import android.util.Patterns;
import android.view.View;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ProgressBar;
import android.widget.TextView;

import androidx.appcompat.app.AppCompatActivity;
import androidx.lifecycle.ViewModelProvider;

import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.features.auth.AccountRoutePolicy;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

public final class LoginActivity extends AppCompatActivity {
    /** Optional explanation shown above the form (e.g. why the previous session ended). */
    public static final String EXTRA_NOTICE = "com.solargrid.exchange.extra.LOGIN_NOTICE";

    private EditText emailInput;
    private EditText passwordInput;
    private Button submitButton;
    private ProgressBar progress;
    private TextView errorMessage;
    private boolean routed;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_login);

        emailInput = findViewById(R.id.login_email);
        passwordInput = findViewById(R.id.login_password);
        submitButton = findViewById(R.id.login_submit);
        progress = findViewById(R.id.login_progress);
        errorMessage = findViewById(R.id.login_error);

        String notice = getIntent().getStringExtra(EXTRA_NOTICE);
        TextView noticeView = findViewById(R.id.login_notice);
        if (notice != null && !notice.trim().isEmpty()) {
            noticeView.setText(notice);
            noticeView.setVisibility(View.VISIBLE);
        }

        LoginViewModel viewModel = new ViewModelProvider(this).get(LoginViewModel.class);
        viewModel.getState().observe(this, state -> {
            boolean loading = state.getStatus() == UiState.Status.LOADING;
            progress.setVisibility(loading ? View.VISIBLE : View.GONE);
            submitButton.setEnabled(!loading);

            if (state.getStatus() == UiState.Status.ERROR && state.getError() != null) {
                ApiError error = state.getError();
                if (AccountRoutePolicy.afterLoginFailure(error) == AccountRoutePolicy.Destination.PENDING_ACTIVATION) {
                    // Correct credentials but not yet approved: explain instead of showing an error.
                    viewModel.acknowledgeResult();
                    startActivity(PendingActivationActivity.intent(this, emailInput.getText().toString().trim()));
                    return;
                }
                errorMessage.setText(error.getMessage());
                errorMessage.setVisibility(View.VISIBLE);
            } else {
                errorMessage.setVisibility(View.GONE);
            }

            if (!routed && state.getStatus() == UiState.Status.SUCCESS && state.getData() != null) {
                routed = true;
                routeAfterLogin(state.getData());
            }
        });

        submitButton.setOnClickListener(view -> {
            String email = emailInput.getText().toString().trim();
            String password = passwordInput.getText().toString();
            if (!Patterns.EMAIL_ADDRESS.matcher(email).matches()) {
                emailInput.setError(getString(R.string.valid_email_required));
                return;
            }
            if (password.isEmpty()) {
                passwordInput.setError(getString(R.string.password_required));
                return;
            }
            viewModel.login(email, password);
        });

        findViewById(R.id.login_register).setOnClickListener(
                ignored -> startActivity(new Intent(this, RegisterActivity.class)));
    }

    private void routeAfterLogin(SessionUser session) {
        if (AccountRoutePolicy.afterAuthentication(session) == AccountRoutePolicy.Destination.BACKOFFICE_WEB_ONLY) {
            // Backoffice work is web-only: do not keep a Backoffice token on the device.
            ((SolarGridApplication) getApplication()).getAppContainer().getAuthRepository().logout();
            AccountNavigator.toBackofficeWebOnly(this);
            return;
        }
        AccountNavigator.toMainApp(this);
    }
}
