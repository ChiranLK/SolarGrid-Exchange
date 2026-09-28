package com.solargrid.exchange.features.auth;

import androidx.annotation.Nullable;

import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;

import java.util.Collections;
import java.util.EnumMap;
import java.util.Map;

/**
 * Registration screen logic without Android dependencies. Guards against double submission,
 * applies client checks, and maps API validation/conflict responses onto fields. The password is
 * never retained: it lives only in the form passed to {@link #submit}.
 */
public final class RegistrationController {
    public interface Gateway {
        /** Sends POST /api/auth/register; succeeds with the registered (normalised) email. */
        void register(RegistrationForm form, ApiCallback<String> callback);
    }

    public interface Listener {
        void onStateChanged(State state);
    }

    public static final class State {
        private final boolean submitting;
        @Nullable private final String registeredEmail;
        private final Map<RegistrationForm.Field, FormError> fieldErrors;
        @Nullable private final ApiError error;

        State(boolean submitting, @Nullable String registeredEmail,
              Map<RegistrationForm.Field, FormError> fieldErrors, @Nullable ApiError error) {
            this.submitting = submitting;
            this.registeredEmail = registeredEmail;
            this.fieldErrors = Collections.unmodifiableMap(new EnumMap<>(fieldErrors.isEmpty()
                    ? new EnumMap<>(RegistrationForm.Field.class) : fieldErrors));
            this.error = error;
        }

        static State initial() {
            return new State(false, null, new EnumMap<>(RegistrationForm.Field.class), null);
        }

        public boolean isSubmitting() { return submitting; }
        public boolean isRegistered() { return registeredEmail != null; }
        @Nullable public String getRegisteredEmail() { return registeredEmail; }
        public Map<RegistrationForm.Field, FormError> getFieldErrors() { return fieldErrors; }
        @Nullable public ApiError getError() { return error; }
    }

    private final Gateway gateway;
    private final Listener listener;
    private State state = State.initial();

    public RegistrationController(Gateway gateway, Listener listener) {
        this.gateway = gateway;
        this.listener = listener;
    }

    public State getState() {
        return state;
    }

    /** @return true when a request was sent; false when blocked by validation or an in-flight request. */
    public boolean submit(RegistrationForm form) {
        if (state.isSubmitting() || state.isRegistered()) {
            return false;
        }
        Map<RegistrationForm.Field, FormError> errors = form.validate();
        if (!errors.isEmpty()) {
            publish(new State(false, null, errors, null));
            return false;
        }

        publish(new State(true, null, new EnumMap<>(RegistrationForm.Field.class), null));
        gateway.register(form, new ApiCallback<String>() {
            @Override
            public void onSuccess(String registeredEmail) {
                // A new Prosumer is PendingActivation: no session is created and no auto-login happens.
                publish(new State(false, registeredEmail, new EnumMap<>(RegistrationForm.Field.class), null));
            }

            @Override
            public void onError(ApiError error) {
                publish(new State(false, null, RegistrationForm.fieldErrorsFrom(error), error));
            }
        });
        return true;
    }

    private void publish(State next) {
        state = next;
        listener.onStateChanged(next);
    }
}
