package com.solargrid.exchange.data.model;

public final class DashboardReservation {
    private final String reservationId;
    private final String reference;
    private final String stationId;
    private final String stationName;
    private final String stationAddress;
    private final String scheduledStartTimeUtc;
    private final String scheduledEndTimeUtc;
    private final double requestedEnergyKwh;
    private final String status;

    public DashboardReservation(
            String reservationId,
            String reference,
            String stationId,
            String stationName,
            String stationAddress,
            String scheduledStartTimeUtc,
            String scheduledEndTimeUtc,
            double requestedEnergyKwh,
            String status) {
        this.reservationId = reservationId;
        this.reference = reference;
        this.stationId = stationId;
        this.stationName = stationName;
        this.stationAddress = stationAddress;
        this.scheduledStartTimeUtc = scheduledStartTimeUtc;
        this.scheduledEndTimeUtc = scheduledEndTimeUtc;
        this.requestedEnergyKwh = requestedEnergyKwh;
        this.status = status;
    }

    public String getReservationId() { return reservationId; }
    public String getReference() { return reference; }
    public String getStationId() { return stationId; }
    public String getStationName() { return stationName; }
    public String getStationAddress() { return stationAddress; }
    public String getScheduledStartTimeUtc() { return scheduledStartTimeUtc; }
    public String getScheduledEndTimeUtc() { return scheduledEndTimeUtc; }
    public double getRequestedEnergyKwh() { return requestedEnergyKwh; }
    public String getStatus() { return status; }
}
