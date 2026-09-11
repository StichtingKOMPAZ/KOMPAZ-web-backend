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
   `DispatchDomainEventsInterceptor` collects which entities raised something **before** the save and publishes
   **after** it. Before, because saving a deletion detaches the row — an event raised on a row that is being
   removed would otherwise be dropped in silence, which is how the people in a deleted organization get told.
   After, so a reaction never holds a database transaction open across network I/O — and so a reaction can fail
   after the data is committed. Commands that raise one must therefore be safe to repeat (re-inviting a pending
   user resends rather than conflicting).
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
12. **A name and an email are unique folded, not as typed.** `User.NormalizedEmail` and
    `Organization.NormalizedName` carry the unique index; `Name` keeps a plain index because the roster orders by
    it. Compare against the normalized column, never the raw one, or two rows differing by case get in and the
    index that was supposed to stop them is on a different column than the check.
13. **An uploaded file is a row that points at the blob container, and its format is read out of its bytes.**
    `LogoImage.DetectContentType` decides the media type; the upload's own `Content-Type` and file name are never
    believed, because the stored value is what a later response is labelled with. `OrganizationLogo` records a
    `StorageKey` and `IFileStore` holds the bytes — `BlobFileStore` in a deployment, `FileSystemFileStore` on a
    developer machine, and an unconfigured `Storage:ConnectionString` fails startup outside Development for the
    same reason unconfigured SMTP does. The key is minted from the organization's id and a fresh identifier, never
    accepted from a caller. Reads go through the API, which checks the token; the container is private and nothing
    is served from it directly.
14. **A file outlives its transaction, so letting go of one is a domain event.** The database owns the row and the
    container owns the bytes, and no transaction spans both. Every path that stops pointing at a file raises
    `OrganizationLogoDiscardedEvent` — a replacement, a deleted logo, a deleted organization, which has to ask for
    the key before the cascade takes the row — and the file is removed after the commit. That ordering is chosen,
    not incidental: an upload writes its blob *before* the row, and a deletion removes its row *before* the blob,
    so every failure leaves an orphaned file rather than a row pointing at nothing. An orphan costs storage and is
    logged with its key; a dangling pointer would be a broken image, and `GetOrganizationLogoQuery` answers one
    with the placeholder anyway. Never "fix" this by deleting the blob first.
15. **Anything a caller reads is Dutch; anything an operator reads is English.** Every message that reaches an
    HTTP response body — validation messages, `ConflictException`, `ForbiddenAccessException`,
    `AuthenticationFailedException`, `NotFoundException` — is Dutch, and `ValidatorOptions.Global.LanguageManager`
    is set to `nl` so a rule that states no wording of its own still answers in Dutch. Log messages, startup and
    configuration failures stay English: nobody reading those is a user. Wording the product dictates lives in a
    constant (`OrganizationMessages`) when two requests have to answer alike, and is asserted by a test, because
    copy from a ticket is not something the next edit should be free to reword. Problem-details `title` is the
    exception and stays English — it names the status from the HTTP spec's vocabulary, and `detail` is the
    sentence for the reader.
16. Code style: tabs, CRLF, `records` for requests, one file per slice holding request + validator + handler, write
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
- **Binding a form turns anti-forgery on.** A minimal-API parameter of type `IFormFile` adds anti-forgery
  metadata, and the endpoint then throws for want of `UseAntiforgery()`. Requests here are authenticated by a
  bearer token the caller attaches deliberately, never by a cookie a browser attaches on its own, so there is no
  cross-site request to forge: the logo upload states `DisableAntiforgery()` rather than the pipeline growing a
  middleware whose token would protect nothing.
- **A deletion notice goes only to somebody who could sign in.** The account-deleted email says the account is
  gone and that they can no longer log in. For a user still `Invited` every line of that is untrue — they never
  had an account — so revoking an invitation is silent, and deleting an organization tells only its active
  members. `DeleteUserCommand` and `DeleteOrganizationCommand` both state the condition; a third way to remove
  somebody states it too, or it emails a stranger about an account they never had.
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
