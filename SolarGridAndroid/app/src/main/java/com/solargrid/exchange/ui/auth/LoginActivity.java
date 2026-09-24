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
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;

public final class LoginActivity extends AppCompatActivity {
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

        LoginViewModel viewModel = new ViewModelProvider(this).get(LoginViewModel.class);
        viewModel.getState().observe(this, state -> {
            boolean loading = state.getStatus() == UiState.Status.LOADING;
            progress.setVisibility(loading ? View.VISIBLE : View.GONE);
            submitButton.setEnabled(!loading);

            if (state.getStatus() == UiState.Status.ERROR && state.getError() != null) {
                errorMessage.setText(state.getError().getMessage());
                errorMessage.setVisibility(View.VISIBLE);
            } else {
                errorMessage.setVisibility(View.GONE);
            }

            if (!routed && state.getStatus() == UiState.Status.SUCCESS) {
                routed = true;
                Intent intent = new Intent(this, MainActivity.class);
                intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
                startActivity(intent);
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
    }
}
