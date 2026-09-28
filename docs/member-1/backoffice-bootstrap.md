# Initial Backoffice account bootstrap

Backoffice accounts can only be created by an existing Backoffice user (`POST /api/users`).
The API can create the very first one at start-up from configuration. The bootstrap is off by default.

## How it behaves

- It does nothing unless `BootstrapBackoffice:Enabled` is `true`.
- If **any** Backoffice account already exists, it logs a message and does nothing. It never
  creates a second account, and never changes, resets or elevates an existing one.
- If the configured NIC or email already belongs to another account, it logs a warning and skips
  instead of taking that account over.
- When enabled, the settings are checked when the API starts. A missing or invalid value stops
  start-up with an error that names the setting but never shows its value.
- The password is hashed with the shared BCrypt `PasswordHasher` and is never logged.
- The account is created as `Backoffice` with status `Active`.

## Configuration

Provide the values through **environment variables** or **.NET user secrets**. Never put them in
a tracked `appsettings*.json` file. The tracked `appsettings.json` only contains
`"BootstrapBackoffice": { "Enabled": false }`.

| Environment variable | Rule |
| --- | --- |
| `BootstrapBackoffice__Enabled` | `true` to run the bootstrap; anything else leaves it off |
| `BootstrapBackoffice__Nic` | 9 digits + `V`/`X`, or 12 digits |
| `BootstrapBackoffice__FullName` | Required, at most 100 characters |
| `BootstrapBackoffice__Email` | Valid email, at most 100 characters (used to sign in) |
| `BootstrapBackoffice__Phone` | Required, at most 20 characters |
| `BootstrapBackoffice__Password` | 12 to 100 characters |

PowerShell example. The values in angle brackets are placeholders; replace them locally and do
not commit them:

```powershell
$env:BootstrapBackoffice__Enabled  = "true"
$env:BootstrapBackoffice__Nic      = "<backoffice-nic>"
$env:BootstrapBackoffice__FullName = "<backoffice-full-name>"
$env:BootstrapBackoffice__Email    = "<backoffice-email>"
$env:BootstrapBackoffice__Phone    = "<backoffice-phone>"
$env:BootstrapBackoffice__Password = "<strong-password-12-plus-chars>"
dotnet run --project SolarMicrogrid.API
```

User-secrets alternative (stored outside the repository):

```powershell
dotnet user-secrets --project SolarMicrogrid.API set "BootstrapBackoffice:Enabled" "true"
dotnet user-secrets --project SolarMicrogrid.API set "BootstrapBackoffice:Password" "<strong-password-12-plus-chars>"
```

## After the first run

1. Sign in with the configured email and password, and confirm the account works.
2. Set `BootstrapBackoffice__Enabled` back to `false` (or remove it), and remove
   `BootstrapBackoffice__Password` from the environment or secrets store.
3. Create any further Backoffice and Grid Operator accounts through the user-management endpoints.
