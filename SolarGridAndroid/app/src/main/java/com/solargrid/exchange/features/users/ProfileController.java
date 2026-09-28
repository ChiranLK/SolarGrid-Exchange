package com.solargrid.exchange.features.users;

import androidx.annotation.Nullable;

import com.solargrid.exchange.data.model.ProsumerProfile;
import com.solargrid.exchange.features.auth.FormError;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;

import java.util.Collections;
import java.util.EnumMap;
import java.util.Locale;
import java.util.Map;

/**
 * Prosumer profile screen logic without Android dependencies: server-authoritative load, allowed
 * field update, and deactivation request. Each operation ignores repeat taps while in flight, and
 * only API-confirmed values are written to the local session cache.
 */
public final class ProfileController {
    public interface Gateway {
        void getProfile(ApiCallback<ProsumerProfile> callback);
        void updateProfile(ProfileForm form, ApiCallback<ProsumerProfile> callback);
        void requestDeactivation(ApiCallback<ProsumerProfile> callback);
    }

    /** Permitted local cache of profile display fields (the existing SQLite session row). */
    public interface ProfileCache {
        void cacheProfile(ProsumerProfile profile);
    }

    public interface Listener {
        void onStateChanged(State state);
    }

    public enum LoadStatus { IDLE, LOADING, READY, ERROR }

    public enum Notice { NONE, PROFILE_SAVED, PROFILE_SAVED_EMAIL_CHANGED, DEACTIVATION_REQUESTED, DEACTIVATION_ALREADY_PENDING }

    public static final class State {
        private final LoadStatus loadStatus;
        @Nullable private final ProsumerProfile profile;
        @Nullable private final ApiError loadError;
        private final boolean saving;
        private final boolean requestingDeactivation;
        private final Map<ProfileForm.Field, FormError> fieldErrors;
        @Nullable private final ApiError actionError;
        private final Notice notice;

        State(LoadStatus loadStatus, @Nullable ProsumerProfile profile, @Nullable ApiError loadError,
              boolean saving, boolean requestingDeactivation, Map<ProfileForm.Field, FormError> fieldErrors,
              @Nullable ApiError actionError, Notice notice) {
            this.loadStatus = loadStatus;
            this.profile = profile;
            this.loadError = loadError;
            this.saving = saving;
            this.requestingDeactivation = requestingDeactivation;
            this.fieldErrors = Collections.unmodifiableMap(fieldErrors.isEmpty()
                    ? new EnumMap<>(ProfileForm.Field.class) : new EnumMap<>(fieldErrors));
            this.actionError = actionError;
            this.notice = notice;
        }

        public LoadStatus getLoadStatus() { return loadStatus; }
        @Nullable public ProsumerProfile getProfile() { return profile; }
        @Nullable public ApiError getLoadError() { return loadError; }
        public boolean isSaving() { return saving; }
        public boolean isRequestingDeactivation() { return requestingDeactivation; }
        public boolean isBusy() { return saving || requestingDeactivation; }
        public Map<ProfileForm.Field, FormError> getFieldErrors() { return fieldErrors; }
        @Nullable public ApiError getActionError() { return actionError; }
        public Notice getNotice() { return notice; }

        /** UI hint only: the API decides whether a request is accepted. */
        public boolean canRequestDeactivation() {
            return profile != null && profile.isActive() && !profile.isDeactivationRequested() && !isBusy();
        }

        State with(LoadStatus status, @Nullable ProsumerProfile nextProfile, @Nullable ApiError nextLoadError) {
            return new State(status, nextProfile, nextLoadError, saving, requestingDeactivation, fieldErrors, actionError, notice);
        }

        State busy(boolean nextSaving, boolean nextRequesting) {
            return new State(loadStatus, profile, loadError, nextSaving, nextRequesting,
                    new EnumMap<>(ProfileForm.Field.class), null, Notice.NONE);
        }

