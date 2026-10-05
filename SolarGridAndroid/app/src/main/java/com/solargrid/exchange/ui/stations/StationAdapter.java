package com.solargrid.exchange.ui.stations;

import android.content.Context;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ArrayAdapter;
import android.widget.TextView;

import androidx.annotation.NonNull;

import com.solargrid.exchange.R;
import com.solargrid.exchange.data.model.NearbyStation;

import java.util.List;
import java.util.Locale;

public final class StationAdapter extends ArrayAdapter<NearbyStation> {
    public StationAdapter(Context context, List<NearbyStation> stations) {
        super(context, 0, stations);
    }

    @NonNull
    @Override
    public View getView(int position, View convertView, @NonNull ViewGroup parent) {
        View row = convertView;
        if (row == null) {
            row = LayoutInflater.from(getContext()).inflate(R.layout.item_station, parent, false);
        }
        NearbyStation nearby = getItem(position);
        if (nearby != null) {
            ((TextView) row.findViewById(R.id.station_item_name)).setText(nearby.getStation().getName());
            ((TextView) row.findViewById(R.id.station_item_address)).setText(nearby.getStation().getAddress());
            ((TextView) row.findViewById(R.id.station_item_distance)).setText(
                    formatDistance(getContext(), nearby.getDistanceKm()));
        }
        return row;
    }

    static String formatDistance(Context context, double distanceKm) {
        if (distanceKm < 0.05) {
            return context.getString(R.string.station_distance_here);
        }
        if (distanceKm < 1) {
            int metres = (int) (Math.round(distanceKm * 100) * 10);
            return String.format(Locale.getDefault(),
                    context.getString(R.string.station_distance_m), metres);
        }
        return String.format(Locale.getDefault(),
                context.getString(R.string.station_distance_km), distanceKm);
    }
}
