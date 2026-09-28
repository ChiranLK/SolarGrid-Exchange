package com.solargrid.exchange.data.model;

public final class QrTransactionToken {
    private final String reservationId;
    private final long reservationVersion;
    private final String qrToken;
    private final String issuedAtUtc;
    private final String expiresAtUtc;

    public QrTransactionToken(
            String reservationId,
            long reservationVersion,
            String qrToken,
            String issuedAtUtc,
            String expiresAtUtc) {
        this.reservationId = reservationId;
        this.reservationVersion = reservationVersion;
        this.qrToken = qrToken;
        this.issuedAtUtc = issuedAtUtc;
        this.expiresAtUtc = expiresAtUtc;
    }

    public String getReservationId() { return reservationId; }
    public long getReservationVersion() { return reservationVersion; }
    public String getQrToken() { return qrToken; }
    public String getIssuedAtUtc() { return issuedAtUtc; }
    public String getExpiresAtUtc() { return expiresAtUtc; }
}
