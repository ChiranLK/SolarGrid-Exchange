package com.solargrid.exchange.ui.home;

import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;

import com.solargrid.exchange.R;

public final class ProsumerHomeFragment extends Fragment {
    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_home, container, false);
        ((TextView) view.findViewById(R.id.home_title)).setText(R.string.prosumer_home_title);
        ((TextView) view.findViewById(R.id.home_description)).setText(R.string.prosumer_home_description);
        return view;
    }
}
