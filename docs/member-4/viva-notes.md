# Member 4 viva notes

Scope: dashboards, booking history/search, secure QR issuance and verification, explicit completion, and deployment (health, CORS, OpenAPI, IIS). Every statement below describes the code on `feature/dashboard-qr-deployment`; file references are given so each answer can be shown in the source.

## 1. Server authority

- The ASP.NET Core API is the only authority for identity, role and station scope, server time, reservation status/version, dashboard counts, QR eligibility, verification and completion.
- Web and Android render API responses and send opaque values back. They do not calculate the seven-day/twelve-hour rules, eligibility, lifecycle transitions, counts or replay decisions.
- Example: the Android QR button appears only when the server's `allowedActions.canGetQr` is true, and a later server rejection still wins. The operator app never parses the QR; it posts the raw string to `POST /api/transactions/verify`.
- Every operation captures a single `serverNowUtc` from the injected `TimeProvider`, so client clocks are display-only and tests can move time deterministically.

## 2. Opaque token design

- `TransactionService.GenerateOpaqueSecret` uses `RandomNumberGenerator.GetBytes(32)`: 256 bits of cryptographic randomness, encoded as a 43-character URL-safe string.
- The token carries **no meaning**: no NIC, JWT, reservation ID, station ID, amount or outcome. Knowing the token tells an attacker nothing, and it cannot be edited to point at another reservation.
- Only the SHA-256 hash (`HashSecret`) is stored in `QrTransactions.TokenHash`. A database leak does not expose usable tokens.
- The record binds the token hash to the reservation ID, reservation **version**, station ID and a one-way owner-reference hash. If the reservation is updated (version changes), cancelled or moved, the old token no longer matches.
- Why not a signed JWT in the QR? A self-describing token leaks data, stays valid until expiry even after a cancellation, and cannot be consumed once. A random reference checked against server state can be revoked and consumed.
- Android `QrTokenPolicy` refuses to render anything that looks like a JWT, URL, email or NIC, as a defence against a server regression.

## 3. Expiry

- `TransactionSettings.QrTokenLifetimeMinutes` and `VerificationLifetimeMinutes` default to 5 and are validated to 1-30 at startup.
- The verification receipt's expiry is `min(now + verification lifetime, ScheduledEndTimeUtc)`, so a receipt never outlives the booked slot.
- Expiry is enforced **in the database filter** (`TokenExpiresAtUtc > serverNowUtc`), not only in application code, so a token cannot slip through between a check and an update. Expired records are persisted as `Expired`.
- Issuance is refused once `ScheduledEndTimeUtc` has passed. No narrower check-in window exists because the team never defined one; this is recorded as Blocked rather than invented.

## 4. Atomic compare-and-set

- **Verify** (`Issued -> Verified`): one `FindOneAndUpdate` whose filter requires `Id`, `TokenHash`, `State == Issued` and unexpired. Only one caller can match that filter; a second concurrent scan finds nothing and gets 409.
- **Complete** runs inside `MongoTransactionRunner` (`StartSession` + `WithTransactionAsync`, replica set required):
  - Reservation CAS filter: `Id`, `Version == expectedVersion`, `Status == Approved`, `CapacityState == Held`, matching station and owner.
  - Update: `Status = Completed`, `Version + 1`, `CapacityState = Consumed`, completion audit fields, and one `StatusHistory` entry.
  - Transaction CAS: `State == Verified` becomes `Completed`.
  - Both updates commit together or neither does.
- This reuses Member 3's legal-transition guard (`Approved -> Completed` is the only completion path) instead of writing a second lifecycle.

## 5. Replay prevention

- The token is single-use: after `Issued -> Verified` it can never match the verify filter again.
- The receipt is single-use: after `Verified -> Completed` it can never match the complete filter again.
- Repeated scan, repeated completion, expired, revoked and changed-version cases all return 409 `{status, message}`.
- Proven by `SimultaneousCompletionAllowsExactlyOneSuccess`: two parallel completions against a real MongoDB 8 replica set produced exactly one success, one conflict, one version increment and one history entry.
- Android also blocks duplicate frames and rapid double taps, but that is UX only. The server is the guarantee.

## 6. Authorization

- Controllers use `[Authorize(Roles = ...)]` with the exact roles `Prosumer`, `GridOperator`, `Backoffice`:
  - Dashboard/history: all three roles, each scoped differently.
  - QR issue: `Prosumer` only.
  - Verify/complete: `GridOperator` only.
- The JWT role is a first gate only. Services re-read the **persisted, active** user from MongoDB, so a deactivated user or changed role cannot act on an old token.
- Scope comes from the database, never the request: Prosumer is scoped by their NIC, Grid Operator by their stored `AssignedStationId`, Backoffice is global. A wrong-station scan is rejected even with a valid token.
- Verification returns a `PRO-xxxx` alias derived from the reservation ID, so the operator never sees the Prosumer's NIC or name.
- Responses: 400 validation, 401 unauthenticated, 403 wrong role/station, 404 not visible in scope, 409 state conflict.

## 7. MongoDB queries and indexes

