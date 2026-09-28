package com.solargrid.exchange.features.users;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertSame;
import static org.junit.Assert.assertTrue;

import com.solargrid.exchange.data.model.ProsumerProfile;
import com.solargrid.exchange.features.users.ProfileController.LoadStatus;
import com.solargrid.exchange.features.users.ProfileController.Notice;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;

import org.junit.Before;
import org.junit.Test;

import java.util.ArrayList;
import java.util.List;

public final class ProfileControllerTest {
    private final List<ApiCallback<ProsumerProfile>> loads = new ArrayList<>();
    private final List<ApiCallback<ProsumerProfile>> updates = new ArrayList<>();
    private final List<ProfileForm> sentForms = new ArrayList<>();
    private final List<ApiCallback<ProsumerProfile>> deactivations = new ArrayList<>();
    private final List<ProsumerProfile> cached = new ArrayList<>();
    private ProfileController controller;

    // Synthetic Prosumer; not a real person.
    static ProsumerProfile profile(String email, boolean requested) {
        return new ProsumerProfile("200000000004", "Test Prosumer", email, "0770000004", null,
                "Prosumer", "Active", requested, requested ? "2026-09-29T08:00:00Z" : null);
    }

    @Before
    public void setUp() {
        ProfileController.Gateway gateway = new ProfileController.Gateway() {
            @Override public void getProfile(ApiCallback<ProsumerProfile> callback) { loads.add(callback); }
            @Override public void updateProfile(ProfileForm form, ApiCallback<ProsumerProfile> callback) {
                sentForms.add(form);
                updates.add(callback);
            }
            @Override public void requestDeactivation(ApiCallback<ProsumerProfile> callback) { deactivations.add(callback); }
        };
        controller = new ProfileController(gateway, cached::add, state -> { });
    }

    private void loadReady(ProsumerProfile profile) {
        controller.load();
        loads.get(loads.size() - 1).onSuccess(profile);
    }

    @Test
    public void loadShowsLoadingThenTheServerProfileAndCachesIt() {
        assertTrue(controller.load());
        assertEquals(LoadStatus.LOADING, controller.getState().getLoadStatus());

        ProsumerProfile server = profile("prosumer@example.com", false);
        loads.get(0).onSuccess(server);

        assertEquals(LoadStatus.READY, controller.getState().getLoadStatus());
        assertSame(server, controller.getState().getProfile());
        assertEquals(1, cached.size());
    }

    @Test
    public void loadIsNotRepeatedWhileInFlight() {
        controller.load();
        assertFalse(controller.load());
        assertEquals(1, loads.size());
    }

    @Test
    public void loadFailuresExposeTheErrorForRetryWithoutCaching() {
        controller.load();
        loads.get(0).onError(new ApiError(ApiError.Kind.NETWORK, 0, "offline"));

        assertEquals(LoadStatus.ERROR, controller.getState().getLoadStatus());
        assertEquals(ApiError.Kind.NETWORK, controller.getState().getLoadError().getKind());
        assertTrue(cached.isEmpty());
        assertTrue(controller.load());
    }

    @Test
    public void expiredSessionIsReportedSoTheScreenCanSignOut() {
        controller.load();
        loads.get(0).onError(new ApiError(ApiError.Kind.UNAUTHORIZED, 401, "expired"));

        assertTrue(controller.getState().getLoadError().isAuthenticationExpired());
    }

    @Test
    public void saveSendsOnlyTheFormUpdatesTheProfileAndCache() {
        loadReady(profile("prosumer@example.com", false));

        assertTrue(controller.save(new ProfileForm("New Name", "prosumer@example.com", "0711111111", "")));
        assertTrue(controller.getState().isSaving());
        updates.get(0).onSuccess(profile("prosumer@example.com", false));

        assertFalse(controller.getState().isSaving());
        assertEquals(Notice.PROFILE_SAVED, controller.getState().getNotice());
        assertEquals(2, cached.size());
    }

    @Test
    public void changedEmailProducesTheNextLoginNotice() {
        loadReady(profile("old@example.com", false));
        controller.save(new ProfileForm("Test Prosumer", "new@example.com", "0770000004", ""));
        updates.get(0).onSuccess(profile("new@example.com", false));

        assertEquals(Notice.PROFILE_SAVED_EMAIL_CHANGED, controller.getState().getNotice());
    }

    @Test
    public void emailCaseChangeIsNotReportedAsANewEmail() {
        loadReady(profile("prosumer@example.com", false));
        controller.save(new ProfileForm("Test Prosumer", "Prosumer@Example.com", "0770000004", ""));
        updates.get(0).onSuccess(profile("prosumer@example.com", false));

        assertEquals(Notice.PROFILE_SAVED, controller.getState().getNotice());
    }

