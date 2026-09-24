package com.solargrid.exchange.features.stations;

import com.solargrid.exchange.data.model.Slot;
import com.solargrid.exchange.data.model.Station;

import java.util.List;

public final class StationDetailData {
    private final Station station;
    private final List<Slot> availableSlots;

    public StationDetailData(Station station, List<Slot> availableSlots) {
        this.station = station;
        this.availableSlots = availableSlots;
    }

    public Station getStation() { return station; }
    public List<Slot> getAvailableSlots() { return availableSlots; }
}
