package com.solargrid.exchange.data.model;

import java.util.Collections;
import java.util.ArrayList;
import java.util.List;

public final class Reservation {
    private final String id;
    private final String prosumerNic;
    private final String prosumerFullName;
    private final String stationId;
    private final String stationName;
    private final String stationAddress;
    private final String slotId;
    private final String slotAvailabilityStatus;
    private final String scheduledStartTimeUtc;
    private final String scheduledEndTimeUtc;
    private final double requestedEnergyKwh;
    private final String status;
    private final long version;
    private final boolean qrEligible;
    private final ReservationAllowedActions allowedActions;
    private final String createdAtUtc;
    private final String updatedAtUtc;
    private final String cancelledAtUtc;
    private final String cancellationReason;
    private final String rejectionReason;
    private final List<ReservationStatusHistory> statusHistory;

    public Reservation(
            String id,
            String prosumerNic,
            String prosumerFullName,
            String stationId,
            String stationName,
            String stationAddress,
            String slotId,
            String slotAvailabilityStatus,
            String scheduledStartTimeUtc,
            String scheduledEndTimeUtc,
            double requestedEnergyKwh,
            String status,
            long version,
            boolean qrEligible,
            ReservationAllowedActions allowedActions,
            String createdAtUtc,
            String updatedAtUtc,
            String cancelledAtUtc,
            String cancellationReason,
            String rejectionReason,
            List<ReservationStatusHistory> statusHistory) {
        this.id = id;
        this.prosumerNic = prosumerNic;
        this.prosumerFullName = prosumerFullName;
        this.stationId = stationId;
        this.stationName = stationName;
        this.stationAddress = stationAddress;
        this.slotId = slotId;
        this.slotAvailabilityStatus = slotAvailabilityStatus;
        this.scheduledStartTimeUtc = scheduledStartTimeUtc;
        this.scheduledEndTimeUtc = scheduledEndTimeUtc;
        this.requestedEnergyKwh = requestedEnergyKwh;
        this.status = status;
        this.version = version;
        this.qrEligible = qrEligible;
        this.allowedActions = allowedActions;
        this.createdAtUtc = createdAtUtc;
        this.updatedAtUtc = updatedAtUtc;
        this.cancelledAtUtc = cancelledAtUtc;
        this.cancellationReason = cancellationReason;
        this.rejectionReason = rejectionReason;
        this.statusHistory = statusHistory == null
                ? Collections.emptyList()
                : Collections.unmodifiableList(new ArrayList<>(statusHistory));
    }

    public String getId() { return id; }
    public String getProsumerNic() { return prosumerNic; }
    public String getProsumerFullName() { return prosumerFullName; }
    public String getStationId() { return stationId; }
    public String getStationName() { return stationName; }
    public String getStationAddress() { return stationAddress; }
    public String getSlotId() { return slotId; }
    public String getSlotAvailabilityStatus() { return slotAvailabilityStatus; }
    public String getScheduledStartTimeUtc() { return scheduledStartTimeUtc; }
    public String getScheduledEndTimeUtc() { return scheduledEndTimeUtc; }
    public double getRequestedEnergyKwh() { return requestedEnergyKwh; }
    public String getStatus() { return status; }
    public long getVersion() { return version; }
    public boolean isQrEligible() { return qrEligible; }
    public ReservationAllowedActions getAllowedActions() { return allowedActions; }
    public String getCreatedAtUtc() { return createdAtUtc; }
    public String getUpdatedAtUtc() { return updatedAtUtc; }
    public String getCancelledAtUtc() { return cancelledAtUtc; }
    public String getCancellationReason() { return cancellationReason; }
    public String getRejectionReason() { return rejectionReason; }
    public List<ReservationStatusHistory> getStatusHistory() { return statusHistory; }
}
