package com.solargrid.exchange.ui.auth;

import android.content.Context;

import androidx.annotation.Nullable;

import com.google.android.material.textfield.TextInputLayout;
import com.solargrid.exchange.R;
import com.solargrid.exchange.features.auth.FormError;

/** Turns a {@link FormError} into user-facing text: string resources, or the API's own message. */
public final class FormErrorText {
    private FormErrorText() {
    }

    @Nullable
    public static String resolve(Context context, @Nullable FormError error) {
        if (error == null) {
            return null;
        }
        switch (error.getIssue()) {
            case REQUIRED:
                return context.getString(R.string.field_required);
            case INVALID_NIC:
                return context.getString(R.string.field_invalid_nic);
            case INVALID_EMAIL:
                return context.getString(R.string.field_invalid_email);
            case TOO_LONG:
                return context.getResources().getQuantityString(
                        R.plurals.field_too_long, error.getLimit(), error.getLimit());
            case PASSWORD_TOO_SHORT:
                return context.getString(R.string.field_password_too_short);
            case PASSWORDS_DO_NOT_MATCH:
                return context.getString(R.string.field_passwords_do_not_match);
            case SERVER:
            default:
                return error.getServerMessage();
        }
    }

    /** Shows or clears the error on a Material text field (announced by TalkBack). */
    public static void apply(TextInputLayout layout, @Nullable FormError error) {
        String text = resolve(layout.getContext(), error);
        layout.setError(text);
        layout.setErrorEnabled(text != null);
    }
}
