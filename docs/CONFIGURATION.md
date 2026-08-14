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
| `WITCHERHUB_PUBLIC_BASE_URL` | Absolute base URL for every link this environment emails. **Must differ per environment** — see "Links point at the wrong environment" below. A missing scheme is promoted to `https://`. Password reset fails with a clear message when it is unset. |
| `OpenAI__Model` | Model id used for contract drafting. |
| `Swagger__Enabled` | Exposes Swagger outside Development. Leave off in production. |
| `SeedAdmin__Email`, `SeedAdmin__Password` | Used only when the users table is empty, to create the first administrator. Outside Development the seeder refuses to invent credentials and logs an error instead. |
| `BootstrapAdmin__Email` | Address guaranteed to hold the `Admin` role. Defaults to `info@netwitcher.com` in `appsettings.json`. See "Administrator accounts" below. |
| `BootstrapAdmin__Password` | Optional. When omitted, the account is created with a random password and activated through password reset. |
| `Auth__ShowSignInDiagnostics` | Shows the real reason a sign-in failed on the login page. Off outside Development. See "Reading a sign-in failure" below. |

## Local setup

```bash
cd WitcherHub
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=witcherhub;Username=postgres;Password=..."
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"
dotnet user-secrets set "Lexware:AccessToken" "..."
dotnet user-secrets set "OpenAI:ApiKey" "..."
```

User secrets live outside the repository, so they cannot be committed by accident.

## Links point at the wrong environment

Every link the application emails — password resets, quote and contract signing,
invoices — is built from `WITCHERHUB_PUBLIC_BASE_URL` on the environment that
**sends** the mail. Each environment needs its own value:

| Environment | Value |
| --- | --- |
| Dev | `https://witcherhubdev-dev.up.railway.app` |
| Production | `https://hub.netwitcher.com` |

If dev carries the production URL, a reset requested on dev emails a link to
production. Following it lands on whatever production is running — a 404 if
production has not yet been updated with the page being linked to.

The host is taken from configuration and never from the request `Host` header:
building it from the request would let a forged header send a reset link, token
included, to an attacker's site. The cost of that choice is that a wrong value
has no visible symptom, so it is reported in two places:

- **At start-up**, naming the environment:
  `Links emailed by this environment will point at https://hub.netwitcher.com (Development)`
  — production URL next to `Development` means the value is wrong.
- **Per request**, when the host a reset is requested on differs from the host
  the link will use:
  `Password reset requested on witcherhubdev-dev.up.railway.app but the emailed
  link points at hub.netwitcher.com`.

## Administrator accounts

There is one role, `Admin`, and it is granted **every** permission in
`AppPermissions` automatically. It is therefore the highest privilege level the
application has — there is no separate "super admin" tier above it, and adding a
new role would give that role *fewer* rights, not more, until it is listed in
`AppRolePermissions.Map`.

Two mechanisms create administrators:

| | `SeedAdmin__*` | `BootstrapAdmin__*` |
| --- | --- | --- |
| Runs | Only when the users table is empty | Every start-up |
| Works on a populated database | No | Yes |
| Existing account | n/a | Keeps its password, gains the role if missing |
| Purpose | First account of a brand-new environment | Guarantee a nominated address stays an administrator |

`BootstrapAdmin` is idempotent and safe to leave configured permanently. It logs
at warning level whenever it grants the role to an account that already existed,
so an unexpected promotion is visible.

### Signing in bounces straight back to the login page

If the password is accepted but every page afterwards returns you to the login
form, the issued token is being rejected by the same application that issued it.

The usual cause is a missing `Jwt:Issuer` or `Jwt:Audience`: tokens are created
carrying them and validation requires them, so when they are absent sign-in
succeeds, sets a cookie, and then refuses that cookie on the next request. Both
now live in `appsettings.json` so every environment has them, and start-up fails
if either is empty.

Token rejections are logged:

```
Rejected the access token for /Index. The session will be treated as signed out.
```

### "Login failed. Check email/password." when the password is right

Each environment has its own database, so an account created or reset on dev does
not exist on production and vice versa. The start-up log lists what is actually in
the database the instance is connected to:

```
Accounts that can sign in to this database (2): admin@netwitcher.test [Admin], info@netwitcher.com [Admin]
```

If the address is missing from that line, sign-in cannot succeed there no matter
what password is used.

