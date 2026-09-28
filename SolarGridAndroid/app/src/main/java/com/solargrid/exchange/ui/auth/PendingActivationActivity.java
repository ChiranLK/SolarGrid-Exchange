package com.solargrid.exchange.ui.auth;

import android.content.Context;
import android.content.Intent;
import android.os.Bundle;
import android.view.View;
import android.widget.TextView;

import androidx.annotation.Nullable;
import androidx.appcompat.app.AppCompatActivity;

import com.solargrid.exchange.R;

/**
 * Shown after registration, after a login refused with "awaiting activation", or when a stored
 * session turns out to be pending. It only explains the state: activation is done by Backoffice,
 * and there is no timer or local activation.
 */
public final class PendingActivationActivity extends AppCompatActivity {
    static final String EXTRA_EMAIL = "com.solargrid.exchange.extra.PENDING_EMAIL";

    public static Intent intent(Context context, @Nullable String email) {
        Intent intent = new Intent(context, PendingActivationActivity.class);
        if (email != null && !email.trim().isEmpty()) {
            intent.putExtra(EXTRA_EMAIL, email.trim());
        }
        return intent;
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_account_notice);

        ((TextView) findViewById(R.id.account_notice_title)).setText(R.string.pending_activation_title);
        ((TextView) findViewById(R.id.account_notice_message)).setText(R.string.pending_activation_message);
        ((TextView) findViewById(R.id.account_notice_next_steps)).setText(R.string.pending_activation_next_steps);

        String email = getIntent().getStringExtra(EXTRA_EMAIL);
        TextView detail = findViewById(R.id.account_notice_detail);
        if (email != null) {
            detail.setText(getString(R.string.pending_activation_email, email));
            detail.setVisibility(View.VISIBLE);
        }

        findViewById(R.id.account_notice_sign_in)
                .setOnClickListener(ignored -> AccountNavigator.toSignIn(this, null));
    }
}
