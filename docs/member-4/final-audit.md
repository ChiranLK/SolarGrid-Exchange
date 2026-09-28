# Member 4 final completion audit

Audit date: 2026-09-28

Branch: `feature/dashboard-qr-deployment` (based on `develop` at `bb1eafd`, audited at `dd485db` plus this documentation commit)

Status vocabulary: **Completed** (implemented and verified by a command run in this audit), **Blocked** (a missing prerequisite prevents verification or completion), **Not Verified** (implemented or partly evidenced, but the complete real-environment check has not run). Nothing is marked Completed from earlier documentation alone; every Completed line was re-checked against the files and the commands below.

## Source documents

| Document | Result |
| --- | --- |
| SE4040 Assignment 1 PDF (latest) | **Not available.** It is not in the repository, and a search of the user profile found no matching file. The only related PDF found is an exported prompt, not the official brief. Assignment-wide gates below come from the task prompt and repository conventions, not the PDF text. |
| EAD Plan.pdf | **Not available** in the repository or user profile. Team-plan ownership is taken from `docs/component-3/progress.md` and `docs/component-3/reservation-contract.md`. |
| `docs/member-4/contracts.md` | Present. Definitions, routes, DTOs and security rules re-checked against the code. |
| `docs/member-4/progress.md` | Present. Earlier results were re-run rather than copied. |

The student must compare this audit against the two PDFs before submission (see "Manual steps" below).

## Commands run for this audit

All commands were run from the repository root on Windows 11, .NET SDK 10.0.401, Node 22.14.0 and Docker 29.3.1.

