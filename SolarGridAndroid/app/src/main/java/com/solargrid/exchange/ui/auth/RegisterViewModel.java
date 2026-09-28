package com.solargrid.exchange.ui.auth;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.features.auth.RegistrationController;
import com.solargrid.exchange.features.auth.RegistrationForm;

/** Keeps registration state across rotation; the controller owns the in-flight guard. */
public final class RegisterViewModel extends AndroidViewModel {
    private final MutableLiveData<RegistrationController.State> state = new MutableLiveData<>();
    private final RegistrationController controller;
    private boolean routedToPending;

    public RegisterViewModel(@NonNull Application application) {
        super(application);
        controller = new RegistrationController(
                ((SolarGridApplication) application).getAppContainer().getAuthRepository(),
                state::setValue);
        state.setValue(controller.getState());
    }

    public LiveData<RegistrationController.State> getState() {
        return state;
    }

    public boolean submit(RegistrationForm form) {
        return controller.submit(form);
    }

    /** True exactly once after success, so rotation does not open the pending screen twice. */
    public boolean consumePendingNavigation() {
        RegistrationController.State current = controller.getState();
        if (!current.isRegistered() || routedToPending) {
            return false;
        }
        routedToPending = true;
        return true;
    }
}
