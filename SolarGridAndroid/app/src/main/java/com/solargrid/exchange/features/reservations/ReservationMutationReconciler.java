package com.solargrid.exchange.features.reservations;

import com.solargrid.exchange.data.model.Reservation;

public final class ReservationMutationReconciler {
    private static final double ENERGY_TOLERANCE_KWH = 0.000001d;

    private ReservationMutationReconciler() { }

    public static boolean matchesUpdate(
            Reservation current,
            long expectedVersion,
            String originalStatus,
            String originalSlotId,
            double originalEnergyKwh,
            String requestedSlotId,
            double requestedEnergyKwh) {
        if (current == null
                || !requestedSlotId.equals(current.getSlotId())
                || Math.abs(current.getRequestedEnergyKwh() - requestedEnergyKwh)
                >= ENERGY_TOLERANCE_KWH
                || !("Pending".equals(current.getStatus()) || "Approved".equals(current.getStatus()))) {
            return false;
        }

        boolean materialChange = !requestedSlotId.equals(originalSlotId)
                || Math.abs(requestedEnergyKwh - originalEnergyKwh) >= ENERGY_TOLERANCE_KWH;
        if (!materialChange) {
            return current.getVersion() >= expectedVersion;
        }
        if ("Approved".equals(originalStatus) && !"Pending".equals(current.getStatus())) {
            return false;
        }
        return current.getVersion() > expectedVersion;
    }

    public static boolean matchesCancellation(
            Reservation current,
            long expectedVersion,
            String requestedReason) {
        if (current == null
                || !"Cancelled".equals(current.getStatus())
                || current.getVersion() <= expectedVersion
                || current.getCancelledAtUtc() == null
                || current.getCancelledAtUtc().trim().isEmpty()) {
            return false;
        }
        String normalizedReason = requestedReason == null ? "" : requestedReason.trim();
        return normalizedReason.isEmpty()
                || normalizedReason.equals(normalize(current.getCancellationReason()));
    }

    private static String normalize(String value) {
        return value == null ? "" : value.trim();
    }
}