    @Test
    public void repeatedSaveTapsSendOneRequest() {
        loadReady(profile("prosumer@example.com", false));
        ProfileForm form = new ProfileForm("A", "a@b.co", "1", "");

        controller.save(form);
        assertFalse(controller.save(form));
        assertFalse(controller.requestDeactivation());
        assertEquals(1, sentForms.size());
    }

    @Test
    public void invalidEditIsNotSent() {
        loadReady(profile("prosumer@example.com", false));

        assertFalse(controller.save(new ProfileForm("", "bad", "", "")));
        assertTrue(sentForms.isEmpty());
        assertEquals(3, controller.getState().getFieldErrors().size());
    }

    @Test
    public void saveIsIgnoredBeforeTheProfileHasLoaded() {
        assertFalse(controller.save(new ProfileForm("A", "a@b.co", "1", "")));
        assertTrue(sentForms.isEmpty());
    }

    @Test
    public void duplicateEmailConflictIsShownOnTheEmailField() {
        loadReady(profile("prosumer@example.com", false));
        controller.save(new ProfileForm("A", "taken@example.com", "1", ""));
        updates.get(0).onError(new ApiError(ApiError.Kind.CONFLICT, 409, "A user with this email already exists."));

        assertEquals("A user with this email already exists.",
                controller.getState().getFieldErrors().get(ProfileForm.Field.EMAIL).getServerMessage());
        assertEquals("prosumer@example.com", controller.getState().getProfile().getEmail());
        assertEquals(1, cached.size());
    }

    @Test
    public void forbiddenSaveIsSurfacedAsAnActionError() {
        loadReady(profile("prosumer@example.com", false));
        controller.save(new ProfileForm("A", "a@b.co", "1", ""));
        updates.get(0).onError(new ApiError(ApiError.Kind.FORBIDDEN, 403, "This account is not active. Please contact Backoffice."));

        assertEquals(ApiError.Kind.FORBIDDEN, controller.getState().getActionError().getKind());
        assertFalse(controller.getState().isBusy());
    }

    @Test
    public void deactivationRequestUpdatesStateFromTheServerResponse() {
        loadReady(profile("prosumer@example.com", false));
        assertTrue(controller.getState().canRequestDeactivation());

        assertTrue(controller.requestDeactivation());
        assertTrue(controller.getState().isRequestingDeactivation());
        deactivations.get(0).onSuccess(profile("prosumer@example.com", true));

        assertEquals(Notice.DEACTIVATION_REQUESTED, controller.getState().getNotice());
        assertTrue(controller.getState().getProfile().isDeactivationRequested());
        assertEquals("Active", controller.getState().getProfile().getStatus());
        assertFalse(controller.getState().canRequestDeactivation());
    }

    @Test
    public void repeatedDeactivationTapsSendOneRequest() {
        loadReady(profile("prosumer@example.com", false));

        controller.requestDeactivation();
        assertFalse(controller.requestDeactivation());
        assertEquals(1, deactivations.size());
    }

    @Test
    public void alreadyPendingRequestIsNotSentAgain() {
        loadReady(profile("prosumer@example.com", true));

        assertFalse(controller.requestDeactivation());
        assertTrue(deactivations.isEmpty());
    }

    @Test
    public void duplicateRequestConflictShowsPendingAndReloadsTheProfile() {
        loadReady(profile("prosumer@example.com", false));
        controller.requestDeactivation();
        deactivations.get(0).onError(new ApiError(ApiError.Kind.CONFLICT, 409, "A deactivation request is already pending for this account."));

        assertEquals(Notice.DEACTIVATION_ALREADY_PENDING, controller.getState().getNotice());
        assertNull(controller.getState().getActionError());
        assertEquals(2, loads.size());
        loads.get(1).onSuccess(profile("prosumer@example.com", true));
        assertTrue(controller.getState().getProfile().isDeactivationRequested());
    }

    @Test
    public void serverFailureOnDeactivationRequestIsReported() {
        loadReady(profile("prosumer@example.com", false));
        controller.requestDeactivation();
        deactivations.get(0).onError(new ApiError(ApiError.Kind.SERVER, 500, "An unexpected error occurred."));

        assertEquals(ApiError.Kind.SERVER, controller.getState().getActionError().getKind());
        assertFalse(controller.getState().getProfile().isDeactivationRequested());
        assertTrue(controller.getState().canRequestDeactivation());
    }
}
