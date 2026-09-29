# Member 4 test evidence

Evidence date: 2026-09-28

Branch: `feature/dashboard-qr-deployment`

This document separates executed evidence from work that could not run. It contains no JWT, raw QR token, verification receipt, NIC, password, connection string, or production endpoint. Test identifiers and values are synthetic.

## Status definitions

- **Passed**: the named command/scenario actually ran to successful completion in this workspace.
- **Failed**: the final verification command ran and an assertion or build failed.
- **Skipped**: the test runner explicitly reported a skipped test.
- **Blocked**: a required prerequisite was absent, so the command could not execute its requested tests.
- **Not Verified**: the complete real browser/device/hosted scenario was not executed and no success is claimed.

## Environment and isolation

- .NET SDK/runtime: .NET 10 (`net10.0` repository target).
- MongoDB integration: Docker Desktop 29.3.1 and disposable `mongo:8.0` replica-set containers on loopback.
- Every xUnit collection run creates a unique `component3_tests_<guid>` database, resets all shared collections before each test, retains real indexes/transactions, and drops the database afterward.
- Web: repository-pinned npm dependencies, ESLint, Vitest 5, TypeScript 6, Vite 8 and React's server renderer. No browser-only success is inferred from server rendering.
- Android supplemental tests: Java 17-compatible source compiled by `javac` and executed by JUnit 4.13.2/Hamcrest 1.3 from the resolved Gradle cache. Temporary classes/JAR copies are created only under ignored `artifacts/android-junit/run-<pid>` and removed after the run.
- Android Gradle/device prerequisites: Android SDK platform/build tools, an emulator or physical camera device, sanitized accounts, and a reachable hosted API were not available.

## Commands and final results

Run these from the repository root unless a working directory is shown.

| Status | Command | Actual final result |
| --- | --- | --- |
| Passed | `dotnet build SolarMicrogrid.slnx --configuration Release --no-restore -m:1` | All three .NET projects built; 0 warnings, 0 errors. |
| Passed | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\run-component3-tests.ps1` | 73/73 API tests passed, 0 failed, 0 skipped, against a real disposable MongoDB 8 replica set. |
| Passed | `dotnet test SolarMicrogrid.Tests/SolarMicrogrid.Tests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=minimal"` | 69/69 passed, 0 failed, 0 skipped. |
| Passed | `npm.cmd run check` from `SolarMicrogrid.Web` | ESLint passed; Vitest passed 6 files and 34/34 tests; TypeScript/Vite production build passed with 70 modules. |
| Passed | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\run-member4-android-unit-tests.ps1` | JUnit passed 17/17 pure-Java Member 4 tests. |
| Passed | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\verify-member4-deployment.ps1` | Live local Release API health, Member 4 OpenAPI and configured/unlisted CORS checks passed against disposable MongoDB. |
| Blocked | `.\gradlew.bat testDebugUnitTest assembleDebug lintDebug` from `SolarGridAndroid` | Gradle stopped while resolving `:app:testDebugUnitTest`: no Android SDK location was configured. No requested Gradle test/build/lint task ran. |
| Failed | Final verification set | None. |
| Skipped | Executed test runners | None; xUnit, Vitest and supplemental JUnit reported zero skipped tests. |

## Backend coverage

All rows below were executed in the 73-test API run unless stated otherwise.

