# Component 3: Integrated Verification Test Results

Test date: 2026-09-29

Branch: `feature/component3-integration`. The fixes were committed as `c7558f3` on top of the Component 3 integration commit `a9db5bb`. The end-to-end run used a working copy whose application code was identical (`feature/component3-verification`, which differed only in `SolarMicrogrid.Web/package-lock.json` metadata). The automated suites were run again on `feature/component3-integration` before the commit.

Scope: the Component 3 reservation workflow in the real integrated application (backend, web and Android), the IIS deployment configuration, and the defects found on the way.

## Summary

| Area | Result |
| --- | --- |
| Builds and automated tests | All pass. Android lint shows only the 2 existing Member 4 errors. |
| Ten end-to-end workflows (local integrated environment) | 10 / 10 pass after the fixes below |
| Component 3 defects found and fixed | 4 |
| IIS deployment configuration review | Configuration is sound. 5 findings for Member 4 as the deployment owner (none blocking). |
| **Hosted IIS verification** | **Not Verified.** No hosted IIS environment was available or authorised for testing. No hosted deployment success is claimed. |

All ten workflows passed locally, with the real API, web and Android clients running on a development workstation. Hosted IIS verification is Not Verified, and nothing in this record claims a successful hosted deployment.

## Test environment

This was a local development environment. **It does not prove IIS hosting.**

| Part | What was used |
| --- | --- |
| API | `dotnet run` (Kestrel, `http` launch profile, Development environment) on port 5076 |
| Database | Isolated database `c3_demo` on the local development MongoDB 8 container. It was seeded only through the real API. |
| Web | Vite dev server on 127.0.0.1:5173, proxying `/api` to the API. Driven by headless Chrome over the DevTools protocol, using a throwaway profile. |
| Android | Debug APK on an Android emulator, using `10.0.2.2`. The emulator's location was set with `adb emu geo fix`. Driven with `adb` and `uiautomator`. |
| Accounts | Synthetic only: one Backoffice, one Grid Operator assigned to the demo station, and Prosumers "Demo Prosumer A" and "Demo Prosumer B". The passwords were random, kept outside the repository and never printed. |
| Backoffice bootstrap | Enabled once to create the demo Backoffice, then the API was restarted with bootstrap disabled. The running API log has zero bootstrap lines. |

Demo data:

- one station, "C3 Demo Station", with an all-day schedule;
- a control station, "C3 Control Station";
- slots at 2026-10-01 12:00, 14:00, 16:00, 20:00 and 21:00 UTC (the 21:00 slot has 5 kWh capacity, the others 10 kWh);
- a slot beyond the seven-day window (2026-10-07 10:00 UTC);
- a slot within twelve hours (2026-09-29 16:00 UTC).

## Builds and automated tests

| Suite | Result |
| --- | --- |
| `SolarMicrogrid.Tests` (unit) | 216 / 216 passed |
| `SolarMicrogrid.API.Tests` on a MongoDB 8 replica set (`scripts/run-component3-tests.ps1`) | 92 / 92 passed, including new display-name assertions in `ReservationUpdateIntegrationTests.ValidUpdateMovesAllocation` |
| API Debug build | 0 errors |
| Web `lint` / `vitest` / `tsc -b` / `build` | 0 problems / 133 / 133 / pass / pass |
| Android `assembleDebug` / `testDebugUnitTest` | pass / 80 / 80 (new `BookingHorizonMessageTest`) |
| Android `lintDebug` | 2 errors, both existing Member 4 findings (`UnsafeOptInUsageError` in `OperatorScannerFragment`, `PermissionImpliesUnsupportedChromeOsHardware`) |
| `scripts/verify-member4-deployment.ps1 -MongoPort 27031 -ApiPort 5091` (disposable MongoDB) | Health: passed. OpenAPI metadata: passed. Configured and unlisted CORS preflight: passed. |
| Android release URL guard (`assembleRelease --dry-run`) | Unset: refused. `http://10.0.2.2:5076/api/`: refused. `https://api-host.example/api/`: accepted. |

The deployment script defaults to MongoDB port 27019, which the local development container already uses. Pass `-MongoPort` when that container is running.

## End-to-end workflows

