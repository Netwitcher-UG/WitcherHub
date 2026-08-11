# Credential rotation — required before the next deployment

`WitcherHub/appsettings.Development.json` was committed to this repository with
live credentials in it. The values have been removed from the working tree, but
**git history still contains them**, and anyone who has ever cloned or forked
the repository still holds a copy. Removing a value from the current file does
not make it secret again.

Treat every credential below as compromised and rotate it. This is not
optional cleanup — until it is done, the repository history is a working set of
production keys.

## What was exposed

| Credential | Where it was | Rotate by |
| --- | --- | --- |
| PostgreSQL password (Railway) | `ConnectionStrings:DefaultConnection` | Railway → database → reset password, then update `ConnectionStrings__DefaultConnection`. |
| JWT signing key | `Jwt:Key` | Generate a new random key. Rotating it invalidates every issued session, so all users must sign in again. |
| Gmail app password | `Smtp:Password` | Google Account → Security → App passwords → revoke the old one, create a new one. |
| Lexware API token (two of them) | `Lexware:AccessToken`, and a second token commented out on the line above | Lexware → API settings → revoke both, issue a new one. |
| OpenAI API key | `OpenAI:ApiKey` | platform.openai.com → API keys → revoke, create new. Check usage for unexpected spend. |

The same credentials were also pasted into ClickUp task descriptions, per the
project handover document. Deleting them there does not undo the exposure
either; rotation is what closes it.

## Order of work

1. Issue the replacement credentials.
2. Set them as environment variables on Railway for each environment
   (`Lexware__AccessToken`, `OpenAI__ApiKey`, and so on — see
   [CONFIGURATION.md](CONFIGURATION.md)).
3. Deploy and confirm the start-up log reports each integration as configured.
4. Revoke the old credentials.
5. Review access logs for the window in which they were exposed: OpenAI usage,
   Lexware API activity, Gmail sign-ins, and PostgreSQL connections from
   unfamiliar addresses.

Revoking before step 3 will take the running deployment down, so keep the order.

## Preventing a recurrence

- Secrets go in Railway variables or `dotnet user-secrets`, never in a tracked file.
- The application now fails to start when a required secret is missing, so a
  misconfigured environment is loud rather than silent.
- Add secret scanning to CI. GitHub push protection catches the common key
  formats, including the OpenAI and Google ones involved here.
- Purging the values from git history (`git filter-repo`, or a fresh repository)
  is worth doing, but it rewrites every commit hash and invalidates existing
  clones. Rotation is what actually removes the risk; history rewriting only
  reduces further exposure. Do the rotation first.
