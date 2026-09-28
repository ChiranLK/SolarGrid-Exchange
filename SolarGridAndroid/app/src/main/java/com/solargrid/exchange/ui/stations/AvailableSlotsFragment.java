package com.solargrid.exchange.ui.stations;

import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.ListView;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;

import com.solargrid.exchange.R;
import com.solargrid.exchange.features.stations.AvailableSlotPage;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;

public final class AvailableSlotsFragment extends Fragment {
    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        return inflater.inflate(R.layout.fragment_available_slots, container, false);
    }

    @Override
    public void onViewCreated(@NonNull View view, @Nullable Bundle savedInstanceState) {
        super.onViewCreated(view, savedInstanceState);
        ListView list = view.findViewById(R.id.available_slot_list);
        UiStateView stateView = view.findViewById(R.id.available_slot_state);
        View pagination = view.findViewById(R.id.available_slot_pagination);
        TextView pageLabel = view.findViewById(R.id.available_slot_page_label);
        Button previous = view.findViewById(R.id.available_slot_previous);
        Button next = view.findViewById(R.id.available_slot_next);
        AvailableSlotsViewModel viewModel = new ViewModelProvider(this)
                .get(AvailableSlotsViewModel.class);

        previous.setOnClickListener(ignored -> viewModel.previousPage());
        next.setOnClickListener(ignored -> viewModel.nextPage());
        viewModel.getState().observe(getViewLifecycleOwner(), state -> {
            list.setVisibility(state.getStatus() == UiState.Status.SUCCESS ? View.VISIBLE : View.GONE);
            pagination.setVisibility(state.getStatus() == UiState.Status.SUCCESS ? View.VISIBLE : View.GONE);
            switch (state.getStatus()) {
                case LOADING:
                    stateView.showLoading(getString(R.string.loading_available_slots));
                    break;
                case EMPTY:
                    stateView.showEmpty(getString(R.string.no_available_slots),
                            getString(R.string.no_available_slots_review_message),
                            ignored -> viewModel.refresh());
                    break;
                case ERROR:
                    if (state.getError() != null && state.getError().isAuthenticationExpired()) {
                        ((MainActivity) requireActivity()).handleAuthenticationExpiry();
                    } else if (state.getError() != null) {
                        stateView.showError(state.getError(), ignored -> viewModel.refresh());
                    }
                    break;
                case SUCCESS:
                    stateView.hide();
                    AvailableSlotPage page = state.getData();
                    if (page == null) break;
                    list.setAdapter(new SlotAdapter(requireContext(), page.getItems()));
                    pageLabel.setText(getString(R.string.available_slot_page_format,
                            page.getPage(), page.getTotalPages()));
                    previous.setEnabled(page.getPage() > 1);
                    next.setEnabled(page.getPage() < page.getTotalPages());
                    break;
                default:
                    break;
            }
        });
        String stationId = getArguments() == null ? "" : getArguments().getString("stationId", "");
        viewModel.load(stationId);
    }
}
