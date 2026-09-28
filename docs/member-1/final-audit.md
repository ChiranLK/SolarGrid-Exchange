# Member 1 final audit

**Member:** H.A.S MADUWANTHA (IT23472020) · **Branch:** `feature/auth-account-management` · **Audit date:** 2026-09-29

Scope: authentication, JWT roles, user and Prosumer account management, web login and Backoffice user
management, Android registration/login/profile/deactivation request and SQLite session storage.

Status values: **Complete** (implemented and verified at runtime), **Not Runtime Verified** (implemented
and covered by automated tests, but not yet exercised end to end on a real device/browser), **Blocked**
(needs another owner or an external action).

## Evidence summary (this audit run)

| Check | Result |
| --- | --- |
| `dotnet build SolarMicrogrid.slnx -c Release` | Succeeded, 0 errors (2 NU1900 offline NuGet-audit warnings) |
| `SolarMicrogrid.Tests` (unit, no database) | **213 / 213 passed** (144 Member 1 tests) |
| `scripts/run-component3-tests.ps1` (`SolarMicrogrid.API.Tests`, real MongoDB 8.0 replica set in a throwaway Docker container, synthetic data) | **73 / 73 passed** |
| Member 1 HTTP runtime check (temporary API instance + throwaway database, synthetic accounts, deleted afterwards) | **42 / 42 checks passed** |
| Web `npm run lint` / `npm test` / `npx tsc -b` / production build | 0 problems / **129 / 129** (78 Member 1) / pass / pass |
| Android `assembleDebug` / `testDebugUnitTest` | pass / **78 / 78** (48 Member 1) |
| Android `connectedDebugAndroidTest` (Pixel_9 emulator, API 36) | **3 / 3 passed** (SQLite session store) |
| Android `lintDebug` | Fails **only** on 2 existing Member 4 errors (see Blockers); Member 1 adds no lint errors or warnings |
| `scripts/run-member4-android-unit-tests.ps1` | 17 / 17 passed |
| Member 1 C# header / method-comment audit (23 API files, 7 test files) | 0 problems |

## Requirement traceability

