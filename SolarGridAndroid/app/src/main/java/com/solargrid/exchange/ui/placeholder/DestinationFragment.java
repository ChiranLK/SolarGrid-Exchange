package com.solargrid.exchange.ui.placeholder;

import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;

import com.solargrid.exchange.R;

public final class DestinationFragment extends Fragment {
    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_placeholder, container, false);
        Bundle arguments = getArguments();
        String title = arguments == null ? null : arguments.getString("title");
        String description = arguments == null ? null : arguments.getString("description");
        ((TextView) view.findViewById(R.id.placeholder_title)).setText(
                title == null ? getString(R.string.feature_pending) : title);
        ((TextView) view.findViewById(R.id.placeholder_description)).setText(
                description == null ? getString(R.string.feature_pending_description) : description);
        return view;
    }
}
