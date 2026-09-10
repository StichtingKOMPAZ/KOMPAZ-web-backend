<#
.SYNOPSIS
    One-time setup of the KOMPAZ develop environment in Azure.

.DESCRIPTION
    Creates the resource group, deploys infra/foundation.bicep, provisions a database and role on the shared
    PostgreSQL server, seeds the key vault, registers the GitHub OIDC identity, then builds and deploys the first
    image through infra/app.bicep.

    Safe to re-run. Secrets are only generated when absent: regenerating the signing key would invalidate every
    access token and refresh token in circulation, so an existing value is always kept.

.EXAMPLE
    ./scripts/bootstrap-azure.ps1 -MailtrapUserName abc123 -MailtrapPassword def456
#>
[CmdletBinding()]
param(
    [string]$SubscriptionId = 'a71fe690-e0f5-4d62-9990-1ab15fefc2b9',
    [string]$ResourceGroup = 'Kompaz',
    [string]$Location = 'westeurope',
    [string]$KeyVaultName = 'kv-kompaz-develop',

    # Shared with the Phase2 project. Read only: the script reads its admin credentials, and changes nothing.
    [string]$AcrName = 'acrphase2',
    [string]$AcrResourceGroup = 'Phase2',

    # The PostgreSQL server is shared with five other projects. Nothing here alters the server itself.
    [string]$PostgresResourceGroup = 'Igne',
    [string]$PostgresServer = 'igne-postgres',
    [string]$DatabaseName = 'develop-kompaz',
    [string]$DatabaseRole = 'kompaz',

    # Supplying this lets the script create the login role itself. Without it, the SQL is printed to run by hand.
    [string]$PostgresAdminPassword,

    [Parameter(Mandatory = $true)][string]$MailtrapUserName,
    [Parameter(Mandatory = $true)][string]$MailtrapPassword,

    [string]$GitHubRepository = 'StichtingKOMPAZ/KOMPAZ-web-backend',
    [string]$GitHubAppName = 'kompaz-web-backend-deploy',
    [switch]$SkipDatabaseRole,
    [switch]$SkipGitHubOidc,
    [switch]$SkipFirstDeploy
)

# Stop applies to cmdlet errors. Native commands are checked with $LASTEXITCODE instead, and their stderr is
# never redirected: in Windows PowerShell, redirecting a native command's stderr wraps each line in an ErrorRecord,
# so a harmless `az` warning becomes a terminating error under this preference.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "=== $Message ===" -ForegroundColor Cyan
}

function New-RandomPassword([int]$Length = 32) {
    # Alphanumeric only. A password reaching a connection string must not contain ';' or '=', and avoiding the
    # base64 alphabet entirely saves worrying about which of '+' and '/' any given tool escapes.
    $alphabet = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'
    $bytes = New-Object byte[] $Length
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $chars = foreach ($b in $bytes) { $alphabet[$b % $alphabet.Length] }
    return -join $chars
}

function New-SigningKey {
    # 48 bytes base64 is 64 characters, comfortably past the 32-byte minimum AuthenticationSettings.Validate enforces.
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return [Convert]::ToBase64String($bytes)
}

function Get-OrSetSecret([string]$Vault, [string]$Name, [scriptblock]$Generate) {
    # Listing first, rather than asking for the secret and treating the failure as "absent". Stderr cannot be
    # redirected here without turning it into a terminating error, so a probe that is expected to miss on a first
    # run would print an alarming SecretNotFound every time.
    $names = az keyvault secret list --vault-name $Vault --query "[].name" -o tsv
    if ($LASTEXITCODE -ne 0) { throw "Could not list secrets in '$Vault'." }

    if ($names -contains $Name) {
        $existing = az keyvault secret show --vault-name $Vault --name $Name --query value -o tsv
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($existing)) {
            Write-Host "  $Name already set, keeping it"
            return $existing
        }
    }

    $value = & $Generate
    az keyvault secret set --vault-name $Vault --name $Name --value $value --output none
    if ($LASTEXITCODE -ne 0) { throw "Failed to write secret '$Name'." }
    Write-Host "  $Name created" -ForegroundColor Green
    return $value
}

