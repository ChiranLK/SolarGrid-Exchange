package com.solargrid.exchange;

import android.app.Application;

import com.solargrid.exchange.ui.common.UiPreferences;

import com.solargrid.exchange.core.AppContainer;

public final class SolarGridApplication extends Application {
    private AppContainer appContainer;

    @Override
    public void onCreate() {
        super.onCreate();
        // Device-only appearance choice (System / Light / Dark) from the profile or toolbar picker.
        UiPreferences.applyStoredTheme(this);
        appContainer = new AppContainer(this);
    }

    public AppContainer getAppContainer() {
        return appContainer;
    }
}
