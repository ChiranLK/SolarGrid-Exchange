package com.solargrid.exchange.ui.auth;

import android.os.Bundle;
import android.view.View;
import android.view.inputmethod.EditorInfo;
import android.widget.Button;
import android.widget.EditText;
import android.widget.TextView;

import androidx.appcompat.app.AppCompatActivity;
import androidx.lifecycle.ViewModelProvider;

import com.google.android.material.textfield.TextInputLayout;
import com.solargrid.exchange.R;
import com.solargrid.exchange.features.auth.FormError;
import com.solargrid.exchange.features.auth.RegistrationController;
import com.solargrid.exchange.features.auth.RegistrationForm;

import java.util.EnumMap;
import java.util.Map;

/** Prosumer self-registration (POST /api/auth/register). Never signs the new account in. */
public final class RegisterActivity extends AppCompatActivity {
    private final Map<RegistrationForm.Field, TextInputLayout> fields = new EnumMap<>(RegistrationForm.Field.class);
    private RegisterViewModel viewModel;
    private Button submit;
    private View progress;
    private TextView error;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_register);

        fields.put(RegistrationForm.Field.NIC, findViewById(R.id.register_nic_layout));
        fields.put(RegistrationForm.Field.FULL_NAME, findViewById(R.id.register_full_name_layout));
        fields.put(RegistrationForm.Field.EMAIL, findViewById(R.id.register_email_layout));
        fields.put(RegistrationForm.Field.PHONE, findViewById(R.id.register_phone_layout));
        fields.put(RegistrationForm.Field.ADDRESS, findViewById(R.id.register_address_layout));
        fields.put(RegistrationForm.Field.PASSWORD, findViewById(R.id.register_password_layout));
        fields.put(RegistrationForm.Field.CONFIRM_PASSWORD, findViewById(R.id.register_confirm_password_layout));
        submit = findViewById(R.id.register_submit);
        progress = findViewById(R.id.register_progress);
        error = findViewById(R.id.register_error);

        viewModel = new ViewModelProvider(this).get(RegisterViewModel.class);
        viewModel.getState().observe(this, this::render);

        submit.setOnClickListener(ignored -> submitForm());
        EditText confirm = findViewById(R.id.register_confirm_password);
        confirm.setOnEditorActionListener((view, actionId, event) -> {
            if (actionId == EditorInfo.IME_ACTION_DONE) {
                submitForm();
                return true;
            }
            return false;
        });
        findViewById(R.id.register_back_to_login).setOnClickListener(ignored -> finish());
    }

    private void submitForm() {
        RegistrationForm form = new RegistrationForm(
                text(R.id.register_nic),
                text(R.id.register_full_name),
                text(R.id.register_email),
                text(R.id.register_phone),
                text(R.id.register_address),
                text(R.id.register_password),
                text(R.id.register_confirm_password));
        viewModel.submit(form);
    }

    private void render(RegistrationController.State state) {
        boolean submitting = state.isSubmitting();
        submit.setEnabled(!submitting && !state.isRegistered());
        progress.setVisibility(submitting ? View.VISIBLE : View.GONE);
        for (TextInputLayout layout : fields.values()) {
            layout.setEnabled(!submitting);
        }

        for (Map.Entry<RegistrationForm.Field, TextInputLayout> entry : fields.entrySet()) {
            FormError fieldError = state.getFieldErrors().get(entry.getKey());
            FormErrorText.apply(entry.getValue(), fieldError);
        }

        if (state.getError() != null) {
            error.setText(state.getError().getMessage());
            error.setVisibility(View.VISIBLE);
        } else if (!state.getFieldErrors().isEmpty()) {
            error.setText(R.string.form_has_errors);
            error.setVisibility(View.VISIBLE);
        } else {
            error.setVisibility(View.GONE);
        }

        if (viewModel.consumePendingNavigation()) {
            // Keep the sign-in screen underneath so "Back to sign in" returns to it.
            startActivity(PendingActivationActivity.intent(this, state.getRegisteredEmail()));
            finish();
        }
    }

    private String text(int id) {
        CharSequence value = ((EditText) findViewById(id)).getText();
        return value == null ? "" : value.toString();
    }
}
