package com.solargrid.exchange.network;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

import java.util.Map;

public final class ApiErrorFieldErrorsTest {
    @Test
    public void extractsTheFirstMessagePerFieldFromAValidationProblem() {
        Map<String, String> errors = ApiClient.extractFieldErrors(
                "{\"title\":\"One or more validation errors occurred.\",\"status\":400,"
                        + "\"errors\":{\"Nic\":[\"NIC is required.\",\"second\"],\"$.email\":[\"Bad email.\"]}}");

        ApiError error = new ApiError(ApiError.Kind.VALIDATION, 400, "x", errors);

        assertEquals("NIC is required.", error.getFieldError("nic"));
        assertEquals("NIC is required.", error.getFieldError("NIC"));
        assertEquals("Bad email.", error.getFieldError("email"));
        assertNull(error.getFieldError("phone"));
    }

    @Test
    public void sharedErrorBodiesAndNonJsonHaveNoFieldErrors() {
        assertTrue(ApiClient.extractFieldErrors("{\"status\":409,\"message\":\"Conflict\"}").isEmpty());
        assertTrue(ApiClient.extractFieldErrors("<html>502</html>").isEmpty());
        assertTrue(ApiClient.extractFieldErrors("").isEmpty());
        assertTrue(ApiClient.extractFieldErrors(null).isEmpty());
    }

    @Test
    public void existingConstructorKeepsWorkingWithoutFieldErrors() {
        ApiError error = new ApiError(ApiError.Kind.UNAUTHORIZED, 401, "expired");

        assertTrue(error.getFieldErrors().isEmpty());
        assertTrue(error.isAuthenticationExpired());
    }
}