| Requirement | Status | Executed evidence |
| --- | --- | --- |
| Prosumer/operator authorization and isolation | Passed | `DashboardControllerContractTests`, `TransactionsControllerContractTests`, `DashboardServiceIntegrationTests.ProsumerDashboardCountsEachViewAndExcludesAnotherProsumer`, `ProsumerHistorySearchNeverReturnsAnotherOwnersRecord`, station-scoped operator dashboard tests, owner-only issuance and wrong-role verification tests. |
| Current/pending/approved-future counts | Passed | `ProsumerDashboardCountsEachViewAndExcludesAnotherProsumer`, `GridOperatorDashboardIsStationScopedAndReturnsOperationalSections`, and the authenticated combined workflow assert exact authoritative counts. |
| History filters and pagination | Passed | `HistorySupportsStatusStationInclusiveDateAndSafeTextFilters`, `HistoryPaginationUsesStableDescendingScheduleAndIdOrdering`, isolation and invalid-filter tests cover status/station/text/inclusive UTC dates/order/page bounds. |
| QR eligibility, ownership and payload privacy | Passed | `IssueRequiresActiveOwnerAndPersistsOnlyHashes`, `ApprovalGatesQrAndCancellationRejectionOrScheduleExpiryInvalidateIt`, and the authenticated workflow prove owner scope, approval/schedule gates, 43-character opaque payloads, hash-only persistence and absence of owner/reservation/station identifiers. |
| Invalid, expired, wrong role/station verification | Passed | `VerifyRejectsInvalidExpiredWrongRoleAndWrongStationTokens` executed every listed outcome and persisted expiry. |
| Cancelled/rejected/already-completed behavior | Passed | Cancellation of an issued approved token revokes verification; rejected Pending reservations cannot issue; completed/replayed receipts and repeated scans conflict. The lifecycle has no legal `Approved -> Rejected`, so a pre-issued rejected token cannot be constructed without violating Member 3 rules. |
| Atomic completion and replay prevention | Passed | `CompletionTransitionsReservationAndTransactionExactlyOnce` proves the real reservation and transaction records change once, capacity remains `Consumed`, one history entry is written and receipt replay conflicts. |
| Real simultaneous race | Passed | `SimultaneousCompletionAllowsExactlyOneSuccess` launched two completion tasks against the same verified receipt on MongoDB 8 replica-set transactions/CAS. Exactly one response succeeded, one returned `ConflictException`, version increased once and one Completed history entry existed. No mock supplied this evidence. |
| Authenticated combined workflow | Passed | `Member4EndToEndWorkflowIntegrationTests.AuthenticatedApprovedQrCompletionRefreshAndReplayWorkflowSucceeds` performs real Prosumer and Grid Operator logins, Member 3 creation/approval, approved-future read, QR issue, assigned-operator verification, explicit completion, dashboard/history refresh and replay rejection in one isolated database. |
| Health endpoint | Passed | `DeploymentContractTests` covers bounded unhealthy MongoDB, safe response serialization and correlation; the live deployment verifier received healthy process/database state from `GET /health`. |
| Production-safe errors | Passed | `UnexpectedErrorsReturnGenericCorrelatedResponseWithoutSensitiveDetail` ran the correlation and exception middleware chain and proved HTTP 500 returns only the generic error contract plus response correlation header, without injected JWT/QR/NIC/connection/stack text. |

## Web coverage

The final `npm.cmd run check` run passed all 34 tests.

| Requirement | Status | Executed evidence |
| --- | --- | --- |
| Operator route guard | Passed | Exact `GridOperator` access/navigation is allowed; Prosumer/null roles cannot access or see operator routes. |
| Live dashboard rendering | Passed | `operatorDashboardRendering.test.tsx` server-rendered authoritative counts and API reservation reference/station/energy/status data through the production presentational component. |
| Loading/empty/error/retry | Passed | The same component test rendered all states and asserted loading text, no-data explanation, API-unavailable message, Refresh and Try again controls. |
| History request mapping | Passed | Vitest asserted trimmed/encoded search, exact status/station, inclusive UTC boundaries, page and page-size parameters, plus invalid range prevention. |
| Pagination | Passed | Model tests preserve authoritative page order; component rendering asserted page 2/3, total count and Previous/Next controls. |
| Refresh after relevant actions | Passed | The reservation-change subscription test dispatched a sanitized completion notice, observed exactly one refresh callback, unsubscribed, and proved later events no longer refresh. Manual/focus/timer refresh token behavior remains covered. |

Server rendering proves the presentational output and needs no browser. It is not represented as a live authenticated browser-to-API test.

## Android coverage

The reproducible supplemental JUnit script passed 17/17. It compiles only pure-Java production classes and their existing JUnit tests, so it does not substitute for Android Gradle, Fragment/UI, CameraX, ML Kit, emulator or physical-device execution.