| # | Workflow | Result | Actual outcome |
| --- | --- | --- | --- |
| 1 | Android creates a booking; web displays it | PASS | Prosumer A booked 4 kWh through Nearby stations → C3 Demo Station → 12:00 UTC slot → Review → Submit. Android showed "Reservation Created", Pending. The Backoffice web list showed RES-C4A7B3E1, Demo Prosumer A, 4 kWh, Pending. |
| 2 | Staff approve; Android shows the changed status and the existing QR entry point | PASS | The Grid Operator approved it in the web detail dialog, and the web showed Approved, QR eligible. On Android the dashboard showed 0 pending and 1 approved future; the Approved · upcoming list and detail showed Approved with Modify, Cancel reservation and Show secure QR. The Member 4 QR screen opened with its 5-minute expiry countdown. |
| 3 | Web creates for a prosumer; Android shows it | PASS | Backoffice searched eligible Prosumers, selected Demo Prosumer A, picked the 14:00 UTC slot and 3 kWh, reviewed and saved: "Reservation saved", RES-C4A7B3E3, Pending. Android's Pending approval list for Prosumer A showed the booking (Oct 1, 7:30 PM local, 3.00 kWh). |
| 4 | Updates are visible in both clients | PASS | Android changed the booking to 2.5 kWh ("Reservation Updated"), and the web detail showed 2.5 kWh. The web then moved it to the 16:00 UTC slot at 2 kWh ("Reservation updated"). The Android detail showed Oct 1, 9:30 PM, 2.00 kWh. The first slot went back from 7 kWh to 10 kWh available. |
| 5 | Cancellation updates capacity, lists and summaries | PASS | Android cancelled with "Confirm cancellation" and got "Reservation Cancelled". Slot availability went from 8 to 10 kWh. The API summary went from pending 1 / cancelled 0 to pending 0 / cancelled 1 / history 1. The Android home showed Pending 0 and Cancelled 1, with the booking in Recent booking history. The web list showed Cancelled, and the operator web dashboard showed Pending 0. |
| 6 | Seven-day and twelve-hour boundary failures are explained | PASS after fix 3 | **API:** create beyond seven days → 409 "Reservations cannot be scheduled more than 7 days ahead."; move beyond seven days → same 409; update or cancel a booking that starts within 12 h → 409 "…at least 12 hours' notice to update / to cancel." Creating within 12 h is allowed, as the contract says (the notice rule applies only to update and cancel). **Android:** Modify and Cancel are disabled with "The minimum notice period to update/cancel this reservation has passed." Before the fix, the seven-day create failure showed a generic "state changed" toast; it now shows "Bookings can be made only up to seven days ahead…". **Web:** Update and Cancel are disabled with the same reasons, and creating beyond seven days shows the API message plus "Select a slot no more than seven days from the server's current time." |
| 7 | Concurrent requests cannot overbook the last allocation | PASS | Four simultaneous 5 kWh requests (A, B, A, B) hit the 5 kWh slot. Exactly one was accepted (201) and three got 409 "The selected slot is not available for reservation." The slot then showed 0 kWh available, FullyBooked. |
| 8 | One prosumer cannot access another's booking | PASS | Prosumer A's GET, PUT, cancel and QR-issue calls on Prosumer B's booking each returned 404, and B's booking was unchanged (same version and data). A's list contained only A's bookings. A web direct URL showed "Reservation not found". Android uses the same API calls and only lists the signed-in Prosumer's bookings. |
| 9 | A stale QR or repeated completion is rejected through Member 4 | PASS | See "Workflow 9 detail" below. |
| 10 | Station deactivation is blocked while applicable active reservations exist | PASS | The demo station with Pending and Approved bookings got 409 "Station cannot be deactivated while it has pending or approved reservations." and stayed active; the web Deactivate dialog showed the same message. The control station was refused while its booking was Pending and deactivated (200) after the booking was cancelled; a new booking on it then got 409 "…inactive station." |

Workflow 9 detail (Member 4 endpoints, run as the Grid Operator and the owning Prosumer):

| Step | Status | Message |
| --- | --- | --- |
| Approve; issue QR T1; verify T1 → receipt R1 | 200 / 200 / 200 | |
| The Prosumer makes a material update (Approved → Pending) | 200 | |
| Complete with stale receipt R1 | 409 | The reservation changed. Scan a new QR token and try again. |
| Verify T1 again | 409 | The QR token has already been verified. |
| QR issued, not scanned, then the booking is updated; verify that QR | 409 | The reservation is no longer eligible for QR completion. |
| Re-approve; issue T2; verify → R2; two concurrent completions with R2 | 200 and 409 | The transfer has already been completed. |
| Repeat completion with R2; verify T2 again | 409 / 409 | The transfer has already been completed. |
| Cancel the completed booking; issue a QR for it | 409 / 409 | Only pending or approved reservations may be cancelled. / …no longer eligible for QR completion. |

