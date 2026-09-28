# Member 1 final audit

**Member:** H.A.S MADUWANTHA (IT23472020) · **Branch:** `feature/auth-account-management` · **Audit date:** 2026-09-29

Scope: authentication, JWT roles, user and Prosumer account management, web login and Backoffice user
management, Android registration/login/profile/deactivation request and SQLite session storage.

Status values: **Complete** (implemented and verified at runtime), **Not Runtime Verified** (implemented
and covered by automated tests, but the manual end-to-end step was not performed), **Blocked** (needs
another owner or an external action).

## Evidence summary

| Check | Result |
| --- | --- |
| `dotnet build SolarMicrogrid.slnx -c Release` | Succeeded, 0 errors (2 NU1900 offline NuGet-audit warnings) |
| `SolarMicrogrid.Tests` (unit, no database) | **213 / 213 passed** (144 Member 1 tests) |
| `scripts/run-component3-tests.ps1` (`SolarMicrogrid.API.Tests`, real MongoDB 8.0 replica set in a throwaway Docker container, synthetic data) | **73 / 73 passed** |
| Member 1 HTTP runtime check (temporary API instance + throwaway database, synthetic accounts, deleted afterwards) | **42 / 42 checks passed** |
| Manual verification with a person at the browser and emulator (see below) | 6 of 8 checkpoints passed, 1 not verified (skipped), 1 partly verified; 1 defect found and fixed |
| Web `npm run lint` / `npm test` / `npx tsc -b` / production build | 0 problems / **129 / 129** (78 Member 1) / pass / pass |
| Android `assembleDebug` / `testDebugUnitTest` | pass / **78 / 78** (48 Member 1) |
| Android `connectedDebugAndroidTest` (Pixel_9 emulator, API 36) | **3 / 3 passed** (SQLite session store) |
| Android `lintDebug` | Fails **only** on 2 existing Member 4 errors (see Blockers); Member 1 adds no lint errors or warnings |
| `scripts/run-member4-android-unit-tests.ps1` | 17 / 17 passed |
| Member 1 C# header / method-comment audit (23 API files, 7 test files) | 0 problems |

## Manual verification (2026-09-29)

**Environment:** Windows 11 host; API `dotnet run --launch-profile http` on `http://localhost:5076` with
user-secret configuration; MongoDB 8.0 single-node replica set in Docker (`solargrid-dev-mongo`, local only);
web Vite dev server on `http://127.0.0.1:5173` in Chrome; Android debug build on the Pixel_9 emulator
(API 36). All accounts were synthetic (`2099000000xx` NICs, `@example.com` emails, generated test-only
passwords). Evidence is the tester's on-screen observation plus server-side state read from MongoDB after each
step (status and flag fields of the synthetic accounts only). No screenshots are stored in the repository.

| # | Checkpoint | Result | Sanitized evidence |
| --- | --- | --- | --- |
| 1 | Backoffice bootstrap and web login | **Passed** | Bootstrap run logged "Initial Backoffice account created"; exactly 1 Backoffice in the database; API restarted without bootstrap settings (no bootstrap log lines, temporary password copy deleted). After a hard reload, sign-in from `/login` landed on `/users` ("Account administration"). A first attempt in a stale tab showed Home; the same flows traced in a clean headless Chrome all reached `/users`. |
| 2 | Android registration | **Passed** (pending-login sub-step Not Verified) | Registration showed "Awaiting activation"; the account appeared in the web Pending activation list. The tester did not separately confirm the second pending-login check. |
| 3 | Web activation | **Passed** | Activated from the Pending activation tab; database: status Active, 0 pending. |
| 4 | Android login and profile | **Passed** (rotation sub-step Not Verified) | Login opened the Prosumer home. Profile loaded server data; saved name, phone and email changes were confirmed in the database; old email rejected, new email signed in. Initially blocked by the menu defect below (fixed and re-tested). The rotation step was not confirmed. |
| 5 | Deactivation request | **Passed** | Confirmation dialog shown; app showed "Requested on … awaiting Backoffice review" and status Active; database: flag and timestamp set, status Active; web badge 1 and "Requested". Duplicate prevention observed as a disabled button (API 409 for duplicates is covered by the HTTP runtime check). |
| 6 | Web approval and Android lockout | **Passed** | Approved from Deactivation requests; database: Deactivated, request cleared, 0 pending. After refresh/restart the app ended the session. While left open on the Member 4 Prosumer dashboard the app showed "Access denied" instead of signing out (see Blockers). |
| 7 | Reactivation and role protection | **Not Verified** (skipped by tester) | Database after the step: account still Deactivated, 0 stations, 0 Grid Operators. Related API behaviour is covered only by the HTTP runtime check. |
| 8 | Accessibility and responsive UI | **Partly passed** | Keyboard: confirmation dialog opened and closed with Escape (Passed); keyboard confirm did not change the account (Not Verified). Phone width (375 px) layouts Passed. TalkBack Not Verified. |

### Defect found and fixed during manual verification

