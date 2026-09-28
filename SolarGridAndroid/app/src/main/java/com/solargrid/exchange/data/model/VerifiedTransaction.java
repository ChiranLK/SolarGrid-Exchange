package com.solargrid.exchange.data.model;

public final class VerifiedTransaction {
    private final String verificationId;
    private final String reservationId;
    private final String reservationReference;
    private final String prosumerReference;
    private final long reservationVersion;
    private final String stationId;
    private final String stationName;
    private final String scheduledStartTimeUtc;
    private final String scheduledEndTimeUtc;
    private final double requestedEnergyKwh;
    private final String status;
    private final String verifiedAtUtc;
    private final String expiresAtUtc;

    public VerifiedTransaction(
            String verificationId,
            String reservationId,
            String reservationReference,
            String prosumerReference,
            long reservationVersion,
            String stationId,
            String stationName,
            String scheduledStartTimeUtc,
            String scheduledEndTimeUtc,
            double requestedEnergyKwh,
            String status,
            String verifiedAtUtc,
            String expiresAtUtc) {
        this.verificationId = verificationId;
        this.reservationId = reservationId;
        this.reservationReference = reservationReference;
        this.prosumerReference = prosumerReference;
        this.reservationVersion = reservationVersion;
        this.stationId = stationId;
        this.stationName = stationName;
        this.scheduledStartTimeUtc = scheduledStartTimeUtc;
        this.scheduledEndTimeUtc = scheduledEndTimeUtc;
        this.requestedEnergyKwh = requestedEnergyKwh;
        this.status = status;
        this.verifiedAtUtc = verifiedAtUtc;
        this.expiresAtUtc = expiresAtUtc;
    }

    public String getVerificationId() { return verificationId; }
    public String getReservationId() { return reservationId; }
    public String getReservationReference() { return reservationReference; }
    public String getProsumerReference() { return prosumerReference; }
    public long getReservationVersion() { return reservationVersion; }
    public String getStationId() { return stationId; }
    public String getStationName() { return stationName; }
    public String getScheduledStartTimeUtc() { return scheduledStartTimeUtc; }
    public String getScheduledEndTimeUtc() { return scheduledEndTimeUtc; }
    public double getRequestedEnergyKwh() { return requestedEnergyKwh; }
    public String getStatus() { return status; }
    public String getVerifiedAtUtc() { return verifiedAtUtc; }
    public String getExpiresAtUtc() { return expiresAtUtc; }
}
