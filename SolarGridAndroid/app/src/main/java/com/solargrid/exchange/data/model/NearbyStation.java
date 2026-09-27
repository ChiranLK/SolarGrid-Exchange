package com.solargrid.exchange.data.model;

import java.util.Objects;

public final class NearbyStation {
    private final Station station;
    private final double distanceKm;

    public NearbyStation(Station station, double distanceKm) {
        this.station = Objects.requireNonNull(station);
        this.distanceKm = distanceKm;
    }

    public Station getStation() { return station; }
    public double getDistanceKm() { return distanceKm; }
}