| # | Command | Result |
| --- | --- | --- |
| 1 | `dotnet restore SolarMicrogrid.slnx` | Passed |
| 2 | `dotnet build SolarMicrogrid.slnx --configuration Release --no-restore -m:1` | Passed: 0 warnings, 0 errors |
| 3 | `dotnet test SolarMicrogrid.Tests/SolarMicrogrid.Tests.csproj --configuration Release --no-build --no-restore` | Passed: 69/69, 0 failed, 0 skipped |
| 4 | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\run-component3-tests.ps1` | Passed: 73/73 API tests, 0 failed, 0 skipped, against a disposable MongoDB 8 replica set |
| 5 | `npm.cmd ci` (in `SolarMicrogrid.Web`) | Passed: 0 vulnerabilities |
| 6 | `npm.cmd run check` (in `SolarMicrogrid.Web`) | Passed: ESLint clean; Vitest 6 files, 34/34; TypeScript + Vite production build, 70 modules |
| 7 | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\run-member4-android-unit-tests.ps1` | Passed: JUnit 17/17 (pure-Java supplemental, not a Gradle run) |
| 8 | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\verify-member4-deployment.ps1` | Passed: health, OpenAPI bearer/routes/status metadata/examples, configured and unlisted CORS preflight |
| 9 | `dotnet publish SolarMicrogrid.API/SolarMicrogrid.API.csproj -c Release -p:PublishProfile=IIS-Release` | Passed: `artifacts/iis/api` contains the DLL and `web.config` (`AspNetCoreModuleV2`, `inprocess`, stdout logging off); published `appsettings.json` has an empty JWT key and connection string |
| 10 | `.\gradlew.bat testDebugUnitTest assembleDebug lintDebug --no-daemon` (in `SolarGridAndroid`) | **Blocked**: `SDK location not found` while resolving `:app:testDebugUnitTest`. No `ANDROID_HOME` and no `local.properties`. None of the three tasks ran. |
| 11 | `.\gradlew.bat assembleRelease` | **Not run**: blocked by the same missing SDK, and it also needs a hosted HTTPS API URL and signing identity |

Failed: none. Skipped: none reported by any runner.

## Member 4 traceability

| # | Requirement | Artifact (backend / web / Android / deployment) | Test / evidence | Status | Owner / dependency |
| --- | --- | --- | --- | --- | --- |
| 1 | Prosumer dashboard statistics | API `GET /api/dashboard` (`DashboardController`, `DashboardService`); Android `nav_prosumer_home` via `DashboardRepository` | `ProsumerDashboardCountsEachViewAndExcludesAnotherProsumer` (run 4); `ProsumerPresentationTest` (run 7) | Completed at API and logic level. On-device rendering: Not Verified (needs SDK/device). | Member 4; statuses/views from Member 3 |
| 2 | Grid Operator dashboard statistics | Same API, station-scoped; web `/operator/dashboard` | `GridOperatorDashboardIsStationScopedAndReturnsOperationalSections` (run 4); `operatorDashboardRendering.test.tsx` (run 6) | Completed | Member 4; assigned station from Member 1/2 |
| 3 | Current and pending reservations | Dashboard `current`/`pending` sections via `ReservationReadPolicy` views | Dashboard integration tests (run 4); web render tests (run 6) | Completed | Member 3 view definitions |
| 4 | Approved future-reservation count | `ApprovedFuture` count in dashboard response | Dashboard count tests and `AuthenticatedApprovedQrCompletionRefreshAndReplayWorkflowSucceeds` (run 4) | Completed | Member 3 `ApprovedFuture` view |
| 5 | Full booking history, search and filters | API `GET /api/dashboard/history` (search, status, stationId, fromUtc/toUtc, paging) plus 3 history indexes; web `/operator/history`; Android `nav_booking_history` | `HistorySupportsStatusStationInclusiveDateAndSafeTextFilters`, `HistoryPaginationUsesStableDescendingScheduleAndIdOrdering`, `ProsumerHistorySearchNeverReturnsAnotherOwnersRecord` (run 4); web query tests (run 6); `BookingHistoryQueryTest` (run 7) | Completed (API/web). Android screen on device: Not Verified. | Member 4 |
| 6 | Secure QR issuance | `POST /api/transactions/reservations/{id}/qr`; 256-bit random token, hash-only `QrTransactions`, unique hash index | `IssueRequiresActiveOwnerAndPersistsOnlyHashes`, `ApprovalGatesQrAndCancellationRejectionOrScheduleExpiryInvalidateIt` (run 4) | Completed | Member 4; approval from Member 3 |
| 7 | Secure QR display | Android `nav_reservation_qr` (ZXing, in-memory only, countdown, clears at expiry, `QrTokenPolicy`) | `QrTokenPolicyTest`, `QrExpiryTest` (run 7) | Not Verified: logic passes, but on-device display needs an SDK/device run | Member 4 |
| 8 | Native Android Grid Operator scanner | Android `nav_qr_operations` (CameraX 1.6.2 + bundled ML Kit 17.3.0, permission states, duplicate-frame guard) | `CameraPermissionStateTest`, `OperatorFlowPolicyTest`, `RoleRoutePolicyTest` (run 7) | Not Verified: camera scan needs a physical device; Gradle build Blocked (run 10) | Member 4; Android SDK/device (student) |
| 9 | Server verification | `POST /api/transactions/verify` with persisted station/role check, `Issued -> Verified` CAS, `PRO-` alias | `VerifyRejectsInvalidExpiredWrongRoleAndWrongStationTokens` (run 4); `TransactionErrorMapperTest` (run 7) | Completed (API). Android verification screen: Not Verified. | Member 4 |
| 10 | Explicit completion | `POST /api/transactions/reservations/{id}/complete`; Android confirmation dialog + `nav_transaction_completion` | `CompletionTransitionsReservationAndTransactionExactlyOnce` (run 4); `OperatorFlowPolicyTest` confirmation/double-submit (run 7) | Completed (API). Device UI: Not Verified. | Member 4; Member 3 legal transition |
| 11 | Atomic one-time completion / replay prevention | Mongo transaction: reservation version/status/capacity CAS plus transaction `Verified -> Completed` | `SimultaneousCompletionAllowsExactlyOneSuccess` (real race, one winner) and repeat scan/receipt conflicts (run 4) | Completed | Member 4; replica-set MongoDB |
| 12 | Database health | `GET /health` (bounded Mongo check, 200/503, correlation ID only) | `DeploymentContractTests` (run 4); live check (run 8) | Completed | Member 4 |
| 13 | CORS | Named `WebClient` policy, validated exact origins, no credentials | `DeploymentContractTests` (run 4); configured/unlisted preflight (run 8) | Completed (local). Real hosted origin: Blocked (not supplied). | Member 4; team/infrastructure for origin |
| 14 | OpenAPI | Bearer security, Member 4 operation descriptions, status metadata, redacted examples | Run 8 OpenAPI checks | Completed | Member 4 |
| 15 | IIS documentation and configuration | `IIS-Release.pubxml`, `web.config`, `docs/deployment/iis-deployment.md`, `scripts/verify-member4-deployment.ps1` | Publish output inspected (run 9) | Completed (config/publish). Hosted IIS deployment: Not Verified (no host, domain, certificate or secret source). | Member 4; group lead / infrastructure for host |
| 16 | Integration with authentication (Member 1) | Exact JWT roles, persisted active-user checks, Android shared SQLite session, 401 clears session | `Member4CrossComponentContractTests` JWT/role tests and authenticated end-to-end workflow (run 4) | Completed (API). Android session runtime: Not Verified. | Member 1 |
| 17 | Integration with station/map (Member 2) | Station names and assigned-station scope from `SolarStationInfo`; coordinates preserved in DTOs | Wrong-station and dashboard station-name tests (run 4) | Completed for station data. **Map: Blocked**, because no map UI exists upstream. | Member 2 |
| 18 | Integration with reservation workflow (Member 3) | Canonical five statuses, `ReservationReadPolicy`, legal `Approved -> Completed`, QR revoked by cancel/update | Lifecycle integration tests across create/approve/cancel/reject (run 4); `SharedReservationContractTest` (run 7) | Completed | Member 3 |
| 19 | Full live journey: dashboard -> QR -> scan -> verify -> complete -> refreshed history | Both clients against one hosted API | Server-boundary workflow passed (run 4); client pieces passed separately (runs 6, 7) | Not Verified: needs hosted API, device and synthetic accounts | Student / group |
| 20 | Narrower check-in/completion window | None (not defined by the team) | n/a | Blocked: team decision required | Team |

## Assignment-wide gates relevant to Member 4

| Gate | Evidence checked in this audit | Status |
| --- | --- | --- |
| C# API with MongoDB | `SolarMicrogrid.API` targets .NET 10 and uses `MongoDB.Driver`; one `MongoDbContext`; real replica-set tests pass (run 4) | Completed |
| FAT service pattern | Controllers are thin (`DashboardController` 92 lines, `TransactionsController` 119 lines, routing/authorize only); logic lives in `DashboardService` (638) and `TransactionService` (766) | Completed |
| IIS readiness | Publish profile, ANCM `web.config`, runbook, successful publish (run 9) | Completed (hosted deployment Not Verified) |
| Pure native Android with SQLite | 96 `.java` files, 0 `.kt`; no Flutter/React Native/Xamarin/MAUI/Compose/Cordova references; single `SQLiteOpenHelper` (`SessionDatabaseHelper`) | Completed (Gradle build Blocked by missing SDK) |
| Web is a responsive Bootstrap 5 / React UI-only API client | Dependencies are only `bootstrap`, `react`, `react-dom`, `react-router-dom`; the only `fetch(` is in `src/api/apiClient.ts`; no direct database access | Completed (browser visual check Not Verified) |
| `.cs` file header and method comments | Heuristic scan of all 33 `.cs` files added or modified by Member 4 (`git diff main...HEAD`): 33/33 have a `/* <FileName>.cs ... Purpose */` header; 145 methods/constructors detected, and every one has a leading comment or doc comment. The 8 scanner hits were object-creation expressions, not methods. `Exceptionmiddleware.cs` has a pre-existing header whose casing differs from the file name. | Completed |
| No secrets, raw QR tokens, JWTs or real NIC data | `git grep` over tracked files: no JWT-shaped strings, no credentialed Mongo URIs, no private keys, no non-empty `Key`/`ConnectionString`/`Password` JSON values, and no tracked screenshots. NIC-shaped values exist only as obviously synthetic test fixtures (`200012345678`, `200000000000`). Published config has an empty key and connection string. | Completed |
| Build/test results current, failures/skips visible | This document's command table; the Gradle blocker is reported, not hidden | Completed |

## Submission support created in this audit

- `README.md`: consolidated setup, architecture, roles, launch, IIS, tests, troubleshooting and the Member 4 contribution
- `docs/member-4/screenshot-checklist.md`: 34 unique screenshots, all currently uncaptured, with owners
- `docs/member-4/video-script.md`: five-minute running order and a 75-second Member 4 segment
- `docs/member-4/viva-notes.md`: design explanations, challenges and references

No screenshots, video, or hosted-deployment evidence were produced in this audit.

## Manual steps the student must still perform

1. Compare this audit against the latest SE4040 Assignment 1 PDF and EAD Plan.pdf. Add any requirement missing from the table above, and update the status if the brief defines something differently (for example, a check-in window).
2. Install the Android SDK (platform 36 + build tools) or open `SolarGridAndroid` in Android Studio, then run `.\gradlew.bat testDebugUnitTest assembleDebug lintDebug` and record the real result here.
3. Run the Android app on an emulator and a physical camera device: Prosumer QR display, operator permission paths, scan, verify, complete, and replay rejection.
4. Deploy to the team's IIS host following `docs/deployment/iis-deployment.md`, inject secrets through environment variables, and run the smoke tests.
5. Capture every screenshot in `screenshot-checklist.md` following its redaction rules.
6. Record the Member 4 video segment.
7. Replace the repository-link placeholder in `README.md` if the submission uses a different URL, and fill in the member names/IDs.
8. Push the branch and open the PR to `develop` (not `main`) once steps 1-2 are recorded.