### Reading a sign-in failure

Every failed sign-in prints a code, a reference and a timestamp under the error
message:

```
Login failed. Check email/password.
AUTH-02   Reference C02D228D   2026-08-14 06:46:13 UTC
```

The sentence is identical for every cause, so the page cannot be used to discover
which addresses have accounts. The code identifies the cause exactly:

| Code | Cause | What to do |
| --- | --- | --- |
| `AUTH-01` | This database holds no user accounts at all. | Check `ConnectionStrings__DefaultConnection` — the instance is almost certainly pointed at the wrong database. Otherwise seeding never ran. |
| `AUTH-02` | Accounts exist, but none with that address. | The account is in the other environment's database. Compare against the start-up inventory line below. |
| `AUTH-03` | The account exists; the password did not match. | If a password set through "Forgot password?" stopped working after a deploy, check whether `BootstrapAdmin__ResetPasswordOnStartup` is still set — it overwrites the password on every start. |
| `AUTH-04` | The account is locked out. | Wait for the lockout to expire, or clear `LockoutEnd` on the account. |
| `AUTH-05` | The account exists but has no password stored. | Use "Forgot password?", or the break-glass procedure below. |
| `AUTH-500` | Not a credential problem: database unreachable, token signing rejected, a role lookup failing. | Read the log; the reference appears on the same line. |

The reference is random per attempt and written to the log with the code, so a
screenshot can be matched to the exact entry:

```
Sign-in failed for info@netwitcher.com. Code AUTH-02, reference C02D228D.
```

**To see the full reason on the page**, set `Auth__ShowSignInDiagnostics=true` on
that environment and try again. The page then adds the plain-English cause and the
facts behind it — which database host and name this instance is connected to, how
many accounts it holds, whether the address exists, whether it has a password,
whether it is locked out, its roles, and whether a start-up password override is
active for it — plus a **Copy details** button that puts the whole report on the
clipboard. It is on by default in Development.

Switch it off again afterwards. The report names the addresses that exist, which
is the one thing the generic message is there to withhold.

### Break-glass: setting the password of an account that already exists

Normal bootstrapping never touches an existing password, which leaves no way in
when reset email cannot be delivered. To force one:

```
BootstrapAdmin__Password=<new password>
BootstrapAdmin__ResetPasswordOnStartup=true
```

On the next start the password is overwritten through Identity's own reset, and
the log says so:

```
The password for info@netwitcher.com was overwritten from configuration on start-up.
Remove BootstrapAdmin__ResetPasswordOnStartup and BootstrapAdmin__Password now,
otherwise the password is reset on every deploy.
```

**Remove both variables once you are in.** While they remain set, every deploy
resets that password, and the password is sitting in the environment.

### First sign-in without putting a password in configuration

Leave `BootstrapAdmin__Password` unset. The account is then created with a random
password that is never logged or stored anywhere, and the start-up log says:

```
Bootstrap administrator info@netwitcher.com created with the Admin role and an
unknown random password. Use 'Forgot password?' on the login page to set one.
```

Go to the login page, use **Forgot password?**, and set the password from the
email. This needs `Smtp__*` configured on that environment — without working
email there is no way to activate the account, in which case set
`BootstrapAdmin__Password` instead and change it after signing in.

## Password reset

Self-service reset lives at `/Auth/ForgotPassword`, linked from the login page.
It works for any account, including administrators.

It depends on two things being configured:

- **`Smtp__*`** — the link is delivered by email, so nothing arrives without a
  working mail sender.
- **`WITCHERHUB_PUBLIC_BASE_URL`** — used to build an absolute link. If it is
  unset, the page reports that reset is not configured rather than sending an
  email containing a broken link. If it points at the wrong host, the email will
  send users to the wrong environment.

Reset links are valid for **2 hours** and can be used **once**. The lifetime is
set via `DataProtectionTokenProviderOptions` in `AddInfrastructure`.

The form deliberately shows the same confirmation whether or not the address has
an account, so it cannot be used to discover which addresses are registered.

## Health endpoints

| Path | Meaning |
| --- | --- |
| `/health` | Liveness: the process is accepting requests. Runs no checks. |
| `/health/ready` | Readiness: PostgreSQL is reachable. |

Both are anonymous so the platform can probe them without credentials.
