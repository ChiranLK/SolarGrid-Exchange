package com.solargrid.exchange.ui.auth;

import android.app.Activity;
import android.content.Intent;

import androidx.annotation.Nullable;

import com.solargrid.exchange.ui.MainActivity;

/** Single place for Member 1 entry navigation so every screen routes accounts the same way. */
public final class AccountNavigator {
    private AccountNavigator() {
    }

    public static void toMainApp(Activity from) {
        startClearingTask(from, new Intent(from, MainActivity.class));
    }

    /** Sign-in screen as a fresh task, optionally with an explanation (e.g. account deactivated). */
    public static void toSignIn(Activity from, @Nullable String notice) {
        Intent intent = new Intent(from, LoginActivity.class);
        if (notice != null) {
            intent.putExtra(LoginActivity.EXTRA_NOTICE, notice);
        }
        startClearingTask(from, intent);
    }

    /** Pending screen as a fresh task (used when no sign-in screen is underneath). */
    public static void toPendingActivation(Activity from, @Nullable String email) {
        startClearingTask(from, PendingActivationActivity.intent(from, email));
    }

    public static void toBackofficeWebOnly(Activity from) {
        startClearingTask(from, new Intent(from, BackofficeWebOnlyActivity.class));
    }

    private static void startClearingTask(Activity from, Intent intent) {
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        from.startActivity(intent);
        from.finish();
    }
}
