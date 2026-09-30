package com.solargrid.exchange.ui.common;

import android.app.Activity;
import android.content.Context;
import android.content.SharedPreferences;

import androidx.appcompat.app.AppCompatDelegate;

import com.google.android.material.dialog.MaterialAlertDialogBuilder;
import com.solargrid.exchange.R;

/**
 * Device-only UI preferences: the chosen appearance (system, light, dark) and whether the
 * first-run onboarding has been seen. Stores no account, session or server data.
 */
public final class UiPreferences {
    public static final int THEME_SYSTEM = 0;
    public static final int THEME_LIGHT = 1;
    public static final int THEME_DARK = 2;

    private static final String FILE = "solargrid_ui_preferences";
    private static final String KEY_THEME = "theme_mode";
    private static final String KEY_ONBOARDING_DONE = "onboarding_done";

    private UiPreferences() { }

    private static SharedPreferences prefs(Context context) {
        return context.getApplicationContext().getSharedPreferences(FILE, Context.MODE_PRIVATE);
    }

    public static int getThemeMode(Context context) {
        int value = prefs(context).getInt(KEY_THEME, THEME_SYSTEM);
        return value == THEME_LIGHT || value == THEME_DARK ? value : THEME_SYSTEM;
    }

    /** Applies the stored appearance; call from Application.onCreate before any activity starts. */
    public static void applyStoredTheme(Context context) {
        AppCompatDelegate.setDefaultNightMode(nightModeFor(getThemeMode(context)));
    }

    public static void setThemeMode(Context context, int mode) {
        prefs(context).edit().putInt(KEY_THEME, mode).apply();
        AppCompatDelegate.setDefaultNightMode(nightModeFor(mode));
    }

    static int nightModeFor(int mode) {
        switch (mode) {
            case THEME_LIGHT:
                return AppCompatDelegate.MODE_NIGHT_NO;
            case THEME_DARK:
                return AppCompatDelegate.MODE_NIGHT_YES;
            default:
                return AppCompatDelegate.MODE_NIGHT_FOLLOW_SYSTEM;
        }
    }

    /** Shows the System / Light / Dark picker. The change applies immediately to all screens. */
    public static void showAppearancePicker(Activity activity) {
        String[] labels = {
                activity.getString(R.string.sg_appearance_system),
                activity.getString(R.string.sg_appearance_light),
                activity.getString(R.string.sg_appearance_dark)
        };
        new MaterialAlertDialogBuilder(activity)
                .setTitle(R.string.sg_appearance_title)
                .setIcon(R.drawable.sg_ic_sun)
                .setSingleChoiceItems(labels, getThemeMode(activity), (dialog, which) -> {
                    dialog.dismiss();
                    if (which != getThemeMode(activity)) {
                        setThemeMode(activity, which);
                    }
                })
                .setNegativeButton(R.string.cancel, null)
                .show();
    }

    public static boolean isOnboardingDone(Context context) {
        return prefs(context).getBoolean(KEY_ONBOARDING_DONE, false);
    }

    public static void markOnboardingDone(Context context) {
        prefs(context).edit().putBoolean(KEY_ONBOARDING_DONE, true).apply();
    }
}