| Defect | Cause | Fix | Verification |
| --- | --- | --- | --- |
| Android ☰ menu button could not be tapped | On Android 15+ with targetSdk 36 the app is drawn edge-to-edge; the app-shell toolbar sat under the status bar (button at y 21–147 px inside a 137 px status bar), so the system took the taps | `android:fitsSystemWindows="true"` on the shell content container (`activity_main.xml`) and on the login, registration and account-notice screens | Rebuilt and reinstalled: toolbar starts at y 142, button at y 152–299 (clear of the status bar); tester opened the menu and completed Checkpoints 4–6 |

## Requirement traceability

| # | Requirement | Backend evidence | Web evidence | Android evidence | Test evidence | Status | Remaining manual action | Owner |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| B1 | MongoDB settings and context | `Settings/MongoSettings.cs`, `Data/MongoDbContext.cs`, unique email index | — | — | Health `database: Healthy`; 73 integration tests; manual run | Complete | — | Member 1 (shared file) |
| B2 | JWT authentication | `Helpers/JwtHelper.cs`, validation in `Program.cs` | — | — | Token tests; runtime 401 for anonymous; manual logins | Complete | Use a fresh key in every environment (see Security) | Member 1 |
| B3 | Role-based authorization | `[Authorize(Roles)]` on Member 1 controllers | — | — | Policy tests; runtime 403 for wrong roles | Complete | — | Member 1 |
| B4 | Current-status recheck for unexpired tokens | `Filters/RequireActiveAccountAttribute.cs`; `ProsumerService` | — | — | Filter tests; runtime deactivated → 403, reactivated → 200; manual Checkpoint 6 | Complete | — | Member 1 |
| B5 | User entity (NIC key, roles, status) | `Models/Entities/User.cs` | — | — | Tests; runtime; manual | Complete | — | Member 1 |
| B6 | Prosumer registration | `AuthService.RegisterAsync` | — | `RegisterActivity` | Runtime 201/409/400; manual Checkpoint 2 | Complete | — | Member 1 |
| B7 | Login (pending/deactivated refused) | `AuthService.LoginAsync` | — | — | Runtime pending 403 with exact message; manual | Complete | — | Member 1 |
| B8 | Backoffice and Grid Operator creation | `UserService.CreateStaffUserAsync` | — | — | Tests; runtime 201 | Complete | Manual web creation of a Grid Operator (Checkpoint 7) not performed | Member 1 |
| B9 | Initial Backoffice bootstrap | `Data/BackofficeBootstrapInitializer.cs` | — | — | Tests; runtime; manual Checkpoint 1 (created once, then disabled) | Complete | — | Member 1 |
| B10 | Profile read/update | `ProsumersController`, `ProsumerService` | — | — | Tests; runtime; manual Checkpoint 4 (server confirmed) | Complete | — | Member 1 |
| B11 | Deactivation request (no duplicates) | `ProsumerService.RequestDeactivationAsync` | — | — | Runtime 200 then 409; manual Checkpoint 5 | Complete | — | Member 1 |
| B12 | Activate / reactivate / deactivate transitions | `UserService.ActivateAsync`, `DeactivateAsync` | — | — | Transition tests; runtime; manual activate and approve | Complete | — | Member 1 |
| B13 | Deactivation-request list | `UserService.GetDeactivationRequestsAsync` | — | — | Tests; runtime; manual Checkpoints 5–6 | Complete | — | Member 1 |
| B14 | Grid Operator station assignment | `UserService.AssignStationAsync` | — | — | Tests; runtime 400/404/400 | Complete | A positive assignment with a real station was not performed | Member 1 / Member 2 |
| B15 | Shared error format | `ExceptionMiddleware` (shared) | — | — | Middleware tests; runtime `{status, message}` | Complete | — | Shared |
| B16 | Headers and method-start comments on Member 1 C# files | 23 API files + 7 test files | — | — | Scripted audit: 0 problems | Complete | — | Member 1 |
| W1 | Login and role redirect | — | `pages/LoginPage.tsx`, `auth/roleRouting.ts` | — | Tests; manual Checkpoint 1 (Backoffice → `/users`) | Complete | Grid Operator and Prosumer web redirects not checked manually | Member 1 |
| W2 | User list with filters | — | `UserManagementPage.tsx` | — | Tests; manual list, "You" badge, "Requested" badge | Complete | — | Member 1 |
| W3 | Create staff with station picker | — | `CreateStaffPage.tsx` | — | Tests | Not Runtime Verified | Checkpoint 7 C | Member 1 / Member 2 |
| W4 | Pending activation page | — | `PendingActivationsPage.tsx` | — | Tests; manual Checkpoint 3 | Complete | — | Member 1 |
| W5 | Activate and approve deactivation with confirmation | — | `accountActions.ts`, `useAccountAdministration.ts` | — | Tests; manual Checkpoints 3 and 6 | Complete | — | Member 1 |
| W6 | Reactivate from the web | — | `UserManagementPage.tsx` | — | Tests; runtime API reactivation | Not Runtime Verified | Checkpoint 7 A | Member 1 |
| W7 | Deactivation-request view | — | `DeactivationRequestsPage.tsx` | — | Tests; manual Checkpoints 5–6 | Complete | — | Member 1 |
| W8 | Station assignment dialog | — | `StationAssignmentDialog.tsx` | — | Tests | Not Runtime Verified | Needs a real station (Checkpoint 7 B–C) | Member 1 / Member 2 |
| W9 | Role-protected routes (direct URLs) | — | `App.tsx`, `RoleRoute` | — | Direct-URL tests | Not Runtime Verified | Checkpoint 7 D | Member 1 |
| W10 | Prosumer web scope | — | `/prosumer` notice page | — | Route tests | Blocked (partial) | Prosumers can open the Member 2/3 Stations and Reservations web routes | Members 2 & 3 |
| W11 | Keyboard and phone-width UI | — | Dialogs, responsive tables and forms | — | Tests; manual Checkpoint 8 (dialog open/Escape, 375 px) | Complete | Keyboard confirm not observed to complete | Member 1 |
| A1 | Registration screen | — | — | `RegisterActivity`, `RegistrationController` | Tests; manual Checkpoint 2 | Complete | — | Member 1 |
| A2 | Pending-activation screen | — | — | `PendingActivationActivity`, `AccountRoutePolicy` | Tests; manual after registration | Complete | Pending-login repeat not confirmed | Member 1 |
| A3 | Login and role home | — | — | `LoginActivity`, `StartupActivity`, `MainActivity` | Tests; manual Prosumer login | Complete (Prosumer) / Not Runtime Verified (Grid Operator, Backoffice web-only screen) | Checkpoint 7 E | Member 1 |
| A4 | Profile view and edit | — | — | `ProfileFragment`, `ProfileController` | Tests; manual Checkpoint 4 (server confirmed) | Complete | Rotation not confirmed | Member 1 |
| A5 | Deactivation request with confirmation | — | — | `ProfileFragment`, `ProfileController` | Tests; manual Checkpoint 5 | Complete | — | Member 1 |
| A6 | SQLite session storage and cache | — | — | `SessionStore`, `AuthRepository.cacheProfile` | Instrumented 3/3; manual session restore | Complete | — | Member 1 |
| A7 | Logout and inactive-session handling | — | — | `MainActivity`, `StartupActivity`, `ApiClient` | Tests; manual Checkpoint 6 after restart | Complete | Live sign-out from the Member 4 dashboard depends on Member 4 | Member 1 / Member 4 |
| A8 | App shell usable on Android 15+ (edge-to-edge) | — | — | `activity_main.xml` and Member 1 screen roots (`fitsSystemWindows`) | Manual re-test after fix | Complete | — | Member 1 |
| A9 | Native only | — | — | Java/AndroidX/XML | Build | Complete | — | Member 1 |

