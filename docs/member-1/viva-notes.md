# Member 1 viva notes

Short answers to likely questions. Each point names the file to open if asked to show it.

## Architecture

- **Why a central API?** Web and Android are UI-only REST clients. All rules (status transitions, ownership,
  validation) live in the C# API, so both clients behave the same and neither touches MongoDB.
- **Layers:** thin controllers → services (business rules) → `MongoDbContext`. Errors are domain exceptions
  turned into `{status, message}` by `ExceptionMiddleware`.

## Identity and security

- **NIC as primary key:** `User.Nic` is the `[BsonId]`; it is normalised to upper case (`…V/X`) on every path.
- **Passwords:** BCrypt, work factor 12 (`Helpers/PasswordHasher.cs`). Never returned, never stored on Android.
- **JWT:** HS256, issuer/audience/lifetime validated (`Program.cs`); claims are NIC, email, name, role
  (`Helpers/JwtHelper.cs`). The key is at least 32 characters and comes from user secrets or the environment.
- **Why recheck status if the JWT is valid?** A token lives up to 60 minutes. `[RequireActiveAccount]`
  (`Filters/RequireActiveAccountAttribute.cs`) loads the account on each Member 1 request, so a deactivated
  user is locked out immediately. It runs after `[Authorize]`, so JWT validation is unchanged.
- **Ownership:** `/api/prosumers/me` never takes a NIC from the route or body; it uses the token's NIC.

## Account lifecycle

- Register → **PendingActivation** (no login) → Backoffice activates → **Active**.
- Prosumer requests deactivation → flag and timestamp set, still **Active** → Backoffice approves →
  **Deactivated** (flag cleared) → Backoffice can reactivate.
- Rejected transitions (409): activate an Active account, deactivate a Deactivated or Pending account.
  Also blocked: deactivating yourself (400) and deactivating the last active Backoffice (409).
- Writes are atomic `FindOneAndUpdate` calls guarded by the expected current status, so concurrent requests
  cannot apply a transition twice.

## First Backoffice account

`Data/BackofficeBootstrapInitializer.cs`: off unless `BootstrapBackoffice:Enabled=true`; creates one Active
Backoffice only when no Backoffice exists; hashes the password; never logs values; never takes over an existing
account. Credentials come from environment variables only.

## Grid Operator station

`PATCH /api/users/{nic}/station` accepts only a valid, existing, **active** Member 2 station and only for Grid
Operators. The ID is stored in canonical lower case because Member 3/4 compare it by exact string.

## Web

- Login redirects by role: Backoffice → `/users`, Grid Operator → operator dashboard, Prosumer → mobile-app
  notice (`auth/roleRouting.ts`). Deep links are kept; `/login` and `/forbidden` never loop.
- Admin routes are protected by `RoleRoute` (direct URLs too), not only hidden in the menu.
- The UI hides impossible actions but the API decides; the API's message is shown on refusal.

## Android

- Pure native Java/AndroidX/XML. Logic sits in plain-Java controllers (`RegistrationController`,
  `ProfileController`, `AccountRoutePolicy`) so it is unit-tested without a device.
- Backoffice on mobile: blocked by design (web responsibility); the session is cleared.
- SQLite keeps one session row (token, NIC, name, email, role, status). Profile values confirmed by the API
  refresh the cached name/email/status; the token is never changed locally.
- Pending/deactivated are recognised from the API's fixed 403 messages (`AccountStatusPolicy`).

## Testing

Backend unit tests with mocked MongoDB plus 73 integration tests on a real replica set; a 42-step HTTP runtime
check; web static-render and request tests; Android JVM tests and instrumented SQLite tests on an emulator.

## Known limits (be ready to explain)

- No endpoint to reject a deactivation request or to unassign a station (not in the brief).
- Two Android lint errors belong to Member 4's QR scanner.
- Prosumer access to the Member 2/3 web routes needs an owner decision.
- An old JWT key exists in Git history; it must never be used, and every environment sets its own key.
