package com.solargrid.exchange;

import android.app.Application;

import com.solargrid.exchange.core.AppContainer;

public final class SolarGridApplication extends Application {
    private AppContainer appContainer;

    @Override
    public void onCreate() {
        super.onCreate();
        appContainer = new AppContainer(this);
    }

    public AppContainer getAppContainer() {
        return appContainer;
    }
}
