package com.solargrid.exchange.data.model;

import androidx.annotation.Nullable;

import org.json.JSONObject;

/** Server-authoritative Prosumer profile from /api/prosumers/me (ProsumerProfileResponseDto). */
public final class ProsumerProfile {
    public static final String STATUS_ACTIVE = "Active";

    private final String nic;
    private final String fullName;
    private final String email;
    private final String phone;
    @Nullable private final String address;
    private final String role;
    private final String status;
    private final boolean deactivationRequested;
    @Nullable private final String deactivationRequestedAtUtc;

    public ProsumerProfile(String nic, String fullName, String email, String phone,
                           @Nullable String address, String role, String status,
                           boolean deactivationRequested, @Nullable String deactivationRequestedAtUtc) {
        this.nic = nic;
        this.fullName = fullName;
        this.email = email;
        this.phone = phone;
        this.address = address;
        this.role = role;
        this.status = status;
        this.deactivationRequested = deactivationRequested;
        this.deactivationRequestedAtUtc = deactivationRequestedAtUtc;
    }

    /** Parses the API response; returns null when a required field is missing (malformed response). */
    @Nullable
    public static ProsumerProfile fromJson(@Nullable JSONObject json) {
        if (json == null) {
            return null;
        }
        String nic = json.optString("nic", "");
        String fullName = json.optString("fullName", "");
        String email = json.optString("email", "");
        String status = json.optString("status", "");
        if (nic.isEmpty() || fullName.isEmpty() || email.isEmpty() || status.isEmpty()) {
            return null;
        }
        return new ProsumerProfile(
                nic,
                fullName,
                email,
                json.optString("phone", ""),
                optionalString(json, "address"),
                json.optString("role", SessionUser.ROLE_PROSUMER),
                status,
                json.optBoolean("deactivationRequested", false),
                optionalString(json, "deactivationRequestedAtUtc"));
    }

    @Nullable
    private static String optionalString(JSONObject json, String key) {
        if (!json.has(key) || json.isNull(key)) {
            return null;
        }
        String value = json.optString(key, "").trim();
        return value.isEmpty() ? null : value;
    }

    public String getNic() { return nic; }
    public String getFullName() { return fullName; }
    public String getEmail() { return email; }
    public String getPhone() { return phone; }
    @Nullable public String getAddress() { return address; }
    public String getRole() { return role; }
    public String getStatus() { return status; }
    public boolean isDeactivationRequested() { return deactivationRequested; }
    @Nullable public String getDeactivationRequestedAtUtc() { return deactivationRequestedAtUtc; }

    public boolean isActive() {
        return STATUS_ACTIVE.equals(status);
    }
}
