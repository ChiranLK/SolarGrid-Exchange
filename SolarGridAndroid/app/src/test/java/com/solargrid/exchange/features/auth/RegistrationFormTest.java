package com.solargrid.exchange.features.auth;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import com.solargrid.exchange.network.ApiError;

import org.json.JSONObject;
import org.junit.Test;

import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.Map;
import java.util.Set;

public final class RegistrationFormTest {
    // Synthetic test identity; not a real NIC.
    static RegistrationForm valid() {
        return new RegistrationForm(" 200000000004 ", " Test Prosumer ", " prosumer@example.com ",
                " 0770000004 ", "  ", "ValidPass1", "ValidPass1");
    }

    @Test
    public void validFormHasNoErrors() {
        assertTrue(valid().validate().isEmpty());
        assertTrue(new RegistrationForm("991234567v", "A", "a@b.co", "1", "", "12345678", "12345678")
                .validate().isEmpty());
    }

    @Test
    public void requestIsTrimmedOmitsBlankAddressAndNeverSendsConfirmation() throws Exception {
        JSONObject body = valid().toRequestJson();

        assertEquals("200000000004", body.getString("nic"));
        assertEquals("Test Prosumer", body.getString("fullName"));
        assertEquals("prosumer@example.com", body.getString("email"));
        assertEquals("0770000004", body.getString("phone"));
        assertEquals("ValidPass1", body.getString("password"));
        assertEquals(setOf("nic", "fullName", "email", "phone", "password"), keys(body));
        assertFalse(body.has("confirmPassword"));
        assertFalse(body.has("address"));
    }

    @Test
    public void addressIsSentWhenProvided() throws Exception {
        RegistrationForm form = new RegistrationForm("200000000004", "A", "a@b.co", "1",
                " 12 Lake Road ", "12345678", "12345678");

        assertEquals("12 Lake Road", form.toRequestJson().getString("address"));
    }

    @Test
    public void reportsEveryClientSideProblem() {
        Map<RegistrationForm.Field, FormError> errors = new RegistrationForm(
                "123", "", "not-an-email", "", repeat('a', 201), "short", "different").validate();

        assertEquals(FormError.of(FormError.Issue.INVALID_NIC), errors.get(RegistrationForm.Field.NIC));
        assertEquals(FormError.of(FormError.Issue.REQUIRED), errors.get(RegistrationForm.Field.FULL_NAME));
        assertEquals(FormError.of(FormError.Issue.INVALID_EMAIL), errors.get(RegistrationForm.Field.EMAIL));
        assertEquals(FormError.of(FormError.Issue.REQUIRED), errors.get(RegistrationForm.Field.PHONE));
        assertEquals(FormError.tooLong(200), errors.get(RegistrationForm.Field.ADDRESS));
        assertEquals(FormError.of(FormError.Issue.PASSWORD_TOO_SHORT), errors.get(RegistrationForm.Field.PASSWORD));
        assertEquals(FormError.of(FormError.Issue.PASSWORDS_DO_NOT_MATCH), errors.get(RegistrationForm.Field.CONFIRM_PASSWORD));
    }

    @Test
    public void mapsApiValidationErrorsOntoFields() {
        Map<String, String> apiErrors = new HashMap<>();
        apiErrors.put("Nic", "NIC must be 9 digits followed by V or X, or 12 digits.");
        apiErrors.put("$.email", "Enter a valid email address.");
        apiErrors.put("FullName", "Full name is required.");
        ApiError error = new ApiError(ApiError.Kind.VALIDATION, 400, "One or more validation errors occurred.", apiErrors);

        Map<RegistrationForm.Field, FormError> fields = RegistrationForm.fieldErrorsFrom(error);

        assertEquals(FormError.fromServer("NIC must be 9 digits followed by V or X, or 12 digits."), fields.get(RegistrationForm.Field.NIC));
        assertEquals(FormError.fromServer("Enter a valid email address."), fields.get(RegistrationForm.Field.EMAIL));
        assertEquals(FormError.fromServer("Full name is required."), fields.get(RegistrationForm.Field.FULL_NAME));
    }

    @Test
    public void mapsDuplicateNicAndEmailConflictsToTheRightField() {
        Map<RegistrationForm.Field, FormError> nic = RegistrationForm.fieldErrorsFrom(
                new ApiError(ApiError.Kind.CONFLICT, 409, "A user with this NIC already exists."));
        Map<RegistrationForm.Field, FormError> email = RegistrationForm.fieldErrorsFrom(
                new ApiError(ApiError.Kind.CONFLICT, 409, "A user with this email already exists."));

        assertEquals(setOf(RegistrationForm.Field.NIC), nic.keySet());
        assertEquals(setOf(RegistrationForm.Field.EMAIL), email.keySet());
    }

    @SafeVarargs
    static <T> Set<T> setOf(T... values) {
        Set<T> set = new HashSet<>();
        for (T value : values) set.add(value);
        return set;
    }

    static Set<String> keys(JSONObject json) {
        Set<String> keys = new HashSet<>();
        Iterator<String> iterator = json.keys();
        while (iterator.hasNext()) keys.add(iterator.next());
        return keys;
    }

    static String repeat(char c, int count) {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < count; i++) builder.append(c);
        return builder.toString();
    }
}