## External blockers and findings (not Member 1 code)

| Item | Owner | Effect |
| --- | --- | --- |
| Android lint `UnsafeOptInUsageError` in `ui/operations/OperatorScannerFragment.java:295` | Member 4 | `lintDebug` fails |
| Android lint `PermissionImpliesUnsupportedChromeOsHardware` at `AndroidManifest.xml:5` | Member 4 | `lintDebug` fails |
| Prosumer dashboard shows "Access denied" for a deactivated account instead of signing out; `MainActivity.handleAccountNoLongerActive(error)` is available to call | Member 4 | Lockout happens on the next app start instead of immediately on that screen |
| Prosumers can open the web Stations and Reservations routes | Members 2 & 3 | Integration decision needed |
| `ExceptionMiddleware` logs client-cancelled requests (`TaskCanceledException`) as unhandled 500 errors | Shared (team) | Log noise only; no user impact |
| Web top bar shows the signed-in name in low-contrast text | Shared web layout | Readability of the name in the header |
| No endpoint to reject a deactivation request or to clear a Grid Operator's station | Team decision | Not in the brief |

## Security notes

- Historical commits `8460266`, `afec0ad`, `74e4a08` and `91ab0b4` contain a JWT signing key in
  `appsettings.json`. Those values must be treated as exposed and never used. Current tracked files contain
  empty keys; local development uses a freshly generated key in user secrets.
- Historical MongoDB URIs (`678d517`, `afec0ad`) point to localhost with no embedded credentials.
- No secrets, `.env` files, APKs, build output or screenshots are tracked on this branch.
- The local development database contains one non-synthetic Prosumer record created before this
  verification; it is not part of any evidence and is not in the repository.

## Conclusion

The backend is **complete and runtime verified**. The main web and Android account journeys (Backoffice
login, Prosumer registration, activation, profile update with email change, deactivation request, approval and
lockout) **passed manual verification**, and one Android defect found during it was fixed. Member 1 is **not
fully complete**: Checkpoint 7 (reactivation from the web, Grid Operator creation with a station, direct-URL
role checks, Backoffice on Android) and the rotation, keyboard-confirm and TalkBack checks are still Not Verified.
