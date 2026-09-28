# Member 1 contribution summary

**Member:** H.A.S MADUWANTHA (IT23472020)
**Component:** Authentication, JWT roles, user and Prosumer account management, Android SQLite session

## Foundation (branch `IT23472020_Sanka`, merged into `main`)

| Commit | Summary |
| --- | --- |
| `a941a37` | User entity with role and status enums |
| `678d517`, `afec0ad`, `cb76952` | MongoDB settings, JWT settings and authentication DTOs |
| `8460266` | JWT authentication and BCrypt password hashing |
| `46f06d9` | Registration, login and current-user endpoint |
| `4132887`, `ffad297` | Backoffice staff user management |

## Completion work (branch `feature/auth-account-management`)

| Commit | Summary |
| --- | --- |
| `ed198d9` | API: Prosumer profile read/update and deactivation request (`/api/prosumers/me`) |
| `098fa3a` | API: deactivation-request list, status-aware activate/reactivate/deactivate, Grid Operator station assignment, initial Backoffice bootstrap, active-account recheck for unexpired tokens |
| `64f5933` | Web: role-based login redirects, Backoffice user management, pending activation, deactivation requests, station assignment |
| `5ffd2a6` | Android: removed duplicate string resources left by an earlier merge (build fix) |
| `d1fd703` | Android: Prosumer registration, pending activation, Backoffice web-only screen, server-authoritative profile edit, deactivation request |
| *(Phase 5, uncommitted at time of writing)* | Standard headers and method comments on remaining Member 1 C# files; final audit documents |

Note: the completion commits were created with the Git identity configured on the development machine.

## What Member 1 delivers

- **API:** `/api/auth/register`, `/api/auth/login`, `/api/auth/me`; `/api/users` (create staff, list, pending,
  deactivation requests, activate, deactivate, assign station); `/api/prosumers/me` (get, update,
  deactivation request). Status transitions and ownership are enforced on the server.
- **Web:** Backoffice-only account administration under `/users`, role-based landing pages and navigation.
- **Android:** registration, pending activation, login routing, profile and deactivation request, reusing the
  shared `ApiClient` and SQLite `SessionStore` used by Members 2–4.
- **Reused by other members:** `JwtHelper`, role claims, `[RequireActiveAccount]`, the Android session store and
  the `AssignedStationId` field used by Member 3 and Member 4 station scoping.

## Tests

144 Member 1 backend unit tests, 78 Member 1 web tests, 48 Member 1 Android unit tests and 3 instrumented
SQLite tests, plus a 42-step HTTP runtime check against a real MongoDB (see `final-audit.md`).

## Disclosure

AI-use disclosure and source references: to be completed by the member according to the university policy.
