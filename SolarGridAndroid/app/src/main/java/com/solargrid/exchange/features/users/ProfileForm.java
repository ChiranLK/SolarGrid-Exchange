package com.solargrid.exchange.features.users;

import com.solargrid.exchange.features.auth.AccountFieldRules;
import com.solargrid.exchange.features.auth.FormError;
import com.solargrid.exchange.network.ApiError;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.EnumMap;
import java.util.Locale;
import java.util.Map;

/**
 * The only profile fields a Prosumer may change (UpdateProfileDto). NIC, role, status, station and
 * deactivation fields are deliberately absent so they can never be sent.
 */
public final class ProfileForm {
    public enum Field {
        FULL_NAME("fullname"),
        EMAIL("email"),
        PHONE("phone"),
        ADDRESS("address");

        private final String apiKey;

        Field(String apiKey) {
            this.apiKey = apiKey;
        }

        public String getApiKey() {
            return apiKey;
        }
    }

    private final String fullName;
    private final String email;
    private final String phone;
    private final String address;

    public ProfileForm(String fullName, String email, String phone, String address) {
        this.fullName = AccountFieldRules.trim(fullName);
        this.email = AccountFieldRules.trim(email);
        this.phone = AccountFieldRules.trim(phone);
        this.address = AccountFieldRules.trim(address);
    }

    public String getEmail() {
        return email;
    }

    public Map<Field, FormError> validate() {
        Map<Field, FormError> errors = new EnumMap<>(Field.class);
        put(errors, Field.FULL_NAME, AccountFieldRules.required(fullName, AccountFieldRules.MAX_FULL_NAME));
        put(errors, Field.EMAIL, AccountFieldRules.email(email));
        put(errors, Field.PHONE, AccountFieldRules.required(phone, AccountFieldRules.MAX_PHONE));
        put(errors, Field.ADDRESS, AccountFieldRules.optional(address, AccountFieldRules.MAX_ADDRESS));
        return errors;
    }

    /** PUT body with exactly the four allowed fields; a blank address clears it on the server. */
    public JSONObject toRequestJson() throws JSONException {
        JSONObject body = new JSONObject();
        body.put("fullName", fullName);
        body.put("email", email);
        body.put("phone", phone);
        body.put("address", address.isEmpty() ? JSONObject.NULL : address);
        return body;
    }

    public static Map<Field, FormError> fieldErrorsFrom(ApiError error) {
        Map<Field, FormError> errors = new EnumMap<>(Field.class);
        for (Field field : Field.values()) {
            String message = error.getFieldError(field.getApiKey());
            if (message != null && !message.isEmpty()) {
                errors.put(field, FormError.fromServer(message));
            }
        }
        if (error.getKind() == ApiError.Kind.CONFLICT
                && error.getMessage().toLowerCase(Locale.ROOT).contains("email")) {
            errors.put(Field.EMAIL, FormError.fromServer(error.getMessage()));
        }
        return errors;
    }

    private static void put(Map<Field, FormError> errors, Field field, FormError error) {
        if (error != null) {
            errors.put(field, error);
        }
    }
}
