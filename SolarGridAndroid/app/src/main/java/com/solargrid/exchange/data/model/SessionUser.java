package com.solargrid.exchange.data.model;

import java.util.Objects;

public final class SessionUser {
    public static final String ROLE_BACKOFFICE = "Backoffice";
    public static final String ROLE_GRID_OPERATOR = "GridOperator";
    public static final String ROLE_PROSUMER = "Prosumer";

    private final String token;
    private final String nic;
    private final String fullName;
    private final String email;
    private final String role;
    private final String status;

    public SessionUser(
            String token,
            String nic,
            String fullName,
            String email,
            String role,
            String status) {
        this.token = Objects.requireNonNull(token);
        this.nic = Objects.requireNonNull(nic);
        this.fullName = Objects.requireNonNull(fullName);
        this.email = email == null ? "" : email;
        this.role = Objects.requireNonNull(role);
        this.status = Objects.requireNonNull(status);
    }

    public String getToken() { return token; }
    public String getNic() { return nic; }
    public String getFullName() { return fullName; }
    public String getEmail() { return email; }
    public String getRole() { return role; }
    public String getStatus() { return status; }

    public SessionUser withIdentity(String refreshedEmail, String refreshedRole) {
        return new SessionUser(token, nic, fullName, refreshedEmail, refreshedRole, status);
    }

    public boolean isProsumer() {
        return ROLE_PROSUMER.equals(role);
    }

    public boolean isStaff() {
        return ROLE_GRID_OPERATOR.equals(role) || ROLE_BACKOFFICE.equals(role);
    }
}
