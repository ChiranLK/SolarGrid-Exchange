package com.solargrid.exchange.data.model;

public final class Slot {
    private final String id;
    private final String stationId;
    private final String startTimeUtc;
    private final String endTimeUtc;
    private final double availableCapacityKwh;
    private final String availabilityStatus;

    public Slot(String id, String stationId, String startTimeUtc, String endTimeUtc,
                double availableCapacityKwh, String availabilityStatus) {
        this.id = id;
        this.stationId = stationId;
        this.startTimeUtc = startTimeUtc;
        this.endTimeUtc = endTimeUtc;
        this.availableCapacityKwh = availableCapacityKwh;
        this.availabilityStatus = availabilityStatus;
    }

    public String getId() { return id; }
    public String getStationId() { return stationId; }
    public String getStartTimeUtc() { return startTimeUtc; }
    public String getEndTimeUtc() { return endTimeUtc; }
    public double getAvailableCapacityKwh() { return availableCapacityKwh; }
    public String getAvailabilityStatus() { return availabilityStatus; }

    @Override
    public String toString() {
        return startTimeUtc + " — " + endTimeUtc + "\n" +
                availableCapacityKwh + " kWh available · " + availabilityStatus;
    }
}
