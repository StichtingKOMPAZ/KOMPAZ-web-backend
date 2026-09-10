# Deploying to Azure

The develop environment runs the API as a container on Azure Container Apps, reached through the existing
`igne-proxy`. Most of what it needs already existed in the Amulet subscription and is reused rather than
duplicated.

```
                 Cloudflare (igne.link)
                          |
              develop.kompaz.igne.link
                          |
                     igne-proxy            App Service B1, RG Igne, YARP
                    /            \         routes by host and path
   /{api,swagger,health}/**     /**
            |                     |
    ca-kompaz-api-develop   swa-kompaz-develop
      (Container App)        (Static Web App)
            |
      igne-postgres          shared PostgreSQL 16, RG Igne
```

The API and the frontend share one origin, which is why CORS does not apply and `Cors:AllowedOrigins` is left
empty. It also makes the frontend portable: moving it off Azure later is a one-line change to a proxy cluster
address, with no DNS, certificate, CORS or token-audience consequences.

## What is reused

| Resource | Where | Why |
| --- | --- | --- |
| `igne-postgres` | RG `Igne` | PostgreSQL 16 B1ms, already serving five other project databases |
| `acrphase2` | RG `Phase2` | A second Basic registry would cost €4.35/month and buy nothing |
| `igne-proxy` | RG `Igne` | Already the public front door, already holds the certificates |

## What is created

All in resource group `Kompaz`, matching the per-project convention used by `Phase2`, `Stappenplan` and `ddp`.

| Resource | Notes |
| --- | --- |
| `id-kompaz-develop` | User-assigned identity. Reads the vault secrets |
| `kv-kompaz-develop` | Key Vault, access-policy mode. Five secrets |
| `cae-kompaz` | Container Apps environment, consumption-only — no standing charge |
| `ca-kompaz-api-develop` | 0.5 vCPU / 1 GiB, one replica |
| `log-kompaz` | Log Analytics, 30-day retention |
| `swa-kompaz-develop` | Static Web App, Free tier |

Running cost is about **€13/month**, almost all of it the always-warm replica. See the bottom of this document.

Key Vault names are globally unique across all of Azure, so if `kv-kompaz-develop` is already taken the bootstrap
script fails on the foundation deployment. Pass `-KeyVaultName` with something else and change the default in
`infra/foundation.bicep` and `KEY_VAULT_NAME` in the deploy workflow to match.

## A permissions constraint that shapes all of this

The Owner role on this subscription carries an **ABAC condition** limiting which roles the holder may assign:

```
@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals
  {acdd72a7-3385-48ef-bd42-f606fba81ae7,   // Reader
   b24988ac-6180-42a0-ab88-20f7382dd24c}   // Contributor
```

So `Key Vault Secrets User`, `Key Vault Secrets Officer` and `AcrPull` cannot be granted by anyone who would
normally run the bootstrap script. Two design consequences follow, and neither is a preference:

- **The vault uses access policies, not RBAC.** A policy is a property of the vault resource, so it needs no role
  assignment and reaches the same place.
- **The container app pulls with the registry's admin credentials, not the managed identity.** The password is
  itself a vault secret, so it is not in the template or the app definition. `acrphase2` already had the admin user
  enabled, which is presumably why.

The GitHub deploy principal gets **Contributor** — allowed — on the `Kompaz` resource group and on the `acrphase2`
resource. Contributor on a registry carries the push and pull data actions, which is what `az acr login` needs, so
`AcrPush` is not required.

If the condition is ever lifted, the cleaner arrangement is RBAC on the vault plus `AcrPull` for the identity, and
the admin credentials can be turned off.

One warning about diagnosing this yourself: Azure CLI 2.75 renders the authorization failure from
`az deployment group create` and `validate` as **"The content for this response was already consumed"**, which
looks like a client bug. `az deployment group what-if` reports the real error.

## Why the deployment is in two stages

`infra/foundation.bicep` creates the identity and the vault. `infra/app.bicep` creates everything else and
references the secrets **by vault URI**, never by value.

That split exists because a container app cannot start until its secrets exist, and the secrets cannot be written
until the vault and its access policies exist. Keeping the values out of `app.bicep` is what makes it safe for CI to
redeploy on every push: no secret passes through a deployment parameter, GitHub never holds the token signing key,
and a redeploy that omits nothing cannot wipe anything.

Resource names are defaults on the parameters, so only three values are ever passed at deploy time: `image`,
`identityResourceId` and `keyVaultUri`.

## First run

Prerequisites: Azure CLI logged in as someone who can assign the Contributor role on this subscription, and a
Mailtrap sandbox inbox.

```powershell
./scripts/bootstrap-azure.ps1 `
    -MailtrapUserName <inbox-username> `
    -MailtrapPassword <inbox-password> `
    -PostgresAdminPassword <igne-admin-password>