        State afterAction(@Nullable ProsumerProfile nextProfile, Map<ProfileForm.Field, FormError> errors,
                          @Nullable ApiError error, Notice nextNotice) {
            return new State(loadStatus, nextProfile, loadError, false, false, errors, error, nextNotice);
        }
    }

    private static final Map<ProfileForm.Field, FormError> NO_ERRORS = new EnumMap<>(ProfileForm.Field.class);

    private final Gateway gateway;
    private final ProfileCache cache;
    private final Listener listener;
    private State state = new State(LoadStatus.IDLE, null, null, false, false, NO_ERRORS, null, Notice.NONE);

    public ProfileController(Gateway gateway, ProfileCache cache, Listener listener) {
        this.gateway = gateway;
        this.cache = cache;
        this.listener = listener;
    }

    public State getState() {
        return state;
    }

    /** Loads GET /api/prosumers/me. Ignored while a load is already running. */
    public boolean load() {
        if (state.getLoadStatus() == LoadStatus.LOADING) {
            return false;
        }
        publish(state.with(LoadStatus.LOADING, state.getProfile(), null));
        gateway.getProfile(new ApiCallback<ProsumerProfile>() {
            @Override
            public void onSuccess(ProsumerProfile profile) {
                cache.cacheProfile(profile);
                publish(state.with(LoadStatus.READY, profile, null));
            }

            @Override
            public void onError(ApiError error) {
                publish(state.with(LoadStatus.ERROR, state.getProfile(), error));
            }
        });
        return true;
    }

    /** Sends PUT /api/prosumers/me with the four allowed fields. */
    public boolean save(ProfileForm form) {
        ProsumerProfile current = state.getProfile();
        if (current == null || state.isBusy()) {
            return false;
        }
        Map<ProfileForm.Field, FormError> errors = form.validate();
        if (!errors.isEmpty()) {
            publish(state.afterAction(current, errors, null, Notice.NONE));
            return false;
        }

        publish(state.busy(true, false));
        gateway.updateProfile(form, new ApiCallback<ProsumerProfile>() {
            @Override
            public void onSuccess(ProsumerProfile updated) {
                boolean emailChanged = !current.getEmail().toLowerCase(Locale.ROOT)
                        .equals(updated.getEmail().toLowerCase(Locale.ROOT));
                cache.cacheProfile(updated);
                publish(state.afterAction(updated, NO_ERRORS, null,
                        emailChanged ? Notice.PROFILE_SAVED_EMAIL_CHANGED : Notice.PROFILE_SAVED));
            }

            @Override
            public void onError(ApiError error) {
                publish(state.afterAction(state.getProfile(), ProfileForm.fieldErrorsFrom(error), error, Notice.NONE));
            }
        });
        return true;
    }

    /**
     * Sends POST /api/prosumers/me/deactivation-request after the user confirmed. The account stays
     * active; only Backoffice can deactivate it.
     */
    public boolean requestDeactivation() {
        if (!state.canRequestDeactivation()) {
            return false;
        }
        publish(state.busy(false, true));
        gateway.requestDeactivation(new ApiCallback<ProsumerProfile>() {
            @Override
            public void onSuccess(ProsumerProfile updated) {
                cache.cacheProfile(updated);
                publish(state.afterAction(updated, NO_ERRORS, null, Notice.DEACTIVATION_REQUESTED));
            }

            @Override
            public void onError(ApiError error) {
                if (error.getKind() == ApiError.Kind.CONFLICT) {
                    // Already pending on the server: show that, then reload the authoritative state.
                    publish(state.afterAction(state.getProfile(), NO_ERRORS, null, Notice.DEACTIVATION_ALREADY_PENDING));
                    load();
                } else {
                    publish(state.afterAction(state.getProfile(), NO_ERRORS, error, Notice.NONE));
                }
            }
        });
        return true;
    }

    /** Clears a shown success message or action error. */
    public void dismissMessages() {
        publish(state.afterAction(state.getProfile(), state.getFieldErrors(), null, Notice.NONE));
    }

    private void publish(State next) {
        state = next;
        listener.onStateChanged(next);
    }
}
