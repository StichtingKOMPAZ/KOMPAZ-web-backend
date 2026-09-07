# KOMPAZ Web Backend

A Clean Architecture .NET 10 Minimal API, built on the shape of `dotnet-template`. It ships passwordless
(magic link) authentication, user invitations, user and organization CRUD, and paginated querying that can be
narrowed to invited or active users.

## Layers

- `src/Domain` — entities, enums, and the rules that guard them (`Organization`, `User`, `LoginToken`)
- `src/Application` — commands, queries, validation, authorization, and the abstractions the outside world implements
- `src/Infrastructure` — EF Core persistence, JWT issuing, login-token hashing, and email delivery
- `src/Presentation` — Minimal API endpoint groups, problem details, rate limiting, and Swagger
- `tests/Application.UnitTests` — validators, pagination maths, tenant access rules, domain behaviour
- `tests/Application.FunctionalTests` — end-to-end HTTP coverage over an in-memory database
- `tests/Application.CodeStyleTests` — architectural guardrails (request naming, every list query pages)

## Getting started

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Restore and run:

```bash
dotnet restore Kompaz.sln
dotnet run --project src/Presentation/Presentation.csproj
```

3. Open Swagger at the URL printed in the console.

On first run the database is migrated and seeded with a single organization, `KOMPAZ`, holding one active platform
administrator: **`admin@kompaz.local`**. There is no password — sign in with a magic link.

## Signing in

```
POST /api/auth/magic-link     { "email": "admin@kompaz.local" }   -> 202 Accepted
POST /api/auth/tokens         { "token": "<from the link>" }      -> 200 <session>
GET  /api/auth/me             Authorization: Bearer <accessToken>
```

A `<session>` is:

```json
{
  "accessToken": "eyJ…",
  "tokenType": "Bearer",
  "expiresUtc": "2026-09-04T15:51:06Z",
  "refreshToken": "Mnz_KeNeKs2…",
  "refreshTokenExpiresUtc": "2026-09-18T14:51:06Z",
  "user": { }
}
```

`POST /api/auth/magic-link` always returns `202`, whether or not the address has an account, so the endpoint cannot
be used to discover who is registered. Each link works once, expires (15 minutes by default), and requesting a new
link retires any link sent earlier.

## Staying signed in

Access tokens are short-lived. The refresh token that comes with them buys a new one without another trip through
the inbox:

```
POST /api/auth/tokens/refresh   { "refreshToken": "…" }   -> 200 <session>
POST /api/auth/tokens/revoke    { "refreshToken": "…" }   -> 204 No Content
```

**The expiry slides.** A refresh token is good for 14 idle days. Every exchange issues a successor whose 14 days
start from that moment, so a client that keeps using the API never has to sign in again, while one that goes quiet
for a fortnight does. `refreshTokenExpiresUtc` on each response is the new deadline.

**The slide has a ceiling.** A session also carries an absolute deadline, 90 days from sign-in, which refreshing
never moves. Once it passes, the session is over and the user signs in by email again. The sliding window is
clamped so it can never report an expiry beyond that ceiling.

**Tokens rotate, and replay ends the session.** Each refresh spends the token it was given. Presenting a spent or
revoked token means the secret is loose, so the entire rotation chain is revoked and the response is `401` — the
successor stops working too, and the user has to sign in again. Other sessions for the same user are untouched, so
one compromised device does not sign the user out everywhere.

`POST /api/auth/tokens/revoke` is sign-out. It ends the whole chain, and returns `204` even for a token that is
already gone, so a client can always clear its credentials without interpreting an error.

Both endpoints are anonymous — the refresh token *is* the credential — and both share the tighter sign-in rate
limit. Note that an access token keeps whatever role it was minted with until it expires, so a role change takes up
to `AccessTokenLifetimeMinutes` to take effect.

## Email in development