function Set-Secret([string]$Vault, [string]$Name, [string]$Value) {
    az keyvault secret set --vault-name $Vault --name $Name --value $Value --output none
    if ($LASTEXITCODE -ne 0) { throw "Failed to write secret '$Name'." }
    Write-Host "  $Name set" -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------------------------------

Write-Step "Subscription"
az account set --subscription $SubscriptionId
if ($LASTEXITCODE -ne 0) { throw "Could not select subscription $SubscriptionId." }
$accountJson = az account show -o json
if ($LASTEXITCODE -ne 0) { throw "Could not read the current account." }
$account = $accountJson | ConvertFrom-Json
Write-Host "  $($account.name) ($($account.id))"
Write-Host "  signed in as $($account.user.name)"

Write-Step "Resource group"
az group create --name $ResourceGroup --location $Location --output none
if ($LASTEXITCODE -ne 0) { throw "Could not create resource group $ResourceGroup." }
Write-Host "  $ResourceGroup in $Location"

Write-Step "Foundation: identity and key vault"
# The vault grants this object id write access through an access policy, so the secret writes below can work.
# Note that a policy is a property of the vault, not a role assignment: the Owner role here carries an ABAC
# condition permitting only Reader and Contributor to be assigned, which rules out the RBAC vault roles.
$deployerObjectId = az ad signed-in-user show --query id -o tsv
if ([string]::IsNullOrWhiteSpace($deployerObjectId)) {
    # Running as a service principal rather than a person.
    $deployerObjectId = az ad sp show --id $account.user.name --query id -o tsv
}

$foundationJson = az deployment group create `
    --resource-group $ResourceGroup `
    --name "kompaz-foundation" `
    --template-file (Join-Path $repoRoot 'infra/foundation.bicep') `
    --parameters keyVaultName=$KeyVaultName deployerPrincipalId=$deployerObjectId `
    --query properties.outputs -o json
if ($LASTEXITCODE -ne 0) { throw "Foundation deployment failed." }
$foundation = $foundationJson | ConvertFrom-Json

$identityResourceId = $foundation.identityResourceId.value
$keyVaultUri = $foundation.keyVaultUri.value
Write-Host "  identity     $identityResourceId"
Write-Host "  vault        $keyVaultUri"

# The container app authenticates to the registry with admin credentials rather than the managed identity.
# Granting AcrPull would need Microsoft.Authorization/roleAssignments/write for that role, and the Owner role on
# this subscription carries an ABAC condition allowing only Reader and Contributor to be assigned. The password
# goes into the vault like every other secret, so it is not stored in the template or in the app definition.
$acrId = az acr show --name $AcrName --resource-group $AcrResourceGroup --query id -o tsv
if ($LASTEXITCODE -ne 0) { throw "Could not find registry $AcrName in $AcrResourceGroup." }
$acrLoginServer = az acr show --name $AcrName --resource-group $AcrResourceGroup --query loginServer -o tsv

$acrPassword = az acr credential show --name $AcrName --resource-group $AcrResourceGroup --query "passwords[0].value" -o tsv
if ($LASTEXITCODE -ne 0) {
    throw "Could not read admin credentials for $AcrName. Enable them with: az acr update -n $AcrName --admin-enabled true"
}
Write-Host "  registry     $acrLoginServer"

Write-Host "  waiting for the Key Vault access policy to take effect"
$deadline = (Get-Date).AddMinutes(3)
while ($true) {
    az keyvault secret list --vault-name $KeyVaultName --query "[].name" -o tsv | Out-Null
    if ($LASTEXITCODE -eq 0) { break }
    if ((Get-Date) -gt $deadline) { throw "Key Vault data-plane access did not become available within 3 minutes." }
    Start-Sleep -Seconds 10
}

Write-Step "Database on $PostgresServer"
# Creating the database needs no credentials; creating the login role needs SQL, so that part is optional.
az postgres flexible-server db create `
    --resource-group $PostgresResourceGroup `
    --server-name $PostgresServer `
    --database-name $DatabaseName `
    --output none
if ($LASTEXITCODE -ne 0) {
    Write-Host "  database $DatabaseName already exists, continuing"
}
else {
    Write-Host "  database $DatabaseName ready"
}

$databasePassword = Get-OrSetSecret $KeyVaultName 'kompazdb-password' { New-RandomPassword 32 }

if ($SkipDatabaseRole) {
    Write-Host "  skipping the login role, as asked"
}
elseif ($PostgresAdminPassword) {
    # `az postgres flexible-server execute` lives in an extension that is not installed by default.
    $hasConnect = az extension list --query "[?name=='rdbms-connect'] | length(@)" -o tsv
    if ($hasConnect -eq '0') {
        Write-Host "  installing the rdbms-connect extension"
        az extension add --name rdbms-connect --yes --output none
    }

    # One statement per call, deliberately. A multi-line `DO $$ ... $$` block does not survive being passed
    # through --querytext, and the failure is quiet: the role never appears, the connection string written below
    # still looks right, and the first sign of trouble is the container failing to start with
    # "28P01: password authentication failed".
    function Invoke-Sql([string]$Database, [string]$Sql) {
        az postgres flexible-server execute `
            --name $PostgresServer `
            --admin-user 'igne' `
            --admin-password $PostgresAdminPassword `
            --database-name $Database `
            --querytext $Sql `
            --output none
        return $LASTEXITCODE -eq 0
    }

    Write-Host "  creating login role $DatabaseRole"

    # Postgres has no CREATE ROLE IF NOT EXISTS. Try to create, and fall back to setting the password on a role
    # that is already there, which is what every re-run after the first does.
    $created = Invoke-Sql 'postgres' "CREATE ROLE ""$DatabaseRole"" LOGIN PASSWORD '$databasePassword';"
    if (-not $created) {
        $created = Invoke-Sql 'postgres' "ALTER ROLE ""$DatabaseRole"" WITH LOGIN PASSWORD '$databasePassword';"
    }
    if (-not $created) {
        throw "Could not create or update the login role '$DatabaseRole' on $PostgresServer. Check that your public IP is on the server firewall, then re-run."
    }

    if (-not (Invoke-Sql 'postgres' "GRANT ALL PRIVILEGES ON DATABASE ""$DatabaseName"" TO ""$DatabaseRole"";")) {
        throw "Could not grant $DatabaseRole privileges on database $DatabaseName."
    }

    # Not optional, and not obvious. Since PostgreSQL 15 the public schema no longer grants CREATE to everybody,
    # so a role that can connect still cannot create tables and EF migrations fail on the first CREATE TABLE.
    # This has to run connected to the database itself rather than to `postgres`.
    if (-not (Invoke-Sql $DatabaseName "GRANT ALL ON SCHEMA public TO ""$DatabaseRole"";")) {
        throw "Could not grant $DatabaseRole rights on the public schema of $DatabaseName."
    }
    if (-not (Invoke-Sql $DatabaseName "ALTER SCHEMA public OWNER TO ""$DatabaseRole"";")) {
        throw "Could not transfer ownership of the public schema of $DatabaseName to $DatabaseRole."
    }

    Write-Host "  role $DatabaseRole ready" -ForegroundColor Green
}
else {
    # Deliberately fatal rather than a warning. Continuing would write a connection string for a role that does
    # not exist and deploy a container that cannot start.
    throw @"
No -PostgresAdminPassword given, so the login role cannot be created.

Either re-run with -PostgresAdminPassword, or run this by hand and then re-run with -SkipDatabaseRole:

  -- connected to 'postgres':
  CREATE ROLE "$DatabaseRole" LOGIN PASSWORD '<the kompazdb-password secret>';
  GRANT ALL PRIVILEGES ON DATABASE "$DatabaseName" TO "$DatabaseRole";

  -- connected to '$DatabaseName' (PostgreSQL 15+ no longer lets every role create in public,
  -- so without this EF migrations fail on the first CREATE TABLE):
  GRANT ALL ON SCHEMA public TO "$DatabaseRole";
  ALTER SCHEMA public OWNER TO "$DatabaseRole";

Read the password with:
  az keyvault secret show --vault-name $KeyVaultName --name kompazdb-password --query value -o tsv
"@
}

Write-Step "Secrets"
# Maximum Pool Size matters more than it looks. The server allows 50 connections in total and already serves five
# other databases; Npgsql would otherwise default to a pool of 100 per instance and could starve every other app.
$connectionString = "Host=$PostgresServer.postgres.database.azure.com;Port=5432;Database=$DatabaseName;" +
    "Username=$DatabaseRole;Password=$databasePassword;Ssl Mode=Require;Trust Server Certificate=true;" +
    "Maximum Pool Size=10"

Set-Secret $KeyVaultName 'acr-password' $acrPassword
Set-Secret $KeyVaultName 'kompazdb-connection-string' $connectionString
$null = Get-OrSetSecret $KeyVaultName 'authentication-signing-key' { New-SigningKey }
Set-Secret $KeyVaultName 'smtp-username' $MailtrapUserName
Set-Secret $KeyVaultName 'smtp-password' $MailtrapPassword

if (-not $SkipGitHubOidc) {
    Write-Step "GitHub OIDC"
    # Federated credentials rather than a client secret: nothing long-lived ends up in GitHub.
    $appId = az ad app list --display-name $GitHubAppName --query "[0].appId" -o tsv
    if ([string]::IsNullOrWhiteSpace($appId)) {
        $appId = az ad app create --display-name $GitHubAppName --query appId -o tsv
        Write-Host "  created app registration $GitHubAppName"
    }
    else {
        Write-Host "  reusing app registration $GitHubAppName"
    }

    $spId = az ad sp list --filter "appId eq '$appId'" --query "[0].id" -o tsv
    if ([string]::IsNullOrWhiteSpace($spId)) {
        $spId = az ad sp create --id $appId --query id -o tsv
    }

    foreach ($subject in @("repo:${GitHubRepository}:ref:refs/heads/main", "repo:${GitHubRepository}:environment:develop")) {
        $credentialName = ($subject -replace '[^A-Za-z0-9]', '-')
        $exists = az ad app federated-credential list --id $appId --query "[?subject=='$subject'] | length(@)" -o tsv
        if ($exists -eq '0') {
            $body = @{
                name      = $credentialName
                issuer    = 'https://token.actions.githubusercontent.com'
                subject   = $subject
                audiences = @('api://AzureADTokenExchange')
            } | ConvertTo-Json -Compress

            $tempFile = New-TemporaryFile
            Set-Content -Path $tempFile -Value $body -Encoding utf8
            az ad app federated-credential create --id $appId --parameters "@$tempFile" --output none
            Remove-Item $tempFile -Force
            Write-Host "  federated credential for $subject" -ForegroundColor Green
        }
        else {
            Write-Host "  federated credential for $subject already exists"
        }
    }

    # Contributor on this resource group only, plus push rights on the shared registry. Deliberately not
    # subscription-scoped: this principal has no business touching the other five projects.
    az role assignment create --assignee-object-id $spId --assignee-principal-type ServicePrincipal `
        --role Contributor --scope "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup" --output none
    az role assignment create --assignee-object-id $spId --assignee-principal-type ServicePrincipal `
        --role Contributor --scope $acrId --output none
    Write-Host "  role assignments applied"

    # The deploy job runs in a GitHub environment named "develop", and one of the federated credentials above is
    # scoped to exactly that. If the environment does not exist the token subject will not match and the login step
    # fails with an unhelpful "no matching federated identity record found".
    # Also probe the default install location. A shell opened before `winget install GitHub.cli` has a stale PATH
    # and will not find gh even though it is right there, which otherwise skips this whole step for no reason.
    $gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
    if (-not $gh) {
        foreach ($candidate in @(
                "$env:ProgramFiles\GitHub CLI\gh.exe",
                "${env:ProgramFiles(x86)}\GitHub CLI\gh.exe",
                "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe")) {
            if (Test-Path $candidate) { $gh = $candidate; break }
        }
    }

    $ghAvailable = $null -ne $gh
    $ghAuthenticated = $false
    if ($ghAvailable) {
        & $gh auth status | Out-Null
        $ghAuthenticated = $LASTEXITCODE -eq 0
    }

    if ($ghAuthenticated) {
        Write-Host "  configuring the repository with gh"

        & $gh api -X PUT "repos/$GitHubRepository/environments/develop" --silent
        if ($?) { Write-Host "    environment 'develop' ready" -ForegroundColor Green }
        else { Write-Warning "    could not create the 'develop' environment; create it by hand" }

        $secrets = [ordered]@{
            AZURE_CLIENT_ID       = $appId
            AZURE_TENANT_ID       = $account.tenantId
            AZURE_SUBSCRIPTION_ID = $SubscriptionId
        }
        foreach ($name in $secrets.Keys) {
            & $gh secret set $name --repo $GitHubRepository --body $secrets[$name]
            if ($?) { Write-Host "    $name set" -ForegroundColor Green }
            else { Write-Warning "    could not set $name" }
        }
    }
    else {
        if (-not $ghAvailable) {
            Write-Warning "The gh CLI was not found, so the repository was not configured."
            Write-Host "  Install it with: winget install --id GitHub.cli"
            Write-Host "  Then: gh auth login, and re-run this script (everything else is idempotent)."
        }
        else {
            Write-Warning "The gh CLI is not authenticated. Run 'gh auth login' and re-run this script."
        }

        Write-Host ''
        Write-Host "  Or do it by hand on ${GitHubRepository}:" -ForegroundColor Yellow
        Write-Host "    1. Create an environment named 'develop' (Settings > Environments)."
        Write-Host "    2. Add these repository secrets (Settings > Secrets and variables > Actions):"
        Write-Host "         AZURE_CLIENT_ID        $appId"
        Write-Host "         AZURE_TENANT_ID        $($account.tenantId)"
        Write-Host "         AZURE_SUBSCRIPTION_ID  $SubscriptionId"
    }
}

if (-not $SkipFirstDeploy) {
    Write-Step "First image"
    # Built in the registry rather than locally, so this works from a machine without Docker.
    $tag = (git -C $repoRoot rev-parse --short HEAD)
    $image = "$acrLoginServer/kompaz-web-backend:$tag"
    az acr build --registry $AcrName --image "kompaz-web-backend:$tag" --file src/Presentation/Dockerfile $repoRoot
    if ($LASTEXITCODE -ne 0) { throw "Image build failed." }
    Write-Host "  built $image"

    Write-Step "Application"
    $appJson = az deployment group create `
        --resource-group $ResourceGroup `
        --name "kompaz-app" `
        --template-file (Join-Path $repoRoot 'infra/app.bicep') `
        --parameters image=$image identityResourceId=$identityResourceId keyVaultUri=$keyVaultUri `
        --query properties.outputs -o json
    if ($LASTEXITCODE -ne 0) { throw "Application deployment failed." }
    $app = $appJson | ConvertFrom-Json

    Write-Host ''
    Write-Host "  API        $($app.containerAppUrl.value)" -ForegroundColor Green
    Write-Host "  frontend   https://$($app.staticWebAppHostname.value)" -ForegroundColor Green
    Write-Host ''
    Write-Host "  Point the igne-proxy clusters at those two addresses. See docs/deployment.md." -ForegroundColor Yellow
}

Write-Host ''
Write-Host "Done." -ForegroundColor Green
