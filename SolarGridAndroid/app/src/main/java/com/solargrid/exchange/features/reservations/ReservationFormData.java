package com.solargrid.exchange.features.reservations;

import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.data.model.Slot;

import java.util.Collections;
import java.util.ArrayList;
import java.util.List;

public final class ReservationFormData {
    private final boolean editing;
    private final String stationName;
    private final Reservation reservation;
    private final List<Slot> slots;

    public ReservationFormData(
            boolean editing,
            String stationName,
            Reservation reservation,
            List<Slot> slots) {
        this.editing = editing;
        this.stationName = stationName;
        this.reservation = reservation;
        this.slots = Collections.unmodifiableList(new ArrayList<>(slots));
    }

    public boolean isEditing() { return editing; }
    public String getStationName() { return stationName; }
    public Reservation getReservation() { return reservation; }
    public List<Slot> getSlots() { return slots; }
}
