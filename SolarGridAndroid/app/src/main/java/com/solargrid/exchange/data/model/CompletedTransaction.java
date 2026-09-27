package com.solargrid.exchange.data.model;

public final class CompletedTransaction {
    private final String reservationId;
    private final String reservationReference;
    private final String stationId;
    private final String status;
    private final long version;
    private final String completedAtUtc;

    public CompletedTransaction(
            String reservationId,
            String reservationReference,
            String stationId,
            String status,
            long version,
            String completedAtUtc) {
        this.reservationId = reservationId;
        this.reservationReference = reservationReference;
        this.stationId = stationId;
        this.status = status;
        this.version = version;
        this.completedAtUtc = completedAtUtc;
    }

    public String getReservationId() { return reservationId; }
    public String getReservationReference() { return reservationReference; }
    public String getStationId() { return stationId; }
    public String getStatus() { return status; }
    public long getVersion() { return version; }
    public String getCompletedAtUtc() { return completedAtUtc; }
}
