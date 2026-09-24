package com.solargrid.exchange.data.model;

public final class ReservationStatusHistory {
    private final String fromStatus;
    private final String toStatus;
    private final String changedAtUtc;
    private final String actorNic;
    private final String actorRole;
    private final long version;
    private final String reason;

    public ReservationStatusHistory(
            String fromStatus,
            String toStatus,
            String changedAtUtc,
            String actorNic,
            String actorRole,
            long version,
            String reason) {
        this.fromStatus = fromStatus;
        this.toStatus = toStatus;
        this.changedAtUtc = changedAtUtc;
        this.actorNic = actorNic;
        this.actorRole = actorRole;
        this.version = version;
        this.reason = reason;
    }

    public String getFromStatus() { return fromStatus; }
    public String getToStatus() { return toStatus; }
    public String getChangedAtUtc() { return changedAtUtc; }
    public String getActorNic() { return actorNic; }
    public String getActorRole() { return actorRole; }
    public long getVersion() { return version; }
    public String getReason() { return reason; }
}
