# CLAUDE.md — KOMPAZ web backend

Multi-tenant user and organization management with passwordless (magic-link) sign-in and invitations. .NET 10, Clean
Architecture, PostgreSQL/EF Core, MediatR CQRS, minimal APIs.

## Layout

```
src/Domain           entities (Entities/), enums-as-strings, domain events (Events/), base classes in Common/
src/Application      CQRS slices: {Feature}/{Commands,Queries,EventHandlers}; one file = request + validator + handler
src/Infrastructure   ApplicationDbContext (+ Interceptors/, Migrations/), JWT issuing, token hashing, email transports
src/Presentation     entry point: minimal-API endpoint groups (Endpoints/), problem details, rate limiting, Swagger
tests/               UnitTests, CodeStyleTests (architecture guards), FunctionalTests (Testcontainers, needs Docker)
```

## Commands

```sh
docker compose up -d postgres                            # the dev database, on localhost:5433
dotnet run --project src/Presentation                     # run the API
dotnet build Kompaz.sln -c Release                        # THE lint gate: StyleCop+Sonar, warnings-as-errors (Debug doesn't check!)
dotnet test Kompaz.sln                                    # all tests; FunctionalTests start their own Postgres container
dotnet ef migrations add Name --project src/Infrastructure --startup-project src/Presentation --output-dir Persistence/Migrations
```

## Iron rules

1. **`dotnet build -c Release` is the gate, not `dotnet build`.** Release turns on StyleCop, Sonar and the .NET
   analyzers with warnings-as-errors; Debug turns none of it on, so code that "builds fine" still fails CI.
2. **Every `IRequest` carries `[Authorize]` or `[AllowAnonymous]`** (from `Application/Common/Security`, not
   ASP.NET). `AuthorizeTests` enforces it and `AuthorizationBehaviour` refuses a request with neither at runtime, so
   forgetting to think about authorization fails instead of quietly publishing a use case. Only the four
   authentication entry points are anonymous, and that list is asserted.
3. **Tenancy is manual, and so is soft delete.** There are no global query filters. Every handler calls
   `OrganizationAccess` (`EnsureCanRead` / `EnsureCanManage` / `EnsureCanManageRole` / `ResolveTarget`), and every
   query over `Users` states `DeletedUtc == null` for itself. An unscoped new query is a security bug, not an
   oversight. `[Authorize(MinimumRole = …)]` is a floor on the role, never a tenant check. The two deliberate
   exceptions to the delete filter are `RestoreUserCommand` and `InviteUserCommand`, whose whole job involves a
   deleted row; a filter that applied itself would make those two fail quietly instead of loudly.
4. **Never edit a migration that has shipped** — instances migrate on startup. Fix forward. An unshipped migration
   gets deleted and regenerated rather than stacked with a fixup.
5. **Audit fields are stamped by `AuditableEntityInterceptor`** — never set `CreatedUtc`/`UpdatedUtc`/`CreatedBy`/
   `UpdatedBy` in a handler or a domain method. Inherit `AuditableEntity`; `Entity` alone is for rows that are
   written once. Do not initialize `Id`: EF generates a sequential one, and a random `Guid.NewGuid()` here would
   scatter primary-key inserts.
6. **Cross-cutting reactions go through domain events**, not service calls from handlers:
   `entity.AddDomainEvent(...)` before `SaveChangesAsync`, handled under `Application/{Feature}/EventHandlers/`.
   `DispatchDomainEventsInterceptor` publishes them **after** the save, so a reaction never holds a database
   transaction open across network I/O — and so a reaction can fail after the data is committed. Commands that raise
   one must therefore be safe to repeat (re-inviting a pending user resends rather than conflicting).
7. **A single-use secret is spent with a conditional `UPDATE`, never a read followed by a write.** Both redemption
   paths use `ExecuteUpdateAsync` with the "not yet spent" condition in the `WHERE` and check the affected-row count;
   losing that race is a replay. Refresh rotation additionally runs inside `BeginTransactionAsync`, because spending
   the old token and inserting its successor must become visible together or a concurrent replay revokes a chain the
   successor has not joined yet. Do not "simplify" either of these back into a check-then-save.
