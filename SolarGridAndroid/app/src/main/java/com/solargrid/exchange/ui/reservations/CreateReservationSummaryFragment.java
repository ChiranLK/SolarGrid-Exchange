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

public final class CreateReservationSummaryFragment extends Fragment {
    static Bundle argumentsFor(Reservation reservation) {
        Bundle arguments = new Bundle();
        arguments.putString("reservationId", reservation.getId());
        arguments.putString("station", ReservationFormatters.station(
                reservation.getStationName(), reservation.getStationId()));
        arguments.putString("startUtc", reservation.getScheduledStartTimeUtc());
        arguments.putString("endUtc", reservation.getScheduledEndTimeUtc());
        arguments.putDouble("energy", reservation.getRequestedEnergyKwh());
        arguments.putString("status", reservation.getStatus());
        return arguments;
    }

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_create_reservation_summary, container, false);
        Bundle arguments = getArguments() == null ? Bundle.EMPTY : getArguments();
        String reservationId = arguments.getString("reservationId", "");

        setText(view, R.id.create_summary_action, getString(R.string.reservation_created));
        setText(view, R.id.create_summary_reference, reservationId);
        setText(view, R.id.create_summary_station, arguments.getString("station", ""));
        setText(view, R.id.create_summary_date,
                ReservationFormatters.localDate(arguments.getString("startUtc", "")));
        setText(view, R.id.create_summary_time,
                ReservationFormatters.localTimeRange(
                        arguments.getString("startUtc", ""),
                        arguments.getString("endUtc", "")));
        setText(view, R.id.create_summary_quantity,
                ReservationFormatters.energy(arguments.getDouble("energy", 0)));
        setText(view, R.id.create_summary_status, arguments.getString("status", ""));

        view.findViewById(R.id.create_summary_view_detail).setOnClickListener(ignored -> {
            Bundle detail = new Bundle();
            detail.putString("reservationId", reservationId);
            Navigation.findNavController(view).navigate(R.id.nav_reservation_detail, detail);
        });
        view.findViewById(R.id.create_summary_view_pending).setOnClickListener(ignored -> {
            Bundle pending = new Bundle();
            pending.putString("defaultView", "Pending");
            navigateFromSummary(view, R.id.nav_my_reservations, pending);
        });
        view.findViewById(R.id.create_summary_find_station).setOnClickListener(ignored ->
                navigateFromSummary(view, R.id.nav_nearby_stations, null));
        return view;
    }

    private static void navigateFromSummary(View view, int destinationId, Bundle arguments) {
        NavController controller = Navigation.findNavController(view);
        NavOptions options = new NavOptions.Builder()
                .setPopUpTo(controller.getGraph().getStartDestinationId(), false)
                .build();
        controller.navigate(destinationId, arguments, options);
    }

    private static void setText(View view, int id, String value) {
        ((TextView) view.findViewById(id)).setText(value);
    }
}
