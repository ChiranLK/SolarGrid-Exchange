package com.solargrid.exchange.ui.profile;

import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.core.content.ContextCompat;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;

import com.google.android.material.dialog.MaterialAlertDialogBuilder;
import com.google.android.material.textfield.TextInputLayout;
import com.solargrid.exchange.R;
import com.solargrid.exchange.ui.common.DisplayFormats;
import com.solargrid.exchange.ui.common.StatusStyles;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.ProsumerProfile;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.features.auth.AccountRoutePolicy;
import com.solargrid.exchange.features.users.ProfileController;
import com.solargrid.exchange.features.users.ProfileForm;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.auth.FormErrorText;
import com.solargrid.exchange.ui.common.UiStateView;

import java.text.DateFormat;
import java.text.ParseException;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.EnumMap;
import java.util.Locale;
import java.util.Map;
import java.util.TimeZone;

/**
 * Prosumers: server-authoritative profile (GET/PUT /api/prosumers/me) and deactivation request.
 * Staff: the existing read-only summary of the local session, unchanged.
 */
public final class ProfileFragment extends Fragment {
    private final Map<ProfileForm.Field, TextInputLayout> fields = new EnumMap<>(ProfileForm.Field.class);
    private ProfileViewModel viewModel;

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_profile, container, false);
        view.findViewById(R.id.profile_sign_out)
                .setOnClickListener(ignored -> ((MainActivity) requireActivity()).requestSignOut());
        SessionUser session = ((SolarGridApplication) requireActivity().getApplication())
                .getAppContainer().getSessionStore().read();
        if (session == null) {
            ((MainActivity) requireActivity()).handleAuthenticationExpiry();
            return view;
        }
        if (!session.isProsumer()) {
            bindStaffSummary(view, session);
            return view;
        }

        fields.put(ProfileForm.Field.FULL_NAME, view.findViewById(R.id.profile_full_name_layout));
        fields.put(ProfileForm.Field.EMAIL, view.findViewById(R.id.profile_edit_email_layout));
        fields.put(ProfileForm.Field.PHONE, view.findViewById(R.id.profile_phone_layout));
        fields.put(ProfileForm.Field.ADDRESS, view.findViewById(R.id.profile_address_layout));

        viewModel = new ViewModelProvider(this).get(ProfileViewModel.class);
        view.findViewById(R.id.profile_save).setOnClickListener(ignored -> viewModel.save(readForm(view)));
        view.findViewById(R.id.profile_request_deactivation).setOnClickListener(ignored -> confirmDeactivation());
        viewModel.getState().observe(getViewLifecycleOwner(), state -> render(view, state));
        viewModel.loadIfNeeded();
        return view;
    }

    private void render(View view, ProfileController.State state) {
        UiStateView stateView = view.findViewById(R.id.profile_state);
        View content = view.findViewById(R.id.profile_prosumer_section);
        ProsumerProfile profile = state.getProfile();

        ApiError loadError = state.getLoadError();
        if (loadError != null && handleSessionProblem(loadError)) {
            return;
        }
        ApiError actionError = state.getActionError();
        if (actionError != null && handleSessionProblem(actionError)) {
            return;
        }

        if (profile == null) {
            content.setVisibility(View.GONE);
            if (state.getLoadStatus() == ProfileController.LoadStatus.ERROR && loadError != null) {
                // Covers offline (NETWORK), server errors and malformed responses, each with a retry.
                stateView.showError(loadError, ignored -> viewModel.retry());
            } else {
                stateView.showLoading(getString(R.string.loading_profile));
            }
            return;
        }

        stateView.hide();
        content.setVisibility(View.VISIBLE);
        bindProfile(view, profile);
        if (viewModel.shouldBindForm(profile)) {
            setText(view, R.id.profile_full_name, profile.getFullName());
            setText(view, R.id.profile_edit_email, profile.getEmail());
            setText(view, R.id.profile_phone, profile.getPhone());
            setText(view, R.id.profile_address, profile.getAddress() == null ? "" : profile.getAddress());
        }

        for (Map.Entry<ProfileForm.Field, TextInputLayout> entry : fields.entrySet()) {
            entry.getValue().setEnabled(!state.isBusy());
            FormErrorText.apply(entry.getValue(), state.getFieldErrors().get(entry.getKey()));
        }

        Button save = view.findViewById(R.id.profile_save);
        save.setEnabled(!state.isBusy());
        save.setText(state.isSaving() ? R.string.profile_saving : R.string.profile_save);

        Button deactivate = view.findViewById(R.id.profile_request_deactivation);
        deactivate.setEnabled(state.canRequestDeactivation());
        deactivate.setText(state.isRequestingDeactivation() ? R.string.requesting_deactivation : R.string.request_deactivation);

        renderMessage(view, state);
    }

    /** Handles 401 (sign in again) and pending/deactivated 403s; returns true when navigating away. */
    private boolean handleSessionProblem(ApiError error) {
        switch (AccountRoutePolicy.afterSessionFailure(error)) {
            case SIGN_IN:
                ((MainActivity) requireActivity()).handleAuthenticationExpiry();
                return true;
            case PENDING_ACTIVATION:
            case SIGN_IN_WITH_ACCOUNT_NOTICE:
                ((MainActivity) requireActivity()).handleAccountNoLongerActive(error);
                return true;
            default:
                return false;
        }
    }

    private void bindProfile(View view, ProsumerProfile profile) {
        setText(view, R.id.profile_prosumer_name, profile.getFullName());
        // Display only: avatar initials and a masked NIC; the canonical NIC is untouched.
        setText(view, R.id.profile_prosumer_initials, DisplayFormats.initials(profile.getFullName()));
        setText(view, R.id.profile_prosumer_nic, DisplayFormats.maskNic(profile.getNic()));
        setText(view, R.id.profile_prosumer_status, profile.getStatus());
        StatusStyles.apply(view.findViewById(R.id.profile_prosumer_status), profile.getStatus());
        String deactivationState;
        if (!profile.isDeactivationRequested()) {
            deactivationState = getString(R.string.deactivation_state_none);
        } else {
            String requestedAt = formatUtc(profile.getDeactivationRequestedAtUtc());
            deactivationState = requestedAt == null
                    ? getString(R.string.deactivation_state_pending_no_date)
                    : getString(R.string.deactivation_state_pending, requestedAt);
        }
        setText(view, R.id.profile_deactivation_state, deactivationState);
    }

    private void renderMessage(View view, ProfileController.State state) {
        TextView message = view.findViewById(R.id.profile_message);
        String text = null;
        boolean isError = false;
        switch (state.getNotice()) {
            case PROFILE_SAVED:
                text = getString(R.string.profile_saved);
                break;
            case PROFILE_SAVED_EMAIL_CHANGED:
                text = getString(R.string.profile_saved_email_changed);
                break;
            case DEACTIVATION_REQUESTED:
                text = getString(R.string.deactivation_requested);
                break;
            case DEACTIVATION_ALREADY_PENDING:
                text = getString(R.string.deactivation_already_pending);
                break;
            default:
                break;
        }
        if (text == null && state.getActionError() != null) {
            isError = true;
            text = state.getFieldErrors().isEmpty()
                    ? getString(R.string.profile_save_failed, state.getActionError().getMessage())
                    : getString(R.string.form_has_errors);
        } else if (text == null && !state.getFieldErrors().isEmpty()) {
            isError = true;
            text = getString(R.string.form_has_errors);
        }

        if (text == null) {
            message.setVisibility(View.GONE);
            return;
        }
        message.setText(text);
        // Presentation only: semantic banner for the same error/success decision made above.
        message.setTextColor(ContextCompat.getColor(requireContext(),
                isError ? R.color.sg_on_error_container : R.color.sg_on_approved_container));
        message.setBackgroundResource(isError ? R.drawable.sg_bg_banner_error : R.drawable.sg_bg_banner_success);
        message.setVisibility(View.VISIBLE);
        message.setOnClickListener(ignored -> viewModel.dismissMessages());
    }

    private void confirmDeactivation() {
        new MaterialAlertDialogBuilder(requireContext())
                .setTitle(R.string.deactivation_confirm_title)
                .setMessage(R.string.deactivation_confirm_message)
                .setNegativeButton(R.string.cancel, null)
                .setPositiveButton(R.string.deactivation_confirm_action, (dialog, which) -> viewModel.requestDeactivation())
                .show();
    }

    private ProfileForm readForm(View view) {
        return new ProfileForm(
                text(view, R.id.profile_full_name),
                text(view, R.id.profile_edit_email),
                text(view, R.id.profile_phone),
                text(view, R.id.profile_address));
    }

    private void bindStaffSummary(View view, SessionUser session) {
        view.findViewById(R.id.profile_staff_section).setVisibility(View.VISIBLE);
        setText(view, R.id.profile_name, session.getFullName());
        // Display only: header, masked NIC and readable role label.
        setText(view, R.id.profile_staff_initials, DisplayFormats.initials(session.getFullName()));
        setText(view, R.id.profile_staff_role_subtitle, DisplayFormats.roleLabel(requireContext(), session.getRole()));
        setText(view, R.id.profile_staff_name_row, session.getFullName());
        setText(view, R.id.profile_nic, DisplayFormats.maskNic(session.getNic()));
        setText(view, R.id.profile_email,
                session.getEmail().isEmpty() ? getString(R.string.not_loaded) : session.getEmail());
        setText(view, R.id.profile_role, DisplayFormats.roleLabel(requireContext(), session.getRole()));
        setText(view, R.id.profile_status, session.getStatus());
        StatusStyles.apply(view.findViewById(R.id.profile_status), session.getStatus());
    }

    @Nullable
    private static String formatUtc(@Nullable String isoUtc) {
        if (isoUtc == null) {
            return null;
        }
        String trimmed = isoUtc.length() >= 19 ? isoUtc.substring(0, 19) : isoUtc;
        SimpleDateFormat parser = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.ROOT);
        parser.setTimeZone(TimeZone.getTimeZone("UTC"));
        try {
            Date date = parser.parse(trimmed);
            return date == null ? null : DateFormat.getDateTimeInstance(DateFormat.MEDIUM, DateFormat.SHORT).format(date);
        } catch (ParseException exception) {
            return null;
        }
    }

    private static void setText(View view, int id, String value) {
        ((TextView) view.findViewById(id)).setText(value);
    }

    private static String text(View view, int id) {
        CharSequence value = ((EditText) view.findViewById(id)).getText();
        return value == null ? "" : value.toString();
    }
}
