package com.solargrid.exchange.ui.profile;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.core.AppContainer;
import com.solargrid.exchange.data.model.ProsumerProfile;
import com.solargrid.exchange.features.users.ProfileController;
import com.solargrid.exchange.features.users.ProfileForm;

/** Survives rotation; the controller keeps the server profile and in-flight guards. */
public final class ProfileViewModel extends AndroidViewModel {
    private final MutableLiveData<ProfileController.State> state = new MutableLiveData<>();
    private final ProfileController controller;
    @Nullable private ProsumerProfile profileBoundToForm;

    public ProfileViewModel(@NonNull Application application) {
        super(application);
        AppContainer container = ((SolarGridApplication) application).getAppContainer();
        controller = new ProfileController(
                container.getUserRepository(),
                container.getAuthRepository(),
                state::setValue);
        state.setValue(controller.getState());
    }

    public LiveData<ProfileController.State> getState() {
        return state;
    }

    /** Loads once per ViewModel; later calls are explicit retries. */
    public void loadIfNeeded() {
        if (controller.getState().getLoadStatus() == ProfileController.LoadStatus.IDLE) {
            controller.load();
        }
    }

    public void retry() {
        controller.load();
    }

    public void save(ProfileForm form) {
        controller.save(form);
    }

    public void requestDeactivation() {
        controller.requestDeactivation();
    }

    public void dismissMessages() {
        controller.dismissMessages();
    }

    /**
     * True when the edit fields should be (re)filled from this server profile. After rotation the
     * same profile is not re-applied, so the user's unsaved edits are kept.
     */
    public boolean shouldBindForm(@Nullable ProsumerProfile profile) {
        if (profile == null || profile == profileBoundToForm) {
            return false;
        }
        profileBoundToForm = profile;
        return true;
    }
}
