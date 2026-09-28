package com.solargrid.exchange.features.auth;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;

import org.junit.Before;
import org.junit.Test;

import java.util.ArrayList;
import java.util.List;

public final class RegistrationControllerTest {
    private final List<RegistrationForm> sent = new ArrayList<>();
    private final List<RegistrationController.State> states = new ArrayList<>();
    private ApiCallback<String> pending;
    private RegistrationController controller;

    @Before
    public void setUp() {
        // The fake gateway records requests and lets each test decide the API response.
        controller = new RegistrationController((form, callback) -> {
            sent.add(form);
            pending = callback;
        }, states::add);
    }

    @Test
    public void successfulRegistrationRoutesToPendingWithoutASession() {
        assertTrue(controller.submit(RegistrationFormTest.valid()));
        assertTrue(controller.getState().isSubmitting());

        pending.onSuccess("prosumer@example.com");

        assertTrue(controller.getState().isRegistered());
        assertEquals("prosumer@example.com", controller.getState().getRegisteredEmail());
        assertFalse(controller.getState().isSubmitting());
        assertNull(controller.getState().getError());
    }

    @Test
    public void repeatedTapsWhileSendingSendOnlyOneRequest() {
        controller.submit(RegistrationFormTest.valid());
        assertFalse(controller.submit(RegistrationFormTest.valid()));
        assertFalse(controller.submit(RegistrationFormTest.valid()));

        assertEquals(1, sent.size());
    }

    @Test
    public void cannotRegisterTwiceAfterSuccess() {
        controller.submit(RegistrationFormTest.valid());
        pending.onSuccess("prosumer@example.com");

        assertFalse(controller.submit(RegistrationFormTest.valid()));
        assertEquals(1, sent.size());
    }

    @Test
    public void invalidFormIsNotSent() {
        assertFalse(controller.submit(new RegistrationForm("", "", "", "", "", "", "")));

        assertTrue(sent.isEmpty());
        assertFalse(controller.getState().getFieldErrors().isEmpty());
        assertFalse(controller.getState().isSubmitting());
    }

    @Test
    public void duplicateRegistrationShowsTheConflictOnTheFieldAndAllowsRetry() {
        controller.submit(RegistrationFormTest.valid());
        pending.onError(new ApiError(ApiError.Kind.CONFLICT, 409, "A user with this email already exists."));

        RegistrationController.State state = controller.getState();
        assertFalse(state.isRegistered());
        assertEquals(ApiError.Kind.CONFLICT, state.getError().getKind());
        assertEquals("A user with this email already exists.",
                state.getFieldErrors().get(RegistrationForm.Field.EMAIL).getServerMessage());
        assertTrue(controller.submit(RegistrationFormTest.valid()));
        assertEquals(2, sent.size());
    }

    @Test
    public void networkAndServerFailuresKeepTheFormForRetry() {
        controller.submit(RegistrationFormTest.valid());
        pending.onError(new ApiError(ApiError.Kind.NETWORK, 0, "Unable to reach the SolarGrid API."));

        assertEquals(ApiError.Kind.NETWORK, controller.getState().getError().getKind());
        assertTrue(controller.getState().getFieldErrors().isEmpty());
        assertTrue(controller.submit(RegistrationFormTest.valid()));
    }

    @Test
    public void publishesSubmittingThenResult() {
        controller.submit(RegistrationFormTest.valid());
        pending.onSuccess("prosumer@example.com");

        assertEquals(2, states.size());
        assertTrue(states.get(0).isSubmitting());
        assertTrue(states.get(1).isRegistered());
    }
}
