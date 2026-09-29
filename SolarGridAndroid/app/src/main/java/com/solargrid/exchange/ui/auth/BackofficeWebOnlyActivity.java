package com.solargrid.exchange.ui.auth;

import android.os.Bundle;
import android.view.View;
import android.widget.TextView;

import androidx.appcompat.app.AppCompatActivity;

import com.solargrid.exchange.R;

/**
 * Backoffice administration is a web responsibility. A Backoffice sign-in is not kept on the
 * device (the caller clears the session first); this screen explains where to go instead.
 */
public final class BackofficeWebOnlyActivity extends AppCompatActivity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_account_notice);

        ((TextView) findViewById(R.id.account_notice_title)).setText(R.string.backoffice_web_only_title);
        ((TextView) findViewById(R.id.account_notice_message)).setText(R.string.backoffice_web_only_message);
        TextView detail = findViewById(R.id.account_notice_detail);
        detail.setText(R.string.backoffice_web_only_signed_out);
        detail.setVisibility(View.VISIBLE);
        findViewById(R.id.account_notice_next_steps).setVisibility(View.GONE);

        findViewById(R.id.account_notice_sign_in)
                .setOnClickListener(ignored -> AccountNavigator.toSignIn(this, null));
    }
}
