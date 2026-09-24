package com.solargrid.exchange.ui.stations;

import android.content.Context;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ArrayAdapter;
import android.widget.TextView;

import androidx.annotation.NonNull;

import com.solargrid.exchange.R;
import com.solargrid.exchange.data.model.Slot;

import java.util.List;

public final class SlotAdapter extends ArrayAdapter<Slot> {
    public SlotAdapter(Context context, List<Slot> slots) {
        super(context, 0, slots);
    }

    @NonNull
    @Override
    public View getView(int position, View convertView, @NonNull ViewGroup parent) {
        View row = convertView;
        if (row == null) {
            row = LayoutInflater.from(getContext()).inflate(R.layout.item_slot, parent, false);
        }
        Slot slot = getItem(position);
        if (slot != null) {
            ((TextView) row.findViewById(R.id.slot_item_time)).setText(
                    getContext().getString(
                            R.string.slot_time_range,
                            slot.getStartTimeUtc(),
                            slot.getEndTimeUtc()));
            ((TextView) row.findViewById(R.id.slot_item_capacity)).setText(
                    getContext().getString(
                            R.string.slot_capacity_status,
                            slot.getAvailableCapacityKwh(),
                            slot.getAvailabilityStatus()));
        }
        return row;
    }
}
