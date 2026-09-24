package com.solargrid.exchange.ui.profile;

import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;

import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.SessionUser;

public final class ProfileFragment extends Fragment {
    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_profile, container, false);
        SessionUser session = ((SolarGridApplication) requireActivity().getApplication())
                .getAppContainer().getSessionStore().read();
        if (session != null) {
            setText(view, R.id.profile_name, session.getFullName());
            setText(view, R.id.profile_nic, session.getNic());
            setText(view, R.id.profile_email,
                    session.getEmail().isEmpty() ? getString(R.string.not_loaded) : session.getEmail());
            setText(view, R.id.profile_role, session.getRole());
            setText(view, R.id.profile_status, session.getStatus());
        }
        return view;
    }

    private static void setText(View view, int id, String value) {
        ((TextView) view.findViewById(id)).setText(value);
    }
}
