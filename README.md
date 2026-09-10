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
- `tests/Application.FunctionalTests` — end-to-end HTTP coverage against a real PostgreSQL, started per test run
- `tests/Application.CodeStyleTests` — architectural guardrails (request naming, every list query pages)

## Getting started

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Docker.
2. Bring up PostgreSQL:

```bash
docker compose up -d postgres
```

   That publishes a `kompaz` database on `localhost:5433`, which is what `appsettings.json` points at.

3. Restore and run:

```bash
dotnet restore Kompaz.sln
dotnet run --project src/Presentation/Presentation.csproj
```

4. Open Swagger at the URL printed in the console.

Swagger is off unless `Swagger` is `true`, which `appsettings.Development.json` sets. Nothing else does, so an
environment nobody thought about — a staging slot, a one-off QA box — does not publish the API surface by default.

On first run the database is migrated and seeded with a single organization, `Stichting KOMPAZ`, holding one active
platform administrator: **`admin@kompaz.local`**. There is no password — sign in with a magic link.

That organization is flagged `isPlatform`, which is what marks it as the one that runs the platform. Nothing over the
API sets the flag and a unique index keeps it to one row, so the organization a super admin can belong to is fixed
when the database is seeded rather than chosen per request.

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
link retires the previous **sign-in** link.

**Asking for a sign-in link never touches a pending invitation.** This endpoint is anonymous, so anybody who knows
an address can call it, and retiring invitations here would let a stranger invalidate the link an administrator sent
as often as they liked. Somebody who has not accepted yet can still use a sign-in link, and reissuing the invitation
itself is the administrator's endpoint below.

**Redeeming one does**, because redeeming activates them: the invitation has been accepted at that point, whichever
link they arrived on, so any invitation still outstanding is spent along with it. Otherwise a week-long credential
would stay live in an inbox for somebody who can already sign in, where a sign-in link only lives fifteen minutes,
and the roster would go on reporting an invitation nobody is waiting on.

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
contains the sign-in link, so copy the `token` query parameter out of it.

**The log sink is available in Development only.** Sign-in links are secrets and do not belong in logs, so outside
Development an unconfigured `Email:Smtp` fails at startup rather than falling back to it — the same stance as a
missing signing key. A deployment must supply `Host`, `UserName`, and `Password`.

## Invitations

`POST /api/users/invitations` creates a user with status `Invited` and emails them an invitation link. Redeeming
that link at `POST /api/auth/tokens` both accepts the invitation (status becomes `Active`, `activatedUtc` is
stamped) and signs the user in — there is no separate accept endpoint. `POST /api/users/{id}/invitations` sends a
fresh link and retires the previous one.

**Inviting somebody who has not accepted yet is the same request again**, not a conflict: the name and role are
updated, a new link is sent, and the previous one is retired. The user row commits before the email goes out, so
without this an invitation whose email failed to send would leave a user who was never told and an address nobody
could invite again. Only an address belonging to somebody who has already signed in is a `409`.

Only `email` and `name` are required. `organizationId` defaults to the caller's own organization and `role` to
`Member`, which is the whole request an organization administrator can make; a platform administrator states both.

**An invitation link lasts seven days** (`Authentication:InvitationLifetimeDays`), against fifteen minutes for a
sign-in link, because the invitee has to notice the email before they can act on it. The deadline is fixed when the
link is issued, so reconfiguring the lifetime neither expires nor revives one already sent.

`invitedUtc` says when the last invitation was sent and `invitationExpiresUtc` when the outstanding one lapses —
`null` once there is none, which is the case for everybody active. Together they are the invited table's status
badge: an `Invited` row whose `invitationExpiresUtc` is in the future is *uitgenodigd*, one in the past *verlopen*.
An invitation that lapses is not withdrawn — the row stays on the list until somebody re-invites
(`POST /api/users/{id}/invitations`) or revokes it.

**Revoking an invitation is `DELETE /api/users/{id}`**, the same request as deleting any user, and it takes the
outstanding link with it rather than leaving one that would sign the invitee in after they were removed. There is no
separate revoke endpoint and no `410`-style tombstone: an invitation nobody accepted has left nothing behind.

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

**A role is only ever granted from above.** An administrator can hand out `Member` and nothing else, so the people
they invite into their organization are instructors and appointing another administrator stays the platform's call.
Granting a role and managing somebody who holds one are separate questions: an administrator may still rename or
remove the fellow administrator they could not have appointed. Both the invitation and `PUT /api/users/{id}` answer
to this, or inviting a member and promoting them a moment later would be the way around it.