| Requirement | Status | Executed evidence |
| --- | --- | --- |
| Role routing | Passed | `RoleRoutePolicyTest` maps exact Prosumer/GridOperator/Backoffice sessions to their supported starts. |
| Dashboard/history state mapping | Passed | `ProsumerPresentationTest`, `BookingHistoryQueryTest`, and `SharedReservationContractTest` cover API-owned empty/content state, retry classification, canonical statuses, filters, paging and UTC range validation. |
| QR display safety/expiry | Passed | `QrTokenPolicyTest` accepts only bounded URL-safe opaque values and rejects JWT/URL/NIC/email-shaped input; `QrExpiryTest` covers countdown, expired and malformed values. |
| Camera permission states | Passed | `CameraPermissionStateTest` covers explanation, ordinary denial/retry, permanent denial/settings and grant states. |
| Scanner response mapping | Passed | `TransactionErrorMapperTest` covers invalid, expired, already-completed, wrong-station, offline/server and authoritative conflict responses. |
| Confirmation/double submit | Passed | `OperatorFlowPolicyTest` requires explicit confirmation/verified data and rejects in-flight, already-completed and rapid second submissions. |
| Completion conflict | Passed | The added JUnit case proves a 409 changed-reservation completion response is non-retryable and never becomes a local success state. |
| Android Gradle unit/UI/build/lint | Blocked | Android SDK is absent; Gradle executed configuration/dependency resolution only and no requested task ran. |
| Physical camera/device | Not Verified | No device/emulator/camera was available. Camera permission dialogs/settings return, focus/orientation, QR recognition, rotation/process recreation, offline/session expiry, accessibility and live completion must be exercised separately. |

## End-to-end scenario matrix

“Passed” below means the complete behavior named in that row ran. Component evidence does not convert an unexecuted browser/device journey into a pass.

| # | Scenario | Status | Evidence or remaining check |
| --- | --- | --- | --- |
| 1 | Prosumer login -> approved reservation appears -> QR displayed | Not Verified | Real login, approved-future list and safe issuance passed in the combined Mongo test; Android QR eligibility/token/expiry presentation policy passed separately. No live Android screen or hosted login was run. |
| 2 | Operator login -> scan -> server verifies -> explicit completion -> summary | Not Verified | Real operator login, verification and explicit completion passed in the combined Mongo test; scanner mapping/confirmation/double-submit policies passed separately. Camera scan and completion summary were not run on a device. |
| 3 | Dashboard/history refresh shows Completed | Not Verified | Real Mongo dashboards/history immediately returned Completed and the web refresh/render tests passed, but no live cross-client browser/device refresh was executed. |
| 4 | Same token/receipt cannot complete twice | Passed | Repeated scan and completion receipt replay returned conflicts; the simultaneous Mongo race produced exactly one success. |
| 5 | Expired token rejected | Passed | Fixed server time advanced beyond expiry; verification conflicted and persisted the transaction as Expired. |
| 6 | Cancelled/rejected reservation token rejected | Passed | Issued token was revoked after legal cancellation; rejected reservation could not issue a token. |
| 7 | Wrong role rejected | Passed | Prosumer attempting operator verification and Backoffice attempting Prosumer issuance were rejected; exact controller policies also passed. |
| 8 | Wrong station rejected | Passed | A token bound to station two was rejected for the persisted station-one operator. |
| 9 | Prosumer cannot see another Prosumer's history | Passed | Shared-station search returned only the authenticated owner's record and count. |
| 10 | Web and Android reach the same hosted API contract | Not Verified | Shared DTO/status/role/error contract tests, web build and Android supplemental tests passed, but no hosted target, browser session, APK/device or sanitized accounts were available. |

## Reproducible live workflow still required

Use the IIS runbook to deploy a sanitized environment, then create isolated synthetic users, two stations and one future slot. Never reuse production PII or record credentials/QR values.

1. Configure web and signed Android release builds with the same HTTPS API and exact CORS origin.
2. Sign in as synthetic Prosumer, confirm an approved reservation through the authoritative list/dashboard, open QR, and verify its countdown without capturing the QR.
3. Sign in on a physical operator device assigned to the reservation station; exercise first permission, denial/retry, permanent-denial/settings return and grant paths.
4. Scan, verify the pseudonymous confirmation, explicitly confirm completion and observe the completion summary.
5. Refresh web and Android dashboard/history and confirm the same reservation is Completed.
6. Repeat scan/completion and confirm conflict; repeat with expired/cancelled/rejected/wrong-role/wrong-station fixtures.
7. Sign in as the second Prosumer and prove the first Prosumer's history/reference cannot be retrieved.
8. Record only timestamps, HTTP status categories, synthetic reservation references, test-run IDs and correlation IDs. Never record JWTs, QR/receipt values, NICs, passwords or connection details.

Until those steps run on the actual hosted environment and device, scenarios 1, 2, 3 and 10 remain **Not Verified**.

## Ownership findings

- No Member 1–3 defect was found in the final run.
- The initial combined workflow assertion expected an approved-future reservation in the narrower current list. The test was corrected to query Member 3's authoritative `ApprovedFuture` view; production behavior was not changed.
- No production feature fix outside Member 4 ownership was made.

Focused commit message: `test(member-4): verify dashboard and QR workflows`
