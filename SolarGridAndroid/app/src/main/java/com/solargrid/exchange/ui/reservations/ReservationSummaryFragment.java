package com.solargrid.exchange.ui.reservations;

import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.navigation.NavController;
import androidx.navigation.NavOptions;
import androidx.navigation.Navigation;

import com.solargrid.exchange.R;
import com.solargrid.exchange.data.model.Reservation;

public final class ReservationSummaryFragment extends Fragment {
    static Bundle argumentsFor(String outcome, Reservation reservation) {
        Bundle arguments = new Bundle();
        arguments.putString("outcome", outcome);
        arguments.putString("reservationId", reservation.getId());
        arguments.putString("status", reservation.getStatus());
        arguments.putString("station", ReservationFormatters.station(
                reservation.getStationName(), reservation.getStationId()));
        arguments.putString("slotId", reservation.getSlotId());
        arguments.putString("startUtc", reservation.getScheduledStartTimeUtc());
        arguments.putString("endUtc", reservation.getScheduledEndTimeUtc());
        arguments.putDouble("energy", reservation.getRequestedEnergyKwh());
        return arguments;
    }

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_reservation_summary, container, false);
        Bundle arguments = getArguments() == null ? Bundle.EMPTY : getArguments();
        setText(view, R.id.summary_outcome, arguments.getString("outcome", "Reservation saved"));
        setText(view, R.id.summary_status, arguments.getString("status", ""));
        setText(view, R.id.summary_reference, arguments.getString("reservationId", ""));
        setText(view, R.id.summary_station, arguments.getString("station", ""));
        setText(view, R.id.summary_slot, arguments.getString("slotId", ""));
        setText(view, R.id.summary_time,
                ReservationFormatters.localDateTime(arguments.getString("startUtc", "")) + " - "
                        + ReservationFormatters.localDateTime(arguments.getString("endUtc", "")));
        setText(view, R.id.summary_energy,
                ReservationFormatters.energy(arguments.getDouble("energy", 0)));
        view.findViewById(R.id.summary_done_button).setOnClickListener(ignored -> {
            NavController controller = Navigation.findNavController(view);
            NavOptions options = new NavOptions.Builder()
                    .setPopUpTo(controller.getGraph().getStartDestinationId(), false)
                    .build();
            controller.navigate(R.id.nav_my_reservations, null, options);
        });
        return view;
    }

    private static void setText(View view, int id, String value) {
        ((TextView) view.findViewById(id)).setText(value);
    }
}
