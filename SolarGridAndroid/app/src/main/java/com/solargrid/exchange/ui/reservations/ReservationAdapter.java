package com.solargrid.exchange.ui.reservations;

import android.content.Context;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ArrayAdapter;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;

import com.solargrid.exchange.R;
import com.solargrid.exchange.data.model.Reservation;

import java.util.ArrayList;
import java.util.List;

final class ReservationAdapter extends ArrayAdapter<Reservation> {
    ReservationAdapter(Context context, List<Reservation> reservations) {
        super(context, 0, new ArrayList<>(reservations));
        setNotifyOnChange(false);
    }

    void replace(List<Reservation> reservations) {
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
        Reservation reservation = getItem(position);
        if (reservation != null) {
            ((TextView) view.findViewById(R.id.reservation_item_reference)).setText(
                    getContext().getString(R.string.reservation_reference_value, reservation.getId()));
            ((TextView) view.findViewById(R.id.reservation_item_station)).setText(
                    ReservationFormatters.station(
                            reservation.getStationName(), reservation.getStationId()));
            ((TextView) view.findViewById(R.id.reservation_item_time)).setText(
                    ReservationFormatters.localDateTime(reservation.getScheduledStartTimeUtc()));
            ((TextView) view.findViewById(R.id.reservation_item_status)).setText(
                    reservation.getStatus());
            ((TextView) view.findViewById(R.id.reservation_item_energy)).setText(
                    ReservationFormatters.energy(reservation.getRequestedEnergyKwh()));
        }
        return view;
    }
}