```

There is a bash equivalent for Linux, WSL and CI. The two do the same steps in the same order and either is
fine; keep them in step when one changes.

```bash
./scripts/bootstrap-azure.sh     --mailtrap-username <inbox-username>     --mailtrap-password <inbox-password>     --postgres-admin-password <igne-admin-password>
```

The script is idempotent and can be re-run. It:

1. Creates the `Kompaz` resource group.
2. Deploys `foundation.bicep`, creating the identity and the vault, with access policies giving the identity read
   access to secrets and the person running it write access.
3. Reads `acrphase2`'s admin credentials, which is how the container app authenticates to the registry.
4. Creates the `develop-kompaz` database and a `kompaz` login role on `igne-postgres`.
5. Writes five secrets to the vault.
6. Registers the GitHub OIDC application and federated credentials, and prints the three repository secrets to set.
7. Builds the first image with `az acr build` and deploys `app.bicep`.

**Re-running never regenerates the signing key.** It is created only when absent, because rotating it invalidates
every access and refresh token in circulation. The same applies to the database password. The Mailtrap credentials
*are* overwritten each run, since they are passed in.

Omit `-PostgresAdminPassword` and the script prints the role SQL to run by hand instead.

### Repository configuration

If the [GitHub CLI](https://cli.github.com) is installed and authenticated, the script does this itself. Otherwise
it prints what to do and you can re-run it later — everything is idempotent.

```powershell
winget install --id GitHub.cli
gh auth login
```

Two things are needed on the repository:

**An environment named `develop`.** The deploy job targets it, which makes the OIDC token's subject
`repo:StichtingKOMPAZ/KOMPAZ-web-backend:environment:develop`. One of the federated credentials is scoped to
exactly that string, so if the environment does not exist the login step fails with "no matching federated identity
record found" — which does not obviously point at a missing environment.

**Three repository secrets.** These are identifiers, not credentials; the federated credential means there is no
client secret anywhere.

| Secret | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | Application id of `kompaz-web-backend-deploy`, printed by the script |
| `AZURE_TENANT_ID` | `d7dbcda8-3e68-4d09-ab2f-204ad76bca27` |
| `AZURE_SUBSCRIPTION_ID` | `a71fe690-e0f5-4d62-9990-1ab15fefc2b9` |

The script registers a second federated credential for `ref:refs/heads/main`. It is unused while the deploy job
declares an environment, and is there so a job without one still authenticates.

**Four credentials get registered, not two.** GitHub may present either of two subject formats, and which one it
uses is not something this repository controls:

```
repo:StichtingKOMPAZ/KOMPAZ-web-backend:environment:develop
repo:StichtingKOMPAZ@324818359/KOMPAZ-web-backend@1356901373:environment:develop
```

The second embeds the numeric organisation and repository ids — GitHub's *immutable subject claim*, which exists
so that a deleted org or repo name cannot be re-registered by someone else and inherit the trust relationship.
This deployment presented the second form, and the login step failed with:

```
AADSTS700213: No matching federated identity record found for presented assertion subject ...
```

which is easy to misread, because the subject it names looks almost identical to one already registered. Both
forms are now registered for both subjects. If it ever happens again, the error text contains the exact subject
to add:

```bash
az ad app federated-credential list --id <appId> --query "[].subject" -o tsv
```

## Wiring up the proxy

This step needs the container app's FQDN, so it happens after the first deploy:

```bash
az containerapp show -g Kompaz -n ca-kompaz-api-develop --query properties.configuration.ingress.fqdn -o tsv
az staticwebapp show -g Kompaz -n swa-kompaz-develop --query defaultHostname -o tsv
```

`igne-proxy` lives at `bitbucket.org/igne/igne-proxy`, deploys from a **manually triggered** pipeline, and keeps its
routes in `appsettings.Production.json`. Add two routes and two clusters, substituting the hostnames above:

```jsonc
// ReverseProxy.Routes
"kompaz_develop_back": {
  "ClusterId": "kompaz_develop_back",
  "Match": {
    "Path": "/{prefix:regex(api|swagger|health)}/{**remainder}",
    "Hosts": [ "develop.kompaz.igne.link" ]
  }
},
"kompaz_develop_web": {
  "ClusterId": "kompaz_develop_web",
  "Match": {
    "Hosts": [ "develop.kompaz.igne.link" ]
  }
},

