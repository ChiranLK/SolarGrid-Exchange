package com.solargrid.exchange.ui.dashboard;

import android.content.Context;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ArrayAdapter;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;

import com.solargrid.exchange.R;
import com.solargrid.exchange.data.model.DashboardReservation;
import com.solargrid.exchange.ui.reservations.ReservationFormatters;

import java.util.ArrayList;
import java.util.List;

public final class DashboardReservationAdapter extends ArrayAdapter<DashboardReservation> {
    public DashboardReservationAdapter(Context context, List<DashboardReservation> reservations) {
        super(context, 0, new ArrayList<>(reservations));
        setNotifyOnChange(false);
    }

    public void replace(List<DashboardReservation> reservations) {
        clear();
        addAll(reservations);
        notifyDataSetChanged();
    }

    @NonNull
    @Override
    public View getView(int position, @Nullable View convertView, @NonNull ViewGroup parent) {
        View view = convertView;
        if (view == null) {
            view = LayoutInflater.from(getContext()).inflate(R.layout.item_reservation, parent, false);
        }
        DashboardReservation reservation = getItem(position);
        if (reservation != null) {
            bind(view, reservation);
        }
        return view;
    }

    public static void bind(View view, DashboardReservation reservation) {
        Context context = view.getContext();
        ((TextView) view.findViewById(R.id.reservation_item_reference)).setText(
                context.getString(R.string.reservation_reference_value, reservation.getReference()));
        ((TextView) view.findViewById(R.id.reservation_item_station)).setText(
                ReservationFormatters.station(
                        reservation.getStationName(), reservation.getStationId()));
        ((TextView) view.findViewById(R.id.reservation_item_time)).setText(
                ReservationFormatters.localDateTime(reservation.getScheduledStartTimeUtc()));
        ((TextView) view.findViewById(R.id.reservation_item_status)).setText(reservation.getStatus());
        ((TextView) view.findViewById(R.id.reservation_item_energy)).setText(
                ReservationFormatters.energy(reservation.getRequestedEnergyKwh()));
        view.setContentDescription(context.getString(
                R.string.reservation_accessibility_summary,
                reservation.getReference(),
                reservation.getStatus(),
                ReservationFormatters.station(
                        reservation.getStationName(), reservation.getStationId())));
    }
}
