package com.solargrid.exchange.features.reservations;

import com.solargrid.exchange.data.model.Reservation;

import java.util.List;
import java.util.Set;

public final class ReservationCreationReconciler {
    private static final double ENERGY_TOLERANCE_KWH = 0.000001d;

    private ReservationCreationReconciler() { }

    public static Reservation findNewPendingMatch(
            List<Reservation> reservations,
            Set<String> baselineReservationIds,
            String slotId,
            double requestedEnergyKwh) {
        for (Reservation reservation : reservations) {
            if (!baselineReservationIds.contains(reservation.getId())
                    && slotId.equals(reservation.getSlotId())
                    && "Pending".equals(reservation.getStatus())
                    && Math.abs(reservation.getRequestedEnergyKwh() - requestedEnergyKwh)
                    < ENERGY_TOLERANCE_KWH) {
                return reservation;
            }
        }
        return null;
    }
}