**Platform administration does not travel to a tenant.** The role belongs to the organization flagged `isPlatform`,
so granting it anywhere else is a `409` — a clash with where the role lives, not a refusal of the caller, who is
usually entitled to grant it. Nothing moves a user between organizations, so the invariant only needs stating where
the role is handed out.

**The role cannot be abandoned by its last holder.** Nobody may delete their own account, and only a platform
administrator may remove another, so a platform administrator giving up the role is the one way to leave the system
with nobody able to grant it back. That returns `409` unless somebody else already holds it.

**A platform administrator can only be administered by one.** Editing, deleting, or re-inviting somebody who holds
the role is reserved for other platform administrators, whether or not the request changes the role itself, so an
organization administrator who happens to share their organization cannot reach them.

## Querying users

```
GET /api/users?status=Invited&search=jansen&organizationId=<guid>&pageNumber=1&pageSize=25
```

- `status` — `Invited` or `Active`; omit for both. `Invited` is the beheer page's *uitgenodigd* tab, expired
  invitations included; read `invitationExpiresUtc` to tell the two badges apart
- `search` — case-insensitive fragment matched against name and email, accents included, so `renée` finds `Renée`
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

## Running behind a proxy

Every per-client decision keys on the connection's address, and behind a reverse proxy that address is the proxy.
Left unconfigured, one address stands for everybody: the whole API shares a single rate-limit partition, so the
global budget applies to all callers together and the sign-in budget becomes five links per five minutes for the
entire deployment. `X-Forwarded-Proto` goes unread too, so `UseHttpsRedirection` bounces requests whose TLS the
proxy already terminated.

Name the proxy and both are fixed:

```json
"ForwardedHeaders": {
  "KnownNetworks": [ "10.0.0.0/8" ],
  "ForwardLimit": 1
}
```

`KnownProxies` takes individual addresses, `KnownNetworks` takes CIDR ranges, and `ForwardLimit` is how many
chained proxies to walk back through. **Nothing is believed unless it is named here** — not even loopback, which
the framework would otherwise trust silently, making this look like it works until the proxy is not on localhost.

Where the proxy's address is not known ahead of time, such as a Kubernetes ingress, `"TrustAnyProxy": true`
believes any sender and says so in the log at startup. Only use it where the application cannot be reached except
through that proxy: a caller who can connect directly can then claim a fresh address on every request and never
meet a rate limit. Naming proxies and trusting any at the same time is a contradiction, and startup fails.

`/health` sits outside the rate limiter on purpose. An orchestrator polls it, and an instance answering its own
probe with `429` for being busy would be restarted for being busy.

## Configuration

| Key | Notes |
| --- | --- |
| `ConnectionStrings:KompazDb` | PostgreSQL. `docker-compose.yml` publishes one on `localhost:5433` |
| `Authentication:Issuer` / `Audience` | Stamped on and required of every access token |
| `Authentication:SigningKey` | HMAC-SHA256 key, **at least 32 bytes**; startup fails without it |
| `Authentication:AccessTokenLifetimeMinutes` | Default 60 |
| `Authentication:MagicLinkLifetimeMinutes` | Default 30 |
| `Authentication:InvitationLifetimeDays` | Default 7 |
| `Authentication:RefreshTokenSlidingLifetimeDays` | Idle window, restarted on every refresh. Default 14 |
| `Authentication:RefreshTokenAbsoluteLifetimeDays` | Ceiling refreshing never moves. Default 90, must be >= the sliding window |
| `Email:FromAddress` / `FromName` | Sender of sign-in email |
| `Email:DefaultLanguage` | Language outbound email is written in; `nl` (default) or `en`. Add a language by adding `src/Infrastructure/Email/EmailResources.<culture>.resx` |
| `Email:MagicLinkUrl` / `InvitationUrl` | Client URLs; both must contain the `{token}` placeholder |
| `Email:Smtp:Host` | Mailtrap sandbox in development |
| `Email:Smtp:UserName` / `Password` | From user secrets. While unset, email is logged in Development and startup fails elsewhere |
| `RateLimiting:MagicLinkPermitLimit` / `MagicLinkWindowSeconds` | Budget for requesting a sign-in link, per client address (default 5 per 5 minutes). Shared by everyone behind one address |
| `RateLimiting:SignInPermitLimit` / `SignInWindowSeconds` | Budget for the token endpoints, which send no email (default 30 per 5 minutes) |
| `ForwardedHeaders:KnownProxies` / `KnownNetworks` | Proxies whose `X-Forwarded-*` headers are believed. Empty means none |
| `ForwardedHeaders:TrustAnyProxy` | Believe any sender. Only where nothing can reach the app but the proxy |
| `ForwardedHeaders:ForwardLimit` | How many chained proxies to walk back through. Default 1 |
| `Database:MigrateOnStartup` | Default true. Turn off where several instances start together |
| `Swagger` | Default false; `appsettings.Development.json` turns it on |