Development points `Email:Smtp` at a [Mailtrap](https://mailtrap.io) sandbox inbox, which captures mail instead of
delivering it, so invitations and sign-in links can be read without reaching a real recipient. The credentials are
not in the repository — supply your own inbox:

```bash
dotnet user-secrets --project src/Presentation set "Email:Smtp:UserName" "<mailtrap-username>"
dotnet user-secrets --project src/Presentation set "Email:Smtp:Password" "<mailtrap-password>"
```

Find both under **Email Testing → Inboxes → SMTP Settings** in Mailtrap.

Until they are set, `Email:Smtp:UserName` and `Password` still read `<set-with-user-secrets>`, which counts as
absent, and mail is written to the application log instead. That keeps a fresh checkout working: the log line
contains the sign-in link, so copy the `token` query parameter out of it. **The log sink is for development only** —
sign-in links are secrets and do not belong in logs.

## Invitations

`POST /api/users/invitations` creates a user with status `Invited` and emails them an invitation link. Redeeming
that link at `POST /api/auth/tokens` both accepts the invitation (status becomes `Active`, `activatedUtc` is
stamped) and signs the user in — there is no separate accept endpoint. `POST /api/users/{id}/invitations` sends a
fresh link and retires the previous one.

## Endpoints

| Endpoint | Method | Lowest role |
| --- | --- | --- |
| `/api/auth/magic-link` | POST | anonymous |
| `/api/auth/tokens` | POST | anonymous |
| `/api/auth/tokens/refresh` | POST | anonymous (the refresh token is the credential) |
| `/api/auth/tokens/revoke` | POST | anonymous (the refresh token is the credential) |
| `/api/auth/me` | GET | Member |
| `/api/organizations` | GET | Member (scoped to your own) |
| `/api/organizations` | POST | PlatformAdministrator |
| `/api/organizations/{id}` | GET | Member of that organization |
| `/api/organizations/{id}` | PUT | Administrator of that organization |
| `/api/organizations/{id}` | DELETE | PlatformAdministrator |
| `/api/users` | GET | Administrator |
| `/api/users/{id}` | GET | Yourself, or Administrator in the same organization |
| `/api/users/invitations` | POST | Administrator |
| `/api/users/{id}/invitations` | POST | Administrator |
| `/api/users/me` | PUT | Member (their own profile) |
| `/api/users/{id}` | PUT | Administrator |
| `/api/users/{id}` | DELETE | Administrator |

## Editing a profile

There are two ways to change a user, and they are deliberately separate endpoints:

- `PUT /api/users/me` — the signed-in user edits **their own** profile. Open to any role, and it only takes a
  `name`. Read the current profile back from `GET /api/auth/me`.
- `PUT /api/users/{id}` — an administrator edits **somebody else**, including their role.

A member calling `PUT /api/users/{id}` gets `403`, even for their own id, so self-service never leaks into
administration. The email address is not editable at either endpoint: it is the sign-in identity, and changing it
needs proof of the new inbox.

## Roles and tenancy

Roles are hierarchical: `Member` < `Administrator` < `PlatformAdministrator`. Every user belongs to exactly one
organization. Administrators manage their own organization only; platform administrators reach every organization
and are the only ones who may grant, revoke, or delete that role. HTTP-level authentication is enforced by the
endpoint groups; role and tenant checks live in the Application layer so they hold for any caller of a use case.

## Querying users

```
GET /api/users?status=Invited&search=jansen&organizationId=<guid>&pageNumber=1&pageSize=25
```

- `status` — `Invited` or `Active`; omit for both
- `search` — case-insensitive fragment matched against name and email
- `organizationId` — platform administrators only; defaults to the caller's own organization
- `pageNumber` / `pageSize` — `pageSize` is capped at 100

Every list endpoint returns the same envelope:

```json
{
  "items": [],
  "pageNumber": 1,
  "pageSize": 25,
  "totalCount": 0,
  "totalPages": 0,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

`GET /api/organizations` accepts `search`, `pageNumber`, and `pageSize`, and reports `userCount`,
`activeUserCount`, and `invitedUserCount` per organization.

## Configuration

| Key | Notes |
| --- | --- |
| `ConnectionStrings:KompazDb` | SQLite by default (`kompaz.db`) |
| `Authentication:Issuer` / `Audience` | Stamped on and required of every access token |
| `Authentication:SigningKey` | HMAC-SHA256 key, **at least 32 bytes**; startup fails without it |
| `Authentication:AccessTokenLifetimeMinutes` | Default 60 |
| `Authentication:MagicLinkLifetimeMinutes` | Default 15 |
| `Authentication:InvitationLifetimeDays` | Default 7 |
| `Authentication:RefreshTokenSlidingLifetimeDays` | Idle window, restarted on every refresh. Default 14 |
| `Authentication:RefreshTokenAbsoluteLifetimeDays` | Ceiling refreshing never moves. Default 90, must be >= the sliding window |
| `Email:FromAddress` / `FromName` | Sender of sign-in email |
| `Email:MagicLinkUrl` / `InvitationUrl` | Client URLs; both must contain the `{token}` placeholder |
| `Email:Smtp:Host` | Mailtrap sandbox in development |
| `Email:Smtp:UserName` / `Password` | From user secrets; while unset, email is logged instead of sent |
| `RateLimiting:SignInPermitLimit` / `SignInWindowSeconds` | Budget for the two anonymous auth endpoints (default 5 per 5 minutes) |

`Authentication:SigningKey` is deliberately empty in `appsettings.json` and `appsettings.Production.json`, so a
deployment that forgets to supply one fails at startup rather than signing tokens with a shared secret. Supply it
per environment, for example `Authentication__SigningKey` as an environment variable. `appsettings.Development.json`
carries a throwaway key so `dotnet run` works out of the box.

## Security notes

- Login tokens are 256 bits of cryptographic randomness; only their SHA-256 hash is stored, never the value in the link.
- Redeeming a link consumes it, and issuing a new one consumes any outstanding link for that user.
- The two anonymous auth endpoints carry a tighter rate-limit budget than the rest of the API.
- Refresh tokens are stored the same way as login tokens: 256 bits of randomness, only the SHA-256 hash persisted.
- Refresh tokens rotate on every use, and replaying a spent one revokes the whole session.
- Deleting a user or an organization cascades to their sessions, so revoked people lose access immediately.
- Token lifetimes are validated against the injected `TimeProvider`, so the whole application runs on one clock.

## Database

SQLite by default, with real EF Core migrations applied on startup. To add one:

```bash
dotnet ef migrations add <Name> \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/Presentation/Presentation.csproj \
  --output-dir Persistence/Migrations
```

To move to PostgreSQL, swap `Microsoft.EntityFrameworkCore.Sqlite` for `Npgsql.EntityFrameworkCore.PostgreSQL`,
change `UseSqlite` to `UseNpgsql` in `src/Infrastructure/ConfigureServices.cs`, and regenerate the migrations.
`docker-compose.yml` already brings up a Postgres instance for that.

## Build and test

```bash
dotnet build Kompaz.sln --configuration Release
dotnet test Kompaz.sln --configuration Release
```

Release builds run StyleCop, Sonar, and the .NET analyzers with warnings as errors, so a clean Release build is the
quality gate. CI runs restore, build, and test on every push and pull request.

## Project hygiene

- Package versions are centralized in `Directory.Packages.props`, including a few transitive pins that lift
  dependencies above known advisories.
- Build output goes to `artifacts/`.
- Generated EF migrations are excluded from style analysis via `.editorconfig`.