- Dashboard counts are grouped on the server by the exact status enum, with views (`Current`, `Pending`, `ApprovedFuture`, `History`) delegated to Member 3's `ReservationReadPolicy`, so dashboards and reservation lists cannot disagree.
- History search trims and regex-escapes input before a case-insensitive match on the `RES-` reference, Prosumer NIC/name and station name/address. Scope is always combined with the search, so search cannot reveal another owner's records.
- History sorts by `ScheduledStartTimeUtc` desc then `_id` desc, giving stable pagination.
- Indexes (`MongoDbIndexInitializer`):
  - `ix_reservations_prosumer_history_start_id`, `ix_reservations_station_history_start_id`, `ix_reservations_history_start_id` match the three history scopes and the sort.
  - `ux_qr_transactions_token_hash` (unique) and `ux_qr_transactions_verification_hash` (unique, sparse) back the hash lookups and prevent duplicates.
  - `ix_qr_transactions_reservation_version_state` supports revoking or superseding by reservation/version.
  - `ix_qr_transactions_state_expiry` supports expiry queries.
- No text index or duplicate collection was added.

## 8. SQLite scope (Android)

- The only SQLite database is Member 1's `SessionDatabaseHelper` (`solargrid_local.db`), holding one authenticated session row. Member 4 did not add a second database.
- Reservation, dashboard and history data are **not** cached in SQLite; they always come from the API, so stale local copies cannot mislead.
- Raw QR tokens and verification receipts are held only in ViewModel memory, never in SQLite, bundles, saved state or logs. After process death the app requests a fresh token or asks the operator to rescan.
- The HTTP 401 response clears the same session store, and the app returns to login.

## 9. CORS

- One named policy, `WebClient`, reads exact origins from `Cors:AllowedOrigins`.
- Startup validation rejects wildcards, paths, embedded credentials and non-HTTPS origins in Production.
- Credentials are not enabled because the API uses bearer headers, not cookies.
- Development allows only `localhost:5173`. Production defaults to empty (same-origin behind IIS), and real origins are injected as `Cors__AllowedOrigins__0`.
- CORS is a browser rule, not authentication. Android and other clients still need a valid JWT.

## 10. IIS deployment

- `IIS-Release.pubxml` publishes a framework-dependent .NET 10 build to `artifacts/iis/api`.
- `web.config` uses `AspNetCoreModuleV2`, `hostingModel="inprocess"` and `stdoutLogEnabled="false"`.
- Secrets are never in tracked files: `JwtSettings.Key` and `MongoSettings.ConnectionString` are empty and are injected as IIS/app-pool environment variables (`JwtSettings__Key`, `MongoSettings__ConnectionString`).
- `GET /health` is anonymous and bounded (1-30 s Mongo timeout). It returns 200 or 503 with component states and a correlation ID only, with no connection string or stack trace.
- OpenAPI is on in Development, off by default in Production, and declares bearer security with redacted examples.
- `CorrelationIdMiddleware` adds `X-Correlation-ID`. Unexpected errors return a generic 500 and log only exception type, method, path and correlation ID.
- Runbook: `docs/deployment/iis-deployment.md` (prerequisites, publish, app pool, environment keys, smoke tests, rollback, troubleshooting).
- Honest status: publish, the local Release run and health/OpenAPI/CORS checks pass. A real hosted IIS deployment has **not** been performed from this workspace.

## Challenges encountered (from the recorded progress log)

1. **Official brief not in the repository.** The SE4040 assignment PDF and team plan were not available to the audit, so requirements were traced to the shared Component 3 contract and the task prompts, and unconfirmed items are marked Blocked instead of guessed.
2. **Android SDK unavailable in the development environment.** Gradle unit tests, APK assembly and lint could not run. A reproducible `javac` + JUnit runner (`scripts/run-member4-android-unit-tests.ps1`) was added to test the pure-Java logic, and that result is reported separately rather than as a Gradle pass.
3. **MongoDB transactions need a replica set.** The first dashboard integration run failed because no replica set was reachable and the Docker engine returned HTTP 500. It passed once the disposable MongoDB 8 replica-set runner worked.
4. **Proving one-time completion.** A mock cannot prove atomicity, so a real parallel-completion race test was written against MongoDB transactions.
5. **Integrating without taking over other members' features.** QR revocation had to follow Member 3's cancel/update rules, and station scope had to follow Member 2's data, without re-implementing either. One test initially queried the wrong reservation view (Current instead of ApprovedFuture); the test was fixed and production code was left unchanged.
6. **No map UI upstream.** Member 2's station coordinates are preserved in the DTOs, but there is no map screen to integrate with, so that item is Blocked rather than faked.
7. **Keeping secrets out of the repository.** A previously tracked JWT signing value was removed and replaced with environment injection.

## References

- Microsoft Learn: Host ASP.NET Core on Windows with IIS; ASP.NET Core Module (ANCM) configuration.
- Microsoft Learn: Enable Cross-Origin Requests (CORS) in ASP.NET Core.
- Microsoft Learn: Health checks in ASP.NET Core; Generate OpenAPI documents (`Microsoft.AspNetCore.OpenApi`).
- Microsoft Learn: Role-based authorization in ASP.NET Core; JWT bearer authentication.
- .NET API reference: `System.Security.Cryptography.RandomNumberGenerator`, `SHA256`.
- MongoDB Manual: Transactions (replica-set requirement); `findOneAndUpdate`; Compound indexes and sort; Unique and sparse indexes.
- MongoDB C# Driver documentation: sessions and `WithTransactionAsync`.
- Android Developers: CameraX overview; Request runtime permissions; Save data using SQLite; ViewModel and saved state.
- Google ML Kit: Barcode scanning (bundled model).
- ZXing ("Zebra Crossing") core library.
- OWASP Cheat Sheet Series: Session Management; Logging (sensitive data exclusion).
- Bootstrap 5 documentation; React and React Router documentation.