// ReverseProxy.Clusters
"kompaz_develop_back": {
  "HttpRequest": { "ActivityTimeout": "00:10:00" },
  "Destinations": {
    "first": { "Address": "https://<container-app-fqdn>/" }
  }
},
"kompaz_develop_web": {
  "Destinations": {
    "first": { "Address": "https://<static-web-app-hostname>/" }
  }
}
```

The route without a `Path` is the catch-all and must come after the API route, matching how `demo.p2epd.nl` is
already configured. YARP replaces the `Host` header with the destination's own by default, which is exactly what
both Container Apps and Static Web Apps need to route the request.

### DNS and the certificate

Add `develop.kompaz.igne.link` as a custom domain on `igne-proxy` and point Cloudflare at
`igne-proxy.azurewebsites.net`. A free **App Service Managed Certificate** is simpler than the existing
Let's Encrypt acme-challenge-to-blob-storage arrangement and renews itself; it requires the Cloudflare record to be
**unproxied** (grey cloud). If you want the orange cloud, use a Cloudflare Origin Certificate with Full (strict)
instead.

## Measuring `ForwardLimit` — do not skip this

`appsettings.Develop.json` ships `ForwardedHeaders:ForwardLimit` at `1`, which is almost certainly **too low**.

There are now up to four hops in front of the app — Cloudflare, the App Service front end, YARP, and the Container
Apps Envoy ingress — and each may append to `X-Forwarded-For`. `ForwardLimit` is how many entries the middleware
walks back from the right. Too low and it stops on a proxy address, so every caller in the world shares a single
rate-limit partition and the sign-in budget becomes five links per five minutes for the entire deployment. Too high
and it walks into attacker-supplied entries.

It ships at 1 because under-trusting is the safe direction to be wrong in. Correct it by measurement, not
arithmetic:

1. Temporarily raise logging: `Logging:LogLevel:Microsoft.AspNetCore.HttpOverrides` to `Debug`.
2. Make a request from a known public IP through `https://develop.kompaz.igne.link/api/...`.
3. The middleware logs the full `X-Forwarded-For` chain it received. Count the position of your real address from
   the right; that count is `ForwardLimit`.
4. Set it, redeploy, and confirm two different clients get separate rate-limit budgets.

Note also that `igne-proxy` sets `ForwardedHeadersOptions.AllowedHosts` to loopback and registers no
`KnownProxies`, so what it believes about the caller — and therefore what it passes on — is not obvious from
reading its configuration. Measure rather than reason.

## Verifying a deployment

Beyond a green pipeline, run the actual auth flow:

```bash
curl -X POST https://develop.kompaz.igne.link/api/auth/magic-link \
  -H 'Content-Type: application/json' -d '{"email":"admin@kompaz.local"}'      # 202

# read the link out of the Mailtrap inbox, then
curl -X POST https://develop.kompaz.igne.link/api/auth/tokens \
  -H 'Content-Type: application/json' -d '{"token":"<from the link>"}'         # 200 + session

curl https://develop.kompaz.igne.link/api/auth/me -H 'Authorization: Bearer <accessToken>'
```

Swagger is on in this environment at `/swagger`.

## Rolling back

Deployments are by immutable commit SHA, so a rollback is a redeploy of the previous tag:

```bash
az containerapp update -g Kompaz -n ca-kompaz-api-develop \
  --image acrphase2.azurecr.io/kompaz-web-backend:<previous-sha>
```

Or activate the previous revision directly with `az containerapp revision activate`. Note that a rollback does
**not** undo an EF migration — if the bad deploy migrated the schema, the older image will refuse to start against
a database ahead of it.

## Things deliberately not done

- **Private networking for PostgreSQL.** The server is public with a firewall, including an
  allow-all-Azure-services rule, which is how the other five projects already reach it.
- **Locking the Container Apps ingress down.** `ForwardedHeaders:TrustAnyProxy` is `true`, so anyone reaching the
  container app's own FQDN directly can spoof `X-Forwarded-For` and evade the rate limiter. The fix is
  `ipSecurityRestrictions` on the ingress allowing only `igne-proxy`'s 32 possible outbound addresses — free, but
  brittle, and the list changes if the plan moves. Acceptable for develop; not beyond it.
- **Application Insights or OpenTelemetry.** The app has neither package. Container stdout reaches Log Analytics.
- **A separate migration step.** `Database:MigrateOnStartup` is `true` and the app runs a single replica, which is
  safe. Above one replica, turn it off and run `dotnet ef database update` as its own step.
- **Staging and production.** The templates are parameterised; a second environment is another resource group and
  another parameter set.

## Cost

West Europe, EUR, excluding VAT, from the Azure retail price API.

| Line | €/month |
| --- | ---: |
| Container app — vCPU, 0.5 × 1 replica at the idle rate, after the 180k vCPU-second free grant | 3.90 |
| Container app — memory, 1 GiB, after the 360k GiB-second free grant | 7.79 |
| Log Analytics ingestion, ~0.5 GB | ~1.50 |
| Key Vault, a few hundred operations | ~0 |
| Container Apps environment, Static Web App Free, managed identity | 0 |
| PostgreSQL, registry, proxy — all reused | 0 |
| **Total** | **~13.19** |

Two things to know about that figure. The free grants are **per subscription**, shared with anything else running
on Container Apps in it. And the vCPU line assumes the replica is mostly idle, which is the whole point of a
develop environment — a genuinely busy container bills vCPU at 8.6× the idle rate, taking the total to roughly €43.

Setting `minReplicas` to `0` removes €11.69 of that, at the cost of a cold start that includes the EF migration
check. The deploy workflow's health gate expects a running replica, so it needs adjusting first: a scaled-to-zero
revision reports `ScaledToZero` and `None` rather than `Running` and `Healthy`.
