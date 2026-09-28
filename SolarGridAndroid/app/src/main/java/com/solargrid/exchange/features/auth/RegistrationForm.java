package com.solargrid.exchange.features.auth;

import com.solargrid.exchange.network.ApiError;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.EnumMap;
import java.util.Locale;
import java.util.Map;

/** Prosumer self-registration input for POST /api/auth/register (RegisterRequestDto). */
public final class RegistrationForm {
    public enum Field {
        NIC("nic"),
        FULL_NAME("fullname"),
        EMAIL("email"),
        PHONE("phone"),
        ADDRESS("address"),
        PASSWORD("password"),
        CONFIRM_PASSWORD(null);

        private final String apiKey;

        Field(String apiKey) {
            this.apiKey = apiKey;
        }

        /** Lower-case request property name used in API validation errors, or null if client-only. */
        public String getApiKey() {
            return apiKey;
        }
    }

    private final String nic;
    private final String fullName;
    private final String email;
    private final String phone;
    private final String address;
    private final String password;
    private final String confirmPassword;

    public RegistrationForm(String nic, String fullName, String email, String phone,
                            String address, String password, String confirmPassword) {
        this.nic = AccountFieldRules.trim(nic);
        this.fullName = AccountFieldRules.trim(fullName);
        this.email = AccountFieldRules.trim(email);
        this.phone = AccountFieldRules.trim(phone);
        this.address = AccountFieldRules.trim(address);
        // Passwords are not trimmed: spaces may be intentional and the API compares them exactly.
        this.password = password == null ? "" : password;
        this.confirmPassword = confirmPassword == null ? "" : confirmPassword;
    }

    public String getEmail() {
        return email;
    }

    /** Client checks mirroring RegisterRequestDto; empty when the form may be sent. */
    public Map<Field, FormError> validate() {
        Map<Field, FormError> errors = new EnumMap<>(Field.class);
        put(errors, Field.NIC, AccountFieldRules.nic(nic));
        put(errors, Field.FULL_NAME, AccountFieldRules.required(fullName, AccountFieldRules.MAX_FULL_NAME));
        put(errors, Field.EMAIL, AccountFieldRules.email(email));
        put(errors, Field.PHONE, AccountFieldRules.required(phone, AccountFieldRules.MAX_PHONE));
        put(errors, Field.ADDRESS, AccountFieldRules.optional(address, AccountFieldRules.MAX_ADDRESS));
        put(errors, Field.PASSWORD, AccountFieldRules.password(password));
        if (!confirmPassword.equals(password)) {
            errors.put(Field.CONFIRM_PASSWORD, FormError.of(FormError.Issue.PASSWORDS_DO_NOT_MATCH));
        }
        return errors;
    }

    /** Request body: trimmed values, optional address omitted when blank, no confirm field. */
    public JSONObject toRequestJson() throws JSONException {
        JSONObject body = new JSONObject();
        body.put("nic", nic);
        body.put("fullName", fullName);
        body.put("email", email);
        body.put("phone", phone);
        if (!address.isEmpty()) {
            body.put("address", address);
        }
        body.put("password", password);
        return body;
    }

    /** Maps API validation errors and duplicate NIC/email conflicts onto form fields. */
    public static Map<Field, FormError> fieldErrorsFrom(ApiError error) {
        Map<Field, FormError> errors = new EnumMap<>(Field.class);
        for (Field field : Field.values()) {
            String message = field.getApiKey() == null ? null : error.getFieldError(field.getApiKey());
            if (message != null && !message.isEmpty()) {
                errors.put(field, FormError.fromServer(message));
            }
        }
        if (error.getKind() == ApiError.Kind.CONFLICT) {
            String lower = error.getMessage().toLowerCase(Locale.ROOT);
            if (lower.contains("nic")) errors.put(Field.NIC, FormError.fromServer(error.getMessage()));
            if (lower.contains("email")) errors.put(Field.EMAIL, FormError.fromServer(error.getMessage()));
        }
        return errors;
    }

    private static void put(Map<Field, FormError> errors, Field field, FormError error) {
        if (error != null) {
            errors.put(field, error);
        }
    }
}
