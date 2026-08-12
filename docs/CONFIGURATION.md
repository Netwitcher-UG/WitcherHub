# WitcherHub configuration

All configuration is read through the standard ASP.NET Core provider chain, in
increasing order of precedence:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. User secrets (Development only)
4. Environment variables

**No secret belongs in a file that is committed.** The JSON files in this
repository hold non-secret defaults and empty placeholders only. Supply real
values through user secrets locally and environment variables on Railway.

In environment-variable form, a nested key is written with a double underscore:
`Lexware:AccessToken` becomes `Lexware__AccessToken`.

## Required

The application refuses to start without these, naming whichever is missing.

| Variable | Purpose |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string. |
| `Jwt__Key` | Signing key for access tokens. Minimum 32 characters; use a random value, not a passphrase. |

## Optional integrations

Each of these disables one feature when absent. The start-up log states which
features are configured and which are off; values are never logged.

| Variable | Feature when set | Behaviour when empty |
| --- | --- | --- |
| `Lexware__AccessToken` | Customer and invoice sync with Lexware. | Lexware calls fail; the rest of the app works. |
| `LexwareWebhooks__PublicKeyPem` | Verifies webhook signatures. | Every webhook is rejected with 401. Signature checking fails closed by design. |
| `OpenAI__ApiKey` | AI-assisted contract drafting. | Draft generation is unavailable. |
| `Smtp__UserName`, `Smtp__Password`, `Smtp__FromEmail` | Outgoing mail: quote, contract and invoice notifications. | Mail sending fails. |

## Other settings

| Variable | Notes |
| --- | --- |
| `Jwt__AccessTokenMinutes` | Session lifetime. Defaults to 480 (8 hours). |
| `WITCHERHUB_PUBLIC_BASE_URL` | Absolute base URL used to build customer-facing signing and invoice links. Must match the deployed host or the links in emails will point at the wrong environment. |
| `OpenAI__Model` | Model id used for contract drafting. |
| `Swagger__Enabled` | Exposes Swagger outside Development. Leave off in production. |
| `SeedAdmin__Email`, `SeedAdmin__Password` | Used only when the users table is empty, to create the first administrator. Outside Development the seeder refuses to invent credentials and logs an error instead. |

## Local setup

```bash
cd WitcherHub
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=witcherhub;Username=postgres;Password=..."
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"
dotnet user-secrets set "Lexware:AccessToken" "..."
dotnet user-secrets set "OpenAI:ApiKey" "..."
```

User secrets live outside the repository, so they cannot be committed by accident.

## Health endpoints

| Path | Meaning |
| --- | --- |
| `/health` | Liveness: the process is accepting requests. Runs no checks. |
| `/health/ready` | Readiness: PostgreSQL is reachable. |

Both are anonymous so the platform can probe them without credentials.
