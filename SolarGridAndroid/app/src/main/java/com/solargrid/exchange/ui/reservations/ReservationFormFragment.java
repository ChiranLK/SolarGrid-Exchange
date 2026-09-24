package com.solargrid.exchange.ui.reservations;

import android.os.Bundle;
import android.text.InputType;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.NavOptions;
import androidx.navigation.Navigation;

import com.solargrid.exchange.R;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.data.model.Slot;
import com.solargrid.exchange.features.reservations.ReservationFormData;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;

import java.util.ArrayList;
import java.util.List;

public final class ReservationFormFragment extends Fragment {
    private ReservationFormViewModel viewModel;
    private ReservationFormData formData;
    private List<Slot> displayedSlots = new ArrayList<>();

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_reservation_form, container, false);
        UiStateView stateView = view.findViewById(R.id.reservation_form_state);
        View content = view.findViewById(R.id.reservation_form_content);
        Spinner slotSpinner = view.findViewById(R.id.reservation_slot_spinner);
        EditText energy = view.findViewById(R.id.reservation_energy_input);
        Button submit = view.findViewById(R.id.reservation_submit_button);
        energy.setInputType(InputType.TYPE_CLASS_NUMBER | InputType.TYPE_NUMBER_FLAG_DECIMAL);
        viewModel = new ViewModelProvider(this).get(ReservationFormViewModel.class);

        viewModel.getFormState().observe(getViewLifecycleOwner(), state -> {
            content.setVisibility(state.getStatus() == UiState.Status.SUCCESS ? View.VISIBLE : View.GONE);
            switch (state.getStatus()) {
                case LOADING:
                    stateView.showLoading(getString(R.string.loading_reservation_form));
                    break;
                case ERROR:
                    if (state.getError() != null && state.getError().isAuthenticationExpired()) {
                        ((MainActivity) requireActivity()).handleAuthenticationExpiry();
                    } else if (state.getError() != null) {
                        stateView.showError(state.getError(), ignored -> viewModel.retry());
                    }
                    break;
                case SUCCESS:
                    stateView.hide();
                    formData = state.getData();
                    bindForm(view, slotSpinner, energy, submit, formData);
                    break;
                default:
                    break;
            }
        });

        viewModel.getMutationState().observe(getViewLifecycleOwner(), state -> {
            submit.setEnabled(state.getStatus() != UiState.Status.LOADING);
            if (state.getStatus() == UiState.Status.LOADING) {
                submit.setText(R.string.saving_reservation);
            } else if (formData != null) {
                submit.setText(formData.isEditing()
                        ? R.string.update_reservation
                        : R.string.confirm_booking);
            }

            if (state.getStatus() == UiState.Status.ERROR && state.getError() != null) {
                if (state.getError().isAuthenticationExpired()) {
                    ((MainActivity) requireActivity()).handleAuthenticationExpiry();
                } else {
                    Toast.makeText(requireContext(), state.getError().getMessage(), Toast.LENGTH_LONG).show();
                }
                viewModel.consumeMutation();
            } else if (state.getStatus() == UiState.Status.SUCCESS && state.getData() != null) {
                Reservation result = state.getData();
                String outcome = formData != null && formData.isEditing()
                        ? "Reservation updated"
                        : "Booking request created";
                viewModel.consumeMutation();
                int popTarget = formData != null && formData.isEditing()
                        ? R.id.nav_reservation_detail
                        : R.id.nav_reservation_form;
                NavOptions options = new NavOptions.Builder()
                        .setPopUpTo(popTarget, true)
                        .build();
                Navigation.findNavController(view).navigate(
                        R.id.nav_reservation_summary,
                        ReservationSummaryFragment.argumentsFor(outcome, result),
                        options);
            }
        });

        submit.setOnClickListener(ignored -> {
            int position = slotSpinner.getSelectedItemPosition();
            if (position < 0 || position >= displayedSlots.size()) {
                Toast.makeText(requireContext(), R.string.select_slot_error, Toast.LENGTH_SHORT).show();
                return;
            }
            double requestedEnergy;
            try {
                requestedEnergy = Double.parseDouble(energy.getText().toString().trim());
            } catch (NumberFormatException exception) {
                energy.setError(getString(R.string.energy_required));
                return;
            }
            viewModel.submit(displayedSlots.get(position), requestedEnergy);
        });

        Bundle arguments = getArguments() == null ? Bundle.EMPTY : getArguments();
        if ("edit".equals(arguments.getString("mode", "create"))) {
            viewModel.configureEdit(arguments.getString("reservationId", ""));
        } else {
            viewModel.configureCreate(
                    arguments.getString("stationName", ""),
                    arguments.getString("stationId", ""),
                    arguments.getString("slotId", ""),
                    arguments.getString("startUtc", ""),
                    arguments.getString("endUtc", ""),
                    arguments.getDouble("availableCapacity", 0));
        }
        return view;
    }

    private void bindForm(
            View view,
            Spinner spinner,
            EditText energy,
            Button submit,
            ReservationFormData data) {
        if (data == null) {
            return;
        }
        ((TextView) view.findViewById(R.id.reservation_form_title)).setText(
                data.isEditing() ? R.string.modify_reservation : R.string.create_booking);
        ((TextView) view.findViewById(R.id.reservation_form_station)).setText(data.getStationName());
        ((TextView) view.findViewById(R.id.reservation_authority_notice)).setText(
                data.isEditing()
                        ? R.string.update_authority_notice
                        : R.string.booking_authority_notice);
        submit.setText(data.isEditing() ? R.string.update_reservation : R.string.confirm_booking);
        displayedSlots = data.getSlots();
        List<String> labels = new ArrayList<>();
        int selected = 0;
        for (int index = 0; index < displayedSlots.size(); index++) {
            Slot slot = displayedSlots.get(index);
            String slotDetail = "Current reservation".equals(slot.getAvailabilityStatus())
                    ? getString(R.string.current_slot_revalidation)
                    : ReservationFormatters.energy(slot.getAvailableCapacityKwh()) + " · "
                            + slot.getAvailabilityStatus();
            labels.add(ReservationFormatters.localDateTime(slot.getStartTimeUtc()) + " - "
                    + ReservationFormatters.localDateTime(slot.getEndTimeUtc()) + "\n"
                    + slotDetail);
            if (data.getReservation() != null
                    && data.getReservation().getSlotId().equals(slot.getId())) {
                selected = index;
            }
        }
        ArrayAdapter<String> adapter = new ArrayAdapter<>(
                requireContext(), android.R.layout.simple_spinner_item, labels);
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        spinner.setAdapter(adapter);
        spinner.setSelection(selected);
        if (data.getReservation() != null && energy.getText().length() == 0) {
            energy.setText(String.valueOf(data.getReservation().getRequestedEnergyKwh()));
        }
    }
}