`Authentication:SigningKey` is deliberately empty in `appsettings.json` and `appsettings.Production.json`, so a
deployment that forgets to supply one fails at startup rather than signing tokens with a shared secret. Supply it
per environment, for example `Authentication__SigningKey` as an environment variable. `appsettings.Development.json`
carries a throwaway key so `dotnet run` works out of the box.

## Security notes

- Login tokens are 256 bits of cryptographic randomness; only their SHA-256 hash is stored, never the value in the link.
- Redeeming a link consumes it, and issuing a new one consumes any outstanding link for that user.
- Spending a login token or a refresh token is a conditional `UPDATE`, so two requests arriving with the same secret
  cannot both succeed: one wins and the other is told the secret is spent.
- The two anonymous auth endpoints carry a tighter rate-limit budget than the rest of the API.
- Refresh tokens are stored the same way as login tokens: 256 bits of randomness, only the SHA-256 hash persisted.
- Refresh tokens rotate on every use, and replaying a spent one revokes the whole session.
- Deleting a user or an organization cascades to their sessions, so revoked people lose access immediately.
- Token lifetimes are validated against the injected `TimeProvider`, so the whole application runs on one clock.

## Database

PostgreSQL, with real EF Core migrations applied on startup — which is right for one instance and for a developer
machine, but several instances starting together would race each other. A deployment that scales out sets
`Database:MigrateOnStartup` to `false` and applies migrations as a step of its own:

```bash
dotnet ef database update --project src/Infrastructure --startup-project src/Presentation
```

With it off, an instance whose database is behind the code refuses to start rather than serving requests against a
schema that does not match. Configuration is checked before any of this, so a deployment that cannot work does not
leave a migrated, seeded database behind on its way out.

To add a migration:

```bash
dotnet ef migrations add <Name> \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/Presentation/Presentation.csproj \
  --output-dir Persistence/Migrations
```

Two places know which database this is, and changing provider means changing both:

- `src/Infrastructure/Persistence/UniqueConstraint.cs` — the SQLSTATE behind a duplicate (`23505`), which is what
  turns a lost uniqueness race into a `409` instead of a `500`.
- `src/Application/Common/Search/SearchPattern.cs` — how `search` ignores case. Both sides are folded through
  `upper()` rather than reaching for `ILIKE`, so the use-case layer does not name a dialect; the note there explains
  why folding in .NET on one side only would not do.

## Build and test

```bash
dotnet build Kompaz.sln --configuration Release
dotnet test Kompaz.sln --configuration Release
```

The functional tests need Docker: they start a PostgreSQL container for the run and give each test a database of its
own on it. Nothing needs to be running beforehand, and `docker compose up` is not involved — that instance is for
`dotnet run`, not for the tests.

Release builds run StyleCop, Sonar, and the .NET analyzers with warnings as errors, so a clean Release build is the
quality gate. CI runs restore, build, and test on every push and pull request.

## Deployment

The develop environment runs on Azure Container Apps behind the existing `igne-proxy`, and reuses the shared
PostgreSQL server and container registry already in the subscription rather than provisioning its own. Roughly
EUR 13 a month, almost all of it the always-warm replica.

```powershell
./scripts/bootstrap-azure.ps1 -MailtrapUserName <user> -MailtrapPassword <password>
```

Infrastructure is Bicep in `infra/`, split into a `foundation` stage that creates the identity and key vault and an
`app` stage that references the secrets by URI without ever carrying a value. `.github/workflows/deploy-develop.yml`
redeploys the app stage on every push to `main`, gated behind the tests.

Two things a deployment must get right are written up in [docs/deployment.md](docs/deployment.md): the
`ForwardedHeaders:ForwardLimit` measurement, which the rate limiter depends on, and the connection pool cap, which
the five other projects sharing that database server depend on.

## Project hygiene

- Package versions are centralized in `Directory.Packages.props`, including a few transitive pins that lift
  dependencies above known advisories.
- Build output goes to `artifacts/`.
- Generated EF migrations are excluded from style analysis via `.editorconfig`.
