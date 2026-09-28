package com.solargrid.exchange.features.users;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import com.solargrid.exchange.data.model.ProsumerProfile;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.features.auth.FormError;
import com.solargrid.exchange.network.ApiError;

import org.json.JSONObject;
import org.junit.Test;

import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.Map;
import java.util.Set;

/** Request/response mapping for /api/prosumers/me and the permitted session-cache update. */
public final class ProfileContractTest {
    @Test
    public void updatePayloadContainsOnlyTheFourAllowedFields() throws Exception {
        JSONObject body = new ProfileForm(" New Name ", " new@example.com ", " 0711 ", " 1 Road ").toRequestJson();

        Set<String> keys = new HashSet<>();
        Iterator<String> iterator = body.keys();
        while (iterator.hasNext()) keys.add(iterator.next());
        Set<String> expected = new HashSet<>();
        expected.add("fullName");
        expected.add("email");
        expected.add("phone");
        expected.add("address");
        assertEquals(expected, keys);
        assertEquals("New Name", body.getString("fullName"));
        assertEquals("new@example.com", body.getString("email"));
        assertEquals("1 Road", body.getString("address"));
        for (String forbidden : new String[]{"nic", "role", "status", "passwordHash", "assignedStationId", "deactivationRequested"}) {
            assertFalse(forbidden + " must never be sent", body.has(forbidden));
        }
    }

    @Test
    public void blankAddressIsSentAsNullToClearIt() throws Exception {
        assertTrue(new ProfileForm("A", "a@b.co", "1", "   ").toRequestJson().isNull("address"));
    }

    @Test
    public void profileFormValidationMatchesTheApiLimits() {
        Map<ProfileForm.Field, FormError> errors = new ProfileForm(
                repeat(101), "a@b", repeat(21), repeat(201)).validate();

        assertEquals(FormError.tooLong(100), errors.get(ProfileForm.Field.FULL_NAME));
        assertEquals(FormError.of(FormError.Issue.INVALID_EMAIL), errors.get(ProfileForm.Field.EMAIL));
        assertEquals(FormError.tooLong(20), errors.get(ProfileForm.Field.PHONE));
        assertEquals(FormError.tooLong(200), errors.get(ProfileForm.Field.ADDRESS));
    }

    @Test
    public void apiFieldErrorsMapOntoProfileFields() {
        Map<String, String> apiErrors = new HashMap<>();
        apiErrors.put("Phone", "Phone is required.");
        ApiError error = new ApiError(ApiError.Kind.VALIDATION, 400, "One or more validation errors occurred.", apiErrors);

        assertEquals(FormError.fromServer("Phone is required."),
                ProfileForm.fieldErrorsFrom(error).get(ProfileForm.Field.PHONE));
    }

    @Test
    public void mapsTheProfileResponse() throws Exception {
        ProsumerProfile profile = ProsumerProfile.fromJson(new JSONObject(
                "{\"nic\":\"200000000004\",\"fullName\":\"Test Prosumer\",\"email\":\"p@example.com\","
                        + "\"phone\":\"0770000004\",\"address\":\"1 Road\",\"role\":\"Prosumer\",\"status\":\"Active\","
                        + "\"deactivationRequested\":true,\"deactivationRequestedAtUtc\":\"2026-09-29T08:00:00Z\","
                        + "\"createdAtUtc\":\"2026-09-01T00:00:00Z\",\"updatedAtUtc\":\"2026-09-29T08:00:00Z\"}"));

        assertEquals("200000000004", profile.getNic());
        assertEquals("1 Road", profile.getAddress());
        assertTrue(profile.isActive());
        assertTrue(profile.isDeactivationRequested());
        assertEquals("2026-09-29T08:00:00Z", profile.getDeactivationRequestedAtUtc());
    }

    @Test
    public void nullOptionalFieldsAreMappedAsAbsent() throws Exception {
        ProsumerProfile profile = ProsumerProfile.fromJson(new JSONObject(
                "{\"nic\":\"200000000004\",\"fullName\":\"T\",\"email\":\"p@example.com\",\"phone\":\"1\","
                        + "\"address\":null,\"status\":\"Active\",\"deactivationRequested\":false,"
                        + "\"deactivationRequestedAtUtc\":null}"));

        assertNull(profile.getAddress());
        assertNull(profile.getDeactivationRequestedAtUtc());
        assertFalse(profile.isDeactivationRequested());
    }

    @Test
    public void malformedProfileResponsesAreRejected() throws Exception {
        assertNull(ProsumerProfile.fromJson(null));
        assertNull(ProsumerProfile.fromJson(new JSONObject("{}")));
        assertNull(ProsumerProfile.fromJson(new JSONObject("{\"nic\":\"x\",\"fullName\":\"y\",\"email\":\"z\"}")));
    }

    @Test
    public void sessionCacheUpdateKeepsTokenNicAndRole() {
        SessionUser stored = new SessionUser("server-issued-token", "200000000004", "Old Name", "old@example.com", "Prosumer", "Active");

        SessionUser updated = stored.withProfile("New Name", "new@example.com", "Active");

        assertEquals("server-issued-token", updated.getToken());
        assertEquals("200000000004", updated.getNic());
        assertEquals("Prosumer", updated.getRole());
        assertEquals("New Name", updated.getFullName());
        assertEquals("new@example.com", updated.getEmail());
    }

    @Test
    public void usesTheMember1ProfileEndpoints() {
        assertEquals("prosumers/me", UserRepository.PROFILE_PATH);
        assertEquals("prosumers/me/deactivation-request", UserRepository.DEACTIVATION_REQUEST_PATH);
    }

    private static String repeat(int count) {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < count; i++) builder.append('a');
        return builder.toString();
    }
}
