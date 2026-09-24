package com.solargrid.exchange.ui.stations;

import android.content.Context;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ArrayAdapter;
import android.widget.TextView;

import androidx.annotation.NonNull;

import com.solargrid.exchange.R;
import com.solargrid.exchange.data.model.Station;

import java.util.List;

public final class StationAdapter extends ArrayAdapter<Station> {
    public StationAdapter(Context context, List<Station> stations) {
        super(context, 0, stations);
    }

    @NonNull
    @Override
    public View getView(int position, View convertView, @NonNull ViewGroup parent) {
        View row = convertView;
        if (row == null) {
            row = LayoutInflater.from(getContext()).inflate(R.layout.item_station, parent, false);
        }
        Station station = getItem(position);
        if (station != null) {
            ((TextView) row.findViewById(R.id.station_item_name)).setText(station.getName());
            ((TextView) row.findViewById(R.id.station_item_address)).setText(station.getAddress());
        }
        return row;
    }
}