The completed booking kept its consumed capacity (8 of 10 kWh available afterwards).

The Android operator camera scanner was not exercised because the emulator has no camera QR input. The Member 4 verify and complete endpoints were called directly as the Grid Operator.

## Component 3 defects found and fixed (commit `c7558f3`)

| # | Defect | Effect | Fix |
| --- | --- | --- | --- |
| 1 | `item_station.xml`: the card root was `clickable` and `focusable` | It consumed taps, so `NearbyStationsFragment`'s item click never fired. Without a Maps key, no station could be opened, so no booking could start from Nearby stations. | Removed both attributes (the ripple foreground stays). Verified on the device. |
| 2 | `item_reservation.xml`: the reference text was `textIsSelectable` | This made every row focusable, which blocks `ListView` item clicks. Rows in My reservations, booking history and the Prosumer home did not open the detail. | Removed `textIsSelectable`. Verified on the device. |
| 3 | Android create and update error mapping | The API reports the seven-day horizon as 409. `BookingReviewViewModel` mapped it to "The station, slot, capacity, or account state changed…", and `ReservationFormViewModel` to a generic conflict. The API detail was cut off by the two-line toast. | Added `isBookingHorizonMessage` and a horizon branch in both mappings, plus `BookingHorizonMessageTest`. Verified on the device: "Bookings can be made only up to seven days ahead…". |
| 4 | API create, update, cancel, approve and reject responses omitted `stationName`, `stationAddress`, `prosumerFullName` and `slotAvailabilityStatus` | The Android creation, update and cancellation summaries showed the raw station ID instead of the name. The same gap affects any client using mutation responses. | Added `ReservationService.MapToDisplayResponseAsync`. It loads the display references once and ignores caller cancellation, so a committed change is never reported as failed. Added integration assertions. Verified via the API and on the device: the summary now shows "C3 Demo Station". |

## IIS deployment configuration review

Reviewed:

- `docs/deployment/iis-deployment.md`
- `SolarMicrogrid.API/web.config`
- `Properties/PublishProfiles/IIS-Release.pubxml`
- `appsettings.json`, `appsettings.Production.json` and `appsettings.Development.json`
- `Program.cs`, `ExceptionMiddleware` and `CorrelationIdMiddleware`
- `SolarMicrogrid.Web/.env*.example`
- `SolarGridAndroid/app/build.gradle` and the manifests

| Item | Finding |
| --- | --- |
| Secrets | Correct. No connection string, JWT key or password in tracked files. The JWT key and connection string are empty in `appsettings.json` and validated at startup (the key must be at least 32 characters). `.env.*`, `local.properties` and `secrets.properties` are git-ignored (there is no ignore rule for local `appsettings` override files, so secrets must stay in environment values). The runbook requires IIS or secret-provider environment values. |
| Environment API URLs | Correct. Web: `VITE_API_BASE_URL` is set at build time (`/api` for same origin, otherwise the HTTPS API origin plus a CORS entry). Android: release builds require `SOLARGRID_API_BASE_URL` as HTTPS ending in `/api/`, and loopback and HTTP are refused. Cleartext traffic is allowed only in the debug manifest. |
| MongoDB | Correct. All `MongoSettings__*` keys are validated at startup, and the runbook requires a replica set for transactions, TLS and a least-privilege user. `/health` performs a bounded ping and returns 503 without detail. |
| Web CORS | Correct. Exact origins only, HTTPS in non-Development environments, and no credentials. The runbook has a CORS preflight check. |
| Authentication | Correct. JWT issuer, audience, key and lifetime are validated. `[RequireActiveAccount]` rechecks account status on reservation endpoints. |
| Server error logging | Mostly correct. Every response has an `X-Correlation-ID` header and a correlation log scope. 500s are logged without exception payloads. ANCM stdout is off, and the Event Log is the normal sink. See D1. |

Findings for Member 4, who owns the IIS deployment, the runbook and the shared middleware (none are Component 3 code; not changed here):

- **D1: aborted requests are logged as errors.** `ExceptionMiddleware` logs client-aborted requests as `Error` "Unhandled TaskCanceledException for GET /api/…" and tries to write a 500 to the closed connection. This happened repeatedly during the demo when Android screens were left mid-request. On IIS it would fill the Event Log and hide real faults.
  - Suggested fix: `catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)`, log at Information and write nothing.
