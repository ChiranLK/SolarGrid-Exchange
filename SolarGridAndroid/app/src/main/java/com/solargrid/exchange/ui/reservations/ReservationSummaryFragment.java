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
    private static final String ACTION_UPDATED = "updated";
    private static final String ACTION_CANCELLED = "cancelled";

    static Bundle argumentsForUpdate(Reservation reservation, boolean wasApproved) {
        Bundle arguments = argumentsFor(ACTION_UPDATED, reservation);
        arguments.putBoolean("wasApproved", wasApproved);
        return arguments;
    }

    static Bundle argumentsForCancellation(Reservation reservation) {
        return argumentsFor(ACTION_CANCELLED, reservation);
    }

    private static Bundle argumentsFor(String action, Reservation reservation) {
        Bundle arguments = new Bundle();
        arguments.putString("action", action);
        arguments.putString("reservationId", reservation.getId());
        arguments.putString("status", reservation.getStatus());
        arguments.putString("station", ReservationFormatters.station(
                reservation.getStationName(), reservation.getStationId()));
        arguments.putString("slotId", reservation.getSlotId());
        arguments.putString("startUtc", reservation.getScheduledStartTimeUtc());
        arguments.putString("endUtc", reservation.getScheduledEndTimeUtc());
        arguments.putDouble("energy", reservation.getRequestedEnergyKwh());
        arguments.putString("updatedAtUtc", reservation.getUpdatedAtUtc());
        arguments.putString("cancelledAtUtc", reservation.getCancelledAtUtc());
        arguments.putString("cancellationReason", reservation.getCancellationReason());
        return arguments;
    }

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_reservation_summary, container, false);
        Bundle arguments = getArguments() == null ? Bundle.EMPTY : getArguments();
        String action = arguments.getString("action", ACTION_UPDATED);
        boolean cancelled = ACTION_CANCELLED.equals(action);
        setText(view, R.id.summary_outcome, getString(cancelled
                ? R.string.reservation_cancelled
                : R.string.reservation_updated));
        TextView notice = view.findViewById(R.id.summary_notice);
        notice.setText(!cancelled
                && arguments.getBoolean("wasApproved", false)
                && "Pending".equals(arguments.getString("status", ""))
                ? R.string.approved_update_result_notice
                : R.string.server_confirmation_notice);
        setText(view, R.id.summary_status, arguments.getString("status", ""));
        setText(view, R.id.summary_reference, arguments.getString("reservationId", ""));
        setText(view, R.id.summary_station, arguments.getString("station", ""));
        setText(view, R.id.summary_slot, arguments.getString("slotId", ""));
        setText(view, R.id.summary_time,
                ReservationFormatters.localDateTime(arguments.getString("startUtc", "")) + " - "
                        + ReservationFormatters.localDateTime(arguments.getString("endUtc", "")));
        setText(view, R.id.summary_energy,
                ReservationFormatters.energy(arguments.getDouble("energy", 0)));
        setText(view, R.id.summary_timestamp_label, getString(cancelled
                ? R.string.cancelled_at
                : R.string.updated_at));
        String timestamp = cancelled
                ? arguments.getString("cancelledAtUtc", "")
                : arguments.getString("updatedAtUtc", "");
        setText(view, R.id.summary_timestamp, ReservationFormatters.localDateTime(timestamp));

        View reasonGroup = view.findViewById(R.id.summary_reason_group);
        String cancellationReason = arguments.getString("cancellationReason", "");
        boolean hasReason = cancelled && cancellationReason != null && !cancellationReason.isEmpty();
        reasonGroup.setVisibility(hasReason ? View.VISIBLE : View.GONE);
        if (hasReason) {
            setText(view, R.id.summary_reason, cancellationReason);
        }

        view.findViewById(R.id.summary_view_detail).setOnClickListener(ignored -> {
            Bundle detail = new Bundle();
            detail.putString("reservationId", arguments.getString("reservationId", ""));
            Navigation.findNavController(view).navigate(R.id.nav_reservation_detail, detail);
        });
        view.findViewById(R.id.summary_view_reservations).setOnClickListener(ignored -> {
            Bundle pending = new Bundle();
            pending.putString("defaultView", "Pending");
            navigateFromSummary(view, R.id.nav_my_reservations, pending);
        });
        view.findViewById(R.id.summary_view_history).setOnClickListener(ignored ->
                navigateFromSummary(view, R.id.nav_booking_history, null));
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
