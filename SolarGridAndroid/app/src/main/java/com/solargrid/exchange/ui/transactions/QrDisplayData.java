package com.solargrid.exchange.ui.transactions;

import com.solargrid.exchange.data.model.QrTransactionToken;
import com.solargrid.exchange.data.model.Reservation;

public final class QrDisplayData {
    private final Reservation reservation;
    private final QrTransactionToken token;

    public QrDisplayData(Reservation reservation, QrTransactionToken token) {
        this.reservation = reservation;
        this.token = token;
    }

    public Reservation getReservation() { return reservation; }
    public QrTransactionToken getToken() { return token; }
}