8. **Two files know which database this is**: `Persistence/UniqueConstraint.cs` (SQLSTATE `23505`, which turns a lost
   uniqueness race into a 409 instead of a 500) and `Common/Search/SearchPattern.cs` (case folding through `upper()`
   on both sides, deliberately not `ILIKE`, which would put a dialect in the Application layer). Changing provider
   means changing both — nothing else.
9. **Outside Development, an unconfigured `Email:Smtp` fails startup.** The fallback sink writes sign-in links to the
   log, which is a credential leak anywhere but a developer machine. Never widen that fallback to "any environment".
10. **Behind a reverse proxy, configure `ForwardedHeaders`.** Unconfigured, every per-client decision keys on the
    proxy's address, so the whole deployment shares one rate-limit partition and HTTPS redirection loops. Nothing is
    believed unless named; `TrustAnyProxy` is only safe where the app is unreachable except through the proxy.
11. **"Somebody has to be left" lives in `AdministratorCoverage`, not in the command.** Deleting, demoting and
    moving all take a person out of an organization's administrators, and a move or a demotion can also take the
    last platform administrator. Three commands, one rule; a fourth way to remove somebody asks there too.
12. Code style: tabs, CRLF, `records` for requests, one file per slice holding request + validator + handler, write
    the validator even when it would be empty, DTOs suffixed `Dto` with a static `Projection` expression so queries
    project in the database. `FluentValidation`, `MediatR` and `Microsoft.EntityFrameworkCore` are global usings in
    Application — do not add those directives. Match surrounding code.

## Things that have already cost time

- **An access token outlives the account, and what it says about it.** It is a signed statement about who somebody
  was when it was issued, so deleting a user, demoting them or moving them between organizations invalidates
  none of it. Deleting their refresh tokens only stops the *next* hour; `AccountStatusBehaviour` is what stops the
  current one, comparing the `role` and `organizationId` claims against the row at the cost of a primary-key
  lookup per authenticated request. Anything else that changes what somebody may do needs to be compared there
  too — the claims on the token will not notice on their own.
- **`DateTimeOffset` comparisons.** The SQLite provider could not translate them at all, which is why token expiry
  was checked in memory; Npgsql can. Login-token expiry is now one condition of the atomic claim. Refresh-token
  expiry deliberately is **not**, because losing that `UPDATE` revokes the session, and a token expiring between the
  check and the claim would be punished as a replay rather than reported as expired.
- **EF 10 `ExecuteUpdateAsync` setters.** `SetProperty(x => x.Nullable, value)` infers `TProperty` as `object` and
  fails to translate with a message that blames the whole query. Cast the value: `(DateTimeOffset?)now`.
- **Single-shot concurrency tests lie.** A race test that passes once may simply not have interleaved; the
  refresh-rotation test only became reliable when it ran the race over several rounds.
- **Pagination arithmetic is attacker-chosen.** A client picks both page number and size, so the offset is computed
  in 64 bits — `(pageNumber - 1) * pageSize` overflowed `int` and silently served page one.
- **Windows reserves high ports.** The compose database is published on 5433 because 54321 falls in a range Windows
  refuses to bind.

## Environment notes

- `appsettings.json` holds no secrets: `Authentication:SigningKey` and the production connection string are
  deliberately empty so a deployment that forgets them fails at startup rather than running on a shared default.
  Supply them per environment (`Authentication__SigningKey`), and SMTP credentials via `dotnet user-secrets` locally.
- Swagger is off unless `Swagger` is `true`, which only `appsettings.Development.json` sets.
- `Database:MigrateOnStartup` defaults to true. A deployment that runs several instances turns it off and applies
  migrations as its own step; startup then refuses a database that is behind the code.
- Development mail goes to a Mailtrap sandbox; without those credentials it goes to the log, Development only.

## Branch flow

`main` is the only long-lived branch. Work on a feature branch and open a PR; CI runs restore, Release build and
tests on every push and pull request. Commit messages: imperative and descriptive.