- **D2: no hosting section for the web client.**
  - The runbook covers the API only. The React build needs an IIS site or application with a URL Rewrite SPA fallback to `index.html`; otherwise refreshing `/reservations/{id}` returns 404.
  - For `VITE_API_BASE_URL=/api`, `/api` must be routed to the API. The API's `web.config` handler uses `path="*"`, so the two cannot share one physical site root.
- **D3: `BootstrapBackoffice__*` is missing from the runbook's key list.** It should cross-reference `docs/member-1/backoffice-bootstrap.md` and require the setting to be disabled, and the bootstrap password removed, after the first account exists.
- **D4: `BusinessRules__MaxBookingDaysAhead` and `BusinessRules__MinChangeNoticeHours` are not mentioned.** The defaults (7 and 12) match the contract. The runbook should say they must not be overridden in production.
- **D5: the runbook's smoke tests do not cover Component 3.** Use the hosted smoke test below.

## Hosted IIS verification: Not Verified

No hosted IIS environment was available or authorised for this test. The local run above does not prove IIS hosting, the TLS certificate, the reverse-proxy routing, the production MongoDB or the release Android build.

Once Member 4 (deployment owner) provides an authorised environment, follow `docs/deployment/iis-deployment.md` from "Prerequisites" through "Smoke tests", then run the steps below.

Hosted smoke test for Component 3. Use synthetic accounts only and never record tokens, passwords or QR values.

1. Build the clients against the hosted API:
   - Web: `$env:VITE_API_BASE_URL = '/api'` (or the HTTPS API origin), then `npm.cmd --prefix SolarMicrogrid.Web ci` and `npm.cmd --prefix SolarMicrogrid.Web run build`. Deploy `dist` with the SPA fallback (D2).
   - Android: `$env:SOLARGRID_API_BASE_URL = 'https://<api-host>/api/'`, then run `.\gradlew.bat assembleRelease` in `SolarGridAndroid`. Sign it with the team key held outside the repository and install it on a device that trusts the certificate.
2. Check the API is up: `GET https://<api-host>/health` must return 200 with api and database Healthy and an `X-Correlation-ID` header. Run the CORS preflight from the runbook with the deployed web origin and with an unlisted origin.
3. Create the accounts:
   - Create the first Backoffice with bootstrap (Member 1 doc), then disable bootstrap and recycle the pool.
   - In the web client, create a Grid Operator assigned to a test station, and register and activate two Prosumers.
   - Create one station with an all-day schedule and slots:
     - three slots 2–3 days ahead (10 kWh);
     - one slot 2–3 days ahead (5 kWh);
     - one slot 8 days ahead;
     - one slot 4–10 hours ahead.
4. Repeat workflows 1–10 above through the hosted web build and the release APK. For workflows 7–9, send the API calls with `Idempotency-Key` headers from a workstation allowed to reach the API, using the same request bodies as this record. The expected status codes and messages are the ones recorded above.
5. After each failure case, look up the correlation ID in the Event Viewer (Application log) and the IIS logs:
   - no Authorization header, JWT, QR value, NIC or connection string may appear;
   - no `Error` entries may appear for 4xx results.
6. Clean up:
   - cancel the remaining test bookings;
   - deactivate the test station;
   - deactivate the synthetic accounts;
   - confirm `BootstrapBackoffice__Enabled` is false and `OpenApi__Enabled` is false.

Record the actual outcomes in a new "Hosted verification" section of this file, with the date, the release ID and the environment name. Do not include hostnames if they are confidential.

## Remaining blockers and follow-ups

| Owner | Item |
| --- | --- |
| Member 4 (deployment) | Hosted IIS verification is Not Verified (see above). Runbook and logging findings D1–D5. |
| Member 4 | 2 Android lint errors. The operator camera scan was not exercised on a device. |
| Member 2 | Deleting a station with active bookings returns the deactivation wording ("Station cannot be deactivated…"). The station detail lists slots beyond the seven-day window. They can be selected, and the API refusal is now explained. |
| Component 3 | The web create page also offers slots beyond seven days (the refusal is explained). Filtering them out of both clients' slot pickers would avoid the failed attempt. The Android create error toast is limited to two lines; the reason is in the first line. |
| Test environment | The emulator needs `adb emu geo fix <lng> <lat>` pushed until Nearby stations updates. Android test location providers (`cmd location`) broke location updates and must not be used. |