| # | Requirement | Backend evidence | Web evidence | Android evidence | Test evidence | Status | Remaining manual action | Owner |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| B1 | MongoDB settings and context | `Settings/MongoSettings.cs`, `Data/MongoDbContext.cs`, unique email index | — | — | Health check `database: Healthy`; 73 integration tests | Complete | — | Member 1 (shared file) |
| B2 | JWT authentication | `Helpers/JwtHelper.cs`, JWT validation in `Program.cs` | — | — | `ProsumersControllerTests` token round trip; runtime 401 for anonymous | Complete | Use a fresh key in every environment (see Security) | Member 1 |
| B3 | Role-based authorization | `[Authorize(Roles)]` on `UsersController`, `ProsumersController`, `AuthController.Me` | — | — | Policy tests for every action; runtime 403 for wrong roles | Complete | — | Member 1 |
| B4 | Current-status recheck for unexpired tokens | `Filters/RequireActiveAccountAttribute.cs`; status checks in `ProsumerService` | — | — | `ActiveAccountFilterTests`; runtime: deactivated token → 403, reactivated → 200 | Complete | — | Member 1 |
| B5 | User entity (NIC primary key, roles, status) | `Models/Entities/User.cs` | — | — | Registration/lookup tests; runtime | Complete | — | Member 1 |
| B6 | Prosumer registration (NIC key, PendingActivation) | `AuthController.Register`, `AuthService.RegisterAsync` | — | `RegisterActivity` | Runtime 201 / 409 duplicate / 400 field errors | Complete | — | Member 1 |
| B7 | Login (pending/deactivated refused) | `AuthService.LoginAsync` | — | — | Runtime: pending 403 with the exact Android message; login after activation | Complete | — | Member 1 |
| B8 | Backoffice and Grid Operator creation | `UserService.CreateStaffUserAsync`, optional station | — | — | `UserAdministrationServiceTests`; runtime 201 | Complete | — | Member 1 |
| B9 | Initial Backoffice bootstrap (config-driven, off by default, idempotent) | `Data/BackofficeBootstrapInitializer.cs`, `docs/member-1/backoffice-bootstrap.md` | — | — | `BackofficeBootstrapInitializerTests`; runtime: created, then used to log in | Complete | — | Member 1 |
| B10 | Profile read/update (own data, allowed fields, email uniqueness) | `ProsumersController`, `ProsumerService` | — | — | `ProsumerServiceTests`; runtime: trimmed update, NIC unchanged, 409 duplicate email, login with new email | Complete | — | Member 1 |
| B11 | Deactivation request (no duplicates) | `ProsumerService.RequestDeactivationAsync` | — | — | Runtime 200 then 409; account stays Active | Complete | — | Member 1 |
| B12 | Activation / reactivation / deactivation with valid transitions | `UserService.ActivateAsync`, `DeactivateAsync` | — | — | Transition tests; runtime: activate, 409 re-activate, deactivate, 409 re-deactivate, reactivate, 400 self-deactivate | Complete | — | Member 1 |
| B13 | Deactivation-request list for Backoffice | `UserService.GetDeactivationRequestsAsync` | — | — | Tests; runtime listed then cleared after approval | Complete | — | Member 1 |
| B14 | Grid Operator station assignment (existing active station only) | `UserService.AssignStationAsync` | — | — | Tests; runtime 400 invalid ID, 404 unknown station, 400 wrong role | Complete | A positive runtime assignment needs a real station (Member 2 data) | Member 1 / Member 2 |
| B15 | Shared error format | `ExceptionMiddleware` (shared) | — | — | Middleware tests; runtime `{status, message}` bodies | Complete | — | Shared |
| B16 | Header block and method-start comments on Member 1 C# files | 23 API files + 7 test files | — | — | Scripted audit: 0 problems | Complete | Shared files (`Program.cs`, `MongoSettings`, `MongoDbContext`, `ExceptionMiddleware`, `ApiExceptions`) keep their shared headers | Member 1 |
| W1 | Login page and role redirect after sign-in | — | `pages/LoginPage.tsx`, `auth/roleRouting.ts` | — | `roleRouting.test.tsx` (redirects, deep links, no loops, no flash) | Not Runtime Verified | Browser check per role | Member 1 |
| W2 | Backoffice user management (list, filters, create staff, station picker) | — | `features/users/UserManagementPage.tsx`, `CreateStaffPage.tsx` | — | `userAdmin.test.ts`, `userAdminRendering.test.tsx` | Not Runtime Verified | Browser check against the running API | Member 1 |
| W3 | Pending activation page | — | `PendingActivationsPage.tsx` | — | Rendering and request tests | Not Runtime Verified | Browser check | Member 1 |
| W4 | Activate / reactivate / deactivate with confirmation | — | `accountActions.ts`, `useAccountAdministration.ts` | — | Action + refresh tests | Not Runtime Verified | Browser check | Member 1 |
| W5 | Deactivation-request view and approval | — | `DeactivationRequestsPage.tsx` | — | Rendering and request tests | Not Runtime Verified | Browser check; reject flow not supported by the API (by design) | Member 1 |
| W6 | Station assignment dialog | — | `StationAssignmentDialog.tsx` | — | Rendering tests | Not Runtime Verified | Browser check with a real station | Member 1 / Member 2 |
| W7 | Role-protected routes and navigation | — | `App.tsx`, `routes/RoleRoute.tsx`, nav entries | — | Direct-URL tests for every admin route and role | Not Runtime Verified | Browser direct-URL check | Member 1 |
| W8 | Prosumer web scope | — | `/prosumer` notice page | — | Route tests | Blocked (partial) | Prosumers can still open the Member 2/3 Stations and Reservations web routes; needs an owner decision | Members 2 & 3 |
| W9 | UI states and accessibility | — | Shared state components, labelled fields, dialogs | — | Rendering tests | Not Runtime Verified | Keyboard, screen-reader and phone-width checks | Member 1 |
| A1 | Registration screen | — | — | `ui/auth/RegisterActivity.java`, `RegistrationController` | `RegistrationFormTest`, `RegistrationControllerTest`; API contract verified at runtime | Not Runtime Verified | Register on emulator against the running API | Member 1 |
| A2 | Pending-activation screen and routing | — | — | `PendingActivationActivity`, `AccountRoutePolicy` | `AccountRoutePolicyTest`; exact 403 message verified at runtime | Not Runtime Verified | Emulator check after registration and pending login | Member 1 |
| A3 | Login and role home (Backoffice web-only) | — | — | `LoginActivity`, `StartupActivity`, `MainActivity`, `BackofficeWebOnlyActivity` | Route policy tests; Member 4 `RoleRoutePolicyTest` unchanged | Not Runtime Verified | Emulator check per role | Member 1 |
| A4 | Profile view and edit | — | — | `ProfileFragment`, `ProfileController`, `UserRepository` | `ProfileControllerTest`, `ProfileContractTest` | Not Runtime Verified | Emulator edit, rotation and email-change check | Member 1 |
| A5 | Deactivation request with confirmation | — | — | `ProfileFragment` dialog, `ProfileController` | Controller tests (success, 409, repeat taps) | Not Runtime Verified | Emulator check | Member 1 |
| A6 | SQLite session storage and cache | — | — | `SessionStore`, `AuthRepository.cacheProfile` | **Instrumented 3/3 on emulator** | Complete | — | Member 1 |
| A7 | Logout and expired-session handling | — | — | `MainActivity`, `StartupActivity`, `ApiClient` 401 clearing | Route policy tests | Not Runtime Verified | Emulator check with an expired or deactivated token | Member 1 |
| A8 | Native only (no cross-platform frameworks) | — | — | Java/AndroidX/XML, `build.gradle` | Build | Complete | — | Member 1 |

## External blockers (not Member 1 code)

| Blocker | Owner | Effect |
| --- | --- | --- |
| Android lint error `UnsafeOptInUsageError` in `ui/operations/OperatorScannerFragment.java:295` (CameraX `ExperimentalGetImage`) | Member 4 | `lintDebug` fails |
| Android lint error `PermissionImpliesUnsupportedChromeOsHardware` at `AndroidManifest.xml:5` (camera permission without `<uses-feature android:required="false">`) | Member 4 | `lintDebug` fails |
| Prosumers can open the web Stations and Reservations routes (open to every signed-in role) | Members 2 & 3 | Integration decision on whether the web should serve Prosumers |
| No API endpoint to reject a deactivation request or to clear a Grid Operator's station | Team decision | Not in the brief; documented, not implemented |

## Security notes

- Historical commits `8460266`, `afec0ad`, `74e4a08` and `91ab0b4` contain a JWT signing key in
  `appsettings.json`. Those values must be treated as exposed: never use them in any environment. The
  current tracked files contain empty keys; local development uses a freshly generated key in user secrets.
- Historical MongoDB URIs (`678d517`, `afec0ad`) point to localhost with no embedded credentials.
- No secrets, `.env` files, APKs, build output or screenshots are tracked on this branch.

## Conclusion

The backend part of Member 1 is **complete and runtime verified** against a real MongoDB. The web and Android
parts are implemented and fully covered by automated tests, but **Member 1 is not yet fully complete**: the
browser and on-device end-to-end checks in `manual-test-checklist.md` are still outstanding, and the listed
external blockers belong to other owners.
