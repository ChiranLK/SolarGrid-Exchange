# Android account flows (Member 1)

The Android app is a native Java/AndroidX/XML REST client. Every account rule is enforced by the API.

## Entry routing

| Situation | Screen |
| --- | --- |
| Successful login or valid stored session, Prosumer | Main app → Prosumer home (existing `RoleRoutePolicy`) |
| Successful login or valid stored session, Grid Operator | Main app → Operator home (Member 4, unchanged) |
| Backoffice | **Blocked on mobile.** Session is cleared and `BackofficeWebOnlyActivity` explains that Backoffice administration is web-only |
| Registration succeeded | `PendingActivationActivity`; no session is created, no auto-login |
| Login refused with "This account is awaiting activation." | `PendingActivationActivity` |
| Login refused as deactivated, invalid credentials, offline, server error | Error message on the login screen |
| Stored session: `/auth/me` 401 | Sign in |
| Stored session: `/auth/me` 403 pending | Session cleared → pending screen |
| Stored session: `/auth/me` 403 deactivated | Session cleared → sign in with an explanation |
| Stored session: offline / server error | Retry on the start-up screen; session kept |

`AccountRoutePolicy` makes these decisions. `AccountStatusPolicy` recognises the API's fixed 403 messages
(`AuthService`, `[RequireActiveAccount]`, `ProsumerService`); keep the two in step if the messages change.

**Backoffice decision:** the assignment assigns Backoffice administration to the web application, so the
mobile app does not keep a Backoffice token and offers no Backoffice screens. Member 4's `RoleRoutePolicy`
(which maps Backoffice to the profile screen) is left unchanged and is simply never reached for Backoffice.

## Profile and deactivation request

- `GET /api/prosumers/me` loads the profile. The API is the source of truth; confirmed name, email and status
  are written back to the existing SQLite session row (no schema change).
- `PUT /api/prosumers/me` sends only `fullName`, `email`, `phone`, `address`. After an email change the user
  is told to sign in with the new email next time; the JWT is never altered locally.
- `POST /api/prosumers/me/deactivation-request` is sent only after a confirmation dialog. The account stays
  Active until Backoffice approves; a 409 shows "already pending" and reloads the profile.
- Grid Operators opening Profile see the existing local session summary (the Prosumer endpoint is not called).

## Local data

SQLite stores only the existing session row: token, NIC, name, email, role, status. Passwords are never
stored (registration password fields also opt out of instance-state saving).
