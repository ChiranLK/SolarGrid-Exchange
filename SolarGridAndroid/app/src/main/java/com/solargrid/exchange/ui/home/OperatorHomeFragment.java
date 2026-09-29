package com.solargrid.exchange.ui.home;

import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.Navigation;

import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.operations.OperatorTransactionViewModel;

public final class OperatorHomeFragment extends Fragment {
    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_operator_home, container, false);
        SessionUser session = ((SolarGridApplication) requireActivity().getApplication())
                .getAppContainer()
                .getAuthRepository()
                .getStoredSession();
        if (session == null) {
            ((MainActivity) requireActivity()).handleAuthenticationExpiry();
            return view;
        }
        if (!session.isGridOperator()) {
            Navigation.findNavController(requireActivity(), R.id.main_nav_host)
                    .navigate(R.id.nav_profile);
            return view;
        }

        OperatorTransactionViewModel flow = new ViewModelProvider(requireActivity())
                .get(OperatorTransactionViewModel.class);
        view.findViewById(R.id.operator_start_scanner).setOnClickListener(ignored -> {
            flow.prepareForScan();
            Navigation.findNavController(view).navigate(R.id.nav_qr_operations);
        });
        return view;
    }
}
