#!/usr/bin/env bash
#
# One-time setup of the KOMPAZ develop environment in Azure.
#
# The bash counterpart of bootstrap-azure.ps1. Same steps, same order, same names - use whichever suits the
# machine. Keep the two in step when either changes.
#
# Usage:
#   ./scripts/bootstrap-azure.sh --mailtrap-username <user> --mailtrap-password <password> \
#                                [--postgres-admin-password <password>]
#
# Creates the resource group, deploys infra/foundation.bicep, provisions a database and login role on the shared
# PostgreSQL server, seeds the key vault, registers the GitHub OIDC identity, then builds and deploys the first
# image through infra/app.bicep.
#
# Safe to re-run. Secrets are only generated when absent: regenerating the signing key would invalidate every
# access token and refresh token in circulation, so an existing value is always kept.

set -euo pipefail

SUBSCRIPTION_ID="a71fe690-e0f5-4d62-9990-1ab15fefc2b9"
RESOURCE_GROUP="Kompaz"
LOCATION="westeurope"
KEY_VAULT_NAME="kv-kompaz-develop"

# Shared with the Phase2 project. Read only: this reads its admin credentials and changes nothing.
ACR_NAME="acrphase2"
ACR_RESOURCE_GROUP="Phase2"

# The PostgreSQL server is shared with five other projects. Nothing here alters the server itself.
POSTGRES_RESOURCE_GROUP="Igne"
POSTGRES_SERVER="igne-postgres"
POSTGRES_ADMIN_USER="igne"
POSTGRES_ADMIN_PASSWORD=""
DATABASE_NAME="develop-kompaz"
DATABASE_ROLE="kompaz"

MAILTRAP_USERNAME=""
MAILTRAP_PASSWORD=""

GITHUB_REPOSITORY="StichtingKOMPAZ/KOMPAZ-web-backend"
GITHUB_APP_NAME="kompaz-web-backend-deploy"

SKIP_DATABASE_ROLE=false
SKIP_GITHUB_OIDC=false
SKIP_FIRST_DEPLOY=false

while [[ $# -gt 0 ]]; do
	case "$1" in
		--subscription-id)           SUBSCRIPTION_ID="$2"; shift 2 ;;
		--resource-group)            RESOURCE_GROUP="$2"; shift 2 ;;
		--location)                  LOCATION="$2"; shift 2 ;;
		--key-vault-name)            KEY_VAULT_NAME="$2"; shift 2 ;;
		--acr-name)                  ACR_NAME="$2"; shift 2 ;;
		--acr-resource-group)        ACR_RESOURCE_GROUP="$2"; shift 2 ;;
		--postgres-admin-user)       POSTGRES_ADMIN_USER="$2"; shift 2 ;;
		--postgres-admin-password)   POSTGRES_ADMIN_PASSWORD="$2"; shift 2 ;;
		--database-name)             DATABASE_NAME="$2"; shift 2 ;;
		--database-role)             DATABASE_ROLE="$2"; shift 2 ;;
		--mailtrap-username)         MAILTRAP_USERNAME="$2"; shift 2 ;;
		--mailtrap-password)         MAILTRAP_PASSWORD="$2"; shift 2 ;;
		--github-repository)         GITHUB_REPOSITORY="$2"; shift 2 ;;
		--skip-database-role)        SKIP_DATABASE_ROLE=true; shift ;;
		--skip-github-oidc)          SKIP_GITHUB_OIDC=true; shift ;;
		--skip-first-deploy)         SKIP_FIRST_DEPLOY=true; shift ;;
		-h|--help)                   sed -n '2,20p' "$0"; exit 0 ;;
		*) echo "unknown argument '$1'" >&2; exit 1 ;;
	esac
done

[[ -n "$MAILTRAP_USERNAME" ]] || { echo "--mailtrap-username is required" >&2; exit 1; }
[[ -n "$MAILTRAP_PASSWORD" ]] || { echo "--mailtrap-password is required" >&2; exit 1; }

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

step() { printf '\n\033[1;36m=== %s ===\033[0m\n' "$1"; }
ok()   { printf '\033[1;32m  %s\033[0m\n' "$1"; }
warn() { printf '\033[1;33m  %s\033[0m\n' "$1"; }
die()  { printf '\033[1;31m%s\033[0m\n' "$1" >&2; exit 1; }

command -v az >/dev/null || die "az CLI not found"

# Command substitution strips a trailing newline but never a carriage return, and under Git Bash on Windows both
# az and openssl emit CRLF. A stray CR inside a GUID, a password or a resource id fails somewhere far away from
# here and reads like a completely different problem, so every captured value goes through this.
azv() { az "$@" | tr -d '\r'; }

# ---------------------------------------------------------------------------------------------- helpers

# `head -c` closes the pipe early, which is a SIGPIPE that pipefail would turn into a failure. Disabling it
# inside the subshell keeps that contained rather than switching it off for the whole script.
new_password() {
	( set +o pipefail; LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | head -c 32 )
}

# 48 bytes of base64 is 64 characters, comfortably past the 32-byte minimum AuthenticationSettings.Validate
# enforces. Alphanumeric is not required here: this value never goes into a connection string.
new_signing_key() {
	openssl rand -base64 48 | tr -d '\r\n'
}

vault_secret_names() {
	azv keyvault secret list --vault-name "$KEY_VAULT_NAME" --query "[].name" -o tsv
}

# Listing first, rather than asking for the secret and reading the failure as "absent", so a first run does not
# print an alarming SecretNotFound for every secret it is about to create.
get_or_set_secret() {
	local name="$1" generator="$2" existing

	if vault_secret_names | grep -qx "$name"; then
		existing="$(azv keyvault secret show --vault-name "$KEY_VAULT_NAME" --name "$name" --query value -o tsv)"
		if [[ -n "$existing" ]]; then
			echo "  $name already set, keeping it" >&2
			printf '%s' "$existing"
			return 0
		fi
	fi

	local value
	value="$("$generator")"
	az keyvault secret set --vault-name "$KEY_VAULT_NAME" --name "$name" --value "$value" --output none \
		|| die "Failed to write secret '$name'."
	ok "$name created" >&2
	printf '%s' "$value"
}

set_secret() {
	az keyvault secret set --vault-name "$KEY_VAULT_NAME" --name "$1" --value "$2" --output none \
		|| die "Failed to write secret '$1'."
	ok "$1 set"
}

# SQL goes in through --file-path, never --querytext. A file has no shell in the middle of it: on Windows the
# `az` launcher strips the double quotes around a quoted identifier, and "develop-kompaz" cannot survive
# unquoted because of the hyphen - Postgres reads it as a subtraction and reports a syntax error naming nothing
# useful. Same call shape on every platform is worth more than saving a temp file.
run_sql() {
	local database="$1" sql="$2" label="$3" file
	file="$(mktemp)"
	printf '%s\n' "$sql" > "$file"

	if az postgres flexible-server execute \
		--name "$POSTGRES_SERVER" \
		--admin-user "$POSTGRES_ADMIN_USER" \
		--admin-password "$POSTGRES_ADMIN_PASSWORD" \
		--database-name "$database" \
		--file-path "$file" \
		--output none
	then
		rm -f "$file"
		return 0
	fi

	rm -f "$file"
	# The server's own error has already gone to stderr. Name the statement, because "execute failed" on its
	# own sends you looking at the firewall.
	warn "failed: $label"
	echo "    $sql"
	return 1
}

# ------------------------------------------------------------------------------------------ subscription

step "Subscription"
az account set --subscription "$SUBSCRIPTION_ID" || die "Could not select subscription $SUBSCRIPTION_ID."
ACCOUNT_NAME="$(azv account show --query name -o tsv)"
ACCOUNT_USER="$(azv account show --query user.name -o tsv)"
TENANT_ID="$(azv account show --query tenantId -o tsv)"
echo "  $ACCOUNT_NAME ($SUBSCRIPTION_ID)"
echo "  signed in as $ACCOUNT_USER"

step "Resource group"
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none \
	|| die "Could not create resource group $RESOURCE_GROUP."
echo "  $RESOURCE_GROUP in $LOCATION"

# -------------------------------------------------------------------------------------------- foundation

step "Foundation: identity and key vault"
# The vault grants this object id write access through an access policy, so the secret writes below can work.
# A policy is a property of the vault, not a role assignment: the Owner role on this subscription carries an
# ABAC condition permitting only Reader and Contributor to be assigned, which rules out the RBAC vault roles.
DEPLOYER_OBJECT_ID="$(azv ad signed-in-user show --query id -o tsv || true)"
if [[ -z "$DEPLOYER_OBJECT_ID" ]]; then
	# Running as a service principal rather than a person.
	DEPLOYER_OBJECT_ID="$(azv ad sp show --id "$ACCOUNT_USER" --query id -o tsv)"
fi

FOUNDATION="$(az deployment group create \
	--resource-group "$RESOURCE_GROUP" \
	--name kompaz-foundation \
	--template-file "$ROOT/infra/foundation.bicep" \
	--parameters keyVaultName="$KEY_VAULT_NAME" deployerPrincipalId="$DEPLOYER_OBJECT_ID" \
	--query properties.outputs -o json)" || die "Foundation deployment failed."

IDENTITY_RESOURCE_ID="$(printf '%s' "$FOUNDATION" | python -c 'import json,sys; print(json.load(sys.stdin)["identityResourceId"]["value"])')"
KEY_VAULT_URI="$(printf '%s' "$FOUNDATION" | python -c 'import json,sys; print(json.load(sys.stdin)["keyVaultUri"]["value"])')"
echo "  identity     $IDENTITY_RESOURCE_ID"
echo "  vault        $KEY_VAULT_URI"

# The container app authenticates to the registry with admin credentials rather than the managed identity.
# Granting AcrPull needs Microsoft.Authorization/roleAssignments/write for that role, which the ABAC condition
# above forbids. The password goes into the vault like every other secret.
ACR_ID="$(azv acr show --name "$ACR_NAME" --resource-group "$ACR_RESOURCE_GROUP" --query id -o tsv)" \
	|| die "Could not find registry $ACR_NAME in $ACR_RESOURCE_GROUP."
ACR_LOGIN_SERVER="$(azv acr show --name "$ACR_NAME" --resource-group "$ACR_RESOURCE_GROUP" --query loginServer -o tsv)"
ACR_PASSWORD="$(azv acr credential show --name "$ACR_NAME" --resource-group "$ACR_RESOURCE_GROUP" --query 'passwords[0].value' -o tsv)" \
	|| die "Could not read admin credentials for $ACR_NAME. Enable them with: az acr update -n $ACR_NAME --admin-enabled true"
echo "  registry     $ACR_LOGIN_SERVER"

echo "  waiting for the Key Vault access policy to take effect"
deadline=$(( $(date +%s) + 180 ))
until vault_secret_names >/dev/null 2>&1; do
	[[ $(date +%s) -lt $deadline ]] || die "Key Vault data-plane access did not become available within 3 minutes."
	sleep 10
done

# ---------------------------------------------------------------------------------------------- database

step "Database on $POSTGRES_SERVER"
if az postgres flexible-server db create \
	--resource-group "$POSTGRES_RESOURCE_GROUP" \
	--server-name "$POSTGRES_SERVER" \
	--database-name "$DATABASE_NAME" \
	--output none
then
	echo "  database $DATABASE_NAME ready"
else
	echo "  database $DATABASE_NAME already exists, continuing"
fi

DATABASE_PASSWORD="$(get_or_set_secret 'kompazdb-password' new_password)"

if [[ "$SKIP_DATABASE_ROLE" == "true" ]]; then
	echo "  skipping the login role, as asked"
elif [[ -n "$POSTGRES_ADMIN_PASSWORD" ]]; then
	if ! az extension show --name rdbms-connect >/dev/null 2>&1; then
		echo "  installing the rdbms-connect extension"
		az extension add --name rdbms-connect --yes --output none
	fi

	echo "  creating login role $DATABASE_ROLE"

	# Postgres has no CREATE ROLE IF NOT EXISTS. Try to create, and fall back to setting the password on a role
	# that is already there, which is what every re-run after the first does.
	if ! run_sql 'postgres' "CREATE ROLE \"$DATABASE_ROLE\" LOGIN PASSWORD '$DATABASE_PASSWORD';" "create role $DATABASE_ROLE"; then
		run_sql 'postgres' "ALTER ROLE \"$DATABASE_ROLE\" WITH LOGIN PASSWORD '$DATABASE_PASSWORD';" "set the password on the existing role" \
			|| die "Could not create or update the login role '$DATABASE_ROLE'. Check that your public IP is on the server firewall, then re-run."
	fi

	run_sql 'postgres' "GRANT ALL PRIVILEGES ON DATABASE \"$DATABASE_NAME\" TO \"$DATABASE_ROLE\";" "grant on database $DATABASE_NAME" \
		|| die "Could not grant $DATABASE_ROLE privileges on database $DATABASE_NAME."

	# Not optional, and not obvious. Since PostgreSQL 15 the public schema no longer grants CREATE to everybody,
	# so a role that can connect still cannot create tables and EF migrations fail on the first CREATE TABLE.
	# This has to run connected to the database itself rather than to `postgres`.
	run_sql "$DATABASE_NAME" "GRANT ALL ON SCHEMA public TO \"$DATABASE_ROLE\";" "grant on schema public" \
		|| die "Could not grant $DATABASE_ROLE rights on the public schema of $DATABASE_NAME."

	# Best effort. The GRANT above is what migrations need; ownership only matters for altering the schema
	# itself, and on Azure public is owned by pg_database_owner, which the server admin cannot always reassign.
	run_sql "$DATABASE_NAME" "ALTER SCHEMA public OWNER TO \"$DATABASE_ROLE\";" "transfer ownership of schema public" \
		|| echo "  ownership of schema public left as it is; the GRANT above is what migrations need"

	ok "role $DATABASE_ROLE ready"
else
	# Deliberately fatal. Continuing would write a connection string for a role that does not exist and deploy
	# a container that cannot start.
	cat >&2 <<EOF

No --postgres-admin-password given, so the login role cannot be created.

Either re-run with --postgres-admin-password, or run this by hand and re-run with --skip-database-role:

  -- connected to 'postgres':
  CREATE ROLE "$DATABASE_ROLE" LOGIN PASSWORD '<the kompazdb-password secret>';
  GRANT ALL PRIVILEGES ON DATABASE "$DATABASE_NAME" TO "$DATABASE_ROLE";

  -- connected to '$DATABASE_NAME' (PostgreSQL 15+ no longer lets every role create in public,
  -- so without this EF migrations fail on the first CREATE TABLE):
  GRANT ALL ON SCHEMA public TO "$DATABASE_ROLE";

Read the password with:
  az keyvault secret show --vault-name $KEY_VAULT_NAME --name kompazdb-password --query value -o tsv
EOF
	exit 1
fi

# ----------------------------------------------------------------------------------------------- secrets

step "Secrets"
# Maximum Pool Size matters more than it looks. The server allows 50 connections in total and already serves
# five other databases; Npgsql would otherwise default to a pool of 100 per instance and could starve them all.
CONNECTION_STRING="Host=${POSTGRES_SERVER}.postgres.database.azure.com;Port=5432;Database=${DATABASE_NAME};Username=${DATABASE_ROLE};Password=${DATABASE_PASSWORD};Ssl Mode=Require;Trust Server Certificate=true;Maximum Pool Size=10"

set_secret 'acr-password' "$ACR_PASSWORD"
set_secret 'kompazdb-connection-string' "$CONNECTION_STRING"
get_or_set_secret 'authentication-signing-key' new_signing_key >/dev/null
set_secret 'smtp-username' "$MAILTRAP_USERNAME"
set_secret 'smtp-password' "$MAILTRAP_PASSWORD"

# ------------------------------------------------------------------------------------------ github oidc

if [[ "$SKIP_GITHUB_OIDC" != "true" ]]; then
	step "GitHub OIDC"
	# Federated credentials rather than a client secret: nothing long-lived ends up in GitHub.
	APP_ID="$(azv ad app list --display-name "$GITHUB_APP_NAME" --query '[0].appId' -o tsv)"
	if [[ -z "$APP_ID" ]]; then
		APP_ID="$(azv ad app create --display-name "$GITHUB_APP_NAME" --query appId -o tsv)"
		echo "  created app registration $GITHUB_APP_NAME"
	else
		echo "  reusing app registration $GITHUB_APP_NAME"
	fi

	SP_ID="$(azv ad sp list --filter "appId eq '$APP_ID'" --query '[0].id' -o tsv)"
	if [[ -z "$SP_ID" ]]; then
		SP_ID="$(azv ad sp create --id "$APP_ID" --query id -o tsv)"
	fi

	# Two subject formats, both registered, because GitHub may present either. The plain one is the documented
	# form; the other embeds the numeric organisation and repository ids - GitHub's immutable subject claim,
	# which exists so a deleted org or repo name cannot be re-registered by someone else and inherit the trust.
	# Registering only the wrong one fails at the login step with AADSTS700213, reporting a subject that looks
	# almost identical to the one already there.
	SUBJECTS=("repo:${GITHUB_REPOSITORY}:ref:refs/heads/main" "repo:${GITHUB_REPOSITORY}:environment:develop")

	if command -v gh >/dev/null && gh auth status >/dev/null 2>&1; then
		OWNER_ID="$(gh api "repos/$GITHUB_REPOSITORY" --jq .owner.id | tr -d '')"
		REPO_ID="$(gh api "repos/$GITHUB_REPOSITORY" --jq .id | tr -d '')"
		if [[ -n "$OWNER_ID" && -n "$REPO_ID" ]]; then
			OWNER_NAME="${GITHUB_REPOSITORY%%/*}"
			REPO_NAME="${GITHUB_REPOSITORY##*/}"
			IMMUTABLE="repo:${OWNER_NAME}@${OWNER_ID}/${REPO_NAME}@${REPO_ID}"
			SUBJECTS+=("${IMMUTABLE}:ref:refs/heads/main" "${IMMUTABLE}:environment:develop")
		fi
	else
		warn "gh unavailable, so only the plain subject form is registered."
		warn "If the deploy fails with AADSTS700213, add a credential for the subject it reports."
	fi

	for subject in "${SUBJECTS[@]}"; do
		count="$(azv ad app federated-credential list --id "$APP_ID" --query "[?subject=='$subject'] | length(@)" -o tsv)"
		if [[ "$count" == "0" ]]; then
			credential_file="$(mktemp)"
			cat > "$credential_file" <<JSON
{
  "name": "$(echo "$subject" | tr -c 'A-Za-z0-9' '-')",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "$subject",
  "audiences": ["api://AzureADTokenExchange"]
}
JSON
			az ad app federated-credential create --id "$APP_ID" --parameters "@$credential_file" --output none
			rm -f "$credential_file"
			ok "federated credential for $subject"
		else
			echo "  federated credential for $subject already exists"
		fi
	done

	# Contributor on this resource group only, plus on the shared registry. Contributor on a registry carries
	# the push and pull data actions, so AcrPush - which the ABAC condition forbids - is not needed.
	az role assignment create --assignee-object-id "$SP_ID" --assignee-principal-type ServicePrincipal \
		--role Contributor --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP" --output none || true
	az role assignment create --assignee-object-id "$SP_ID" --assignee-principal-type ServicePrincipal \
		--role Contributor --scope "$ACR_ID" --output none || true
	echo "  role assignments applied"

	if command -v gh >/dev/null && gh auth status >/dev/null 2>&1; then
		echo "  configuring the repository with gh"
		# The deploy job runs in a GitHub environment named "develop", and one of the credentials above is
		# scoped to exactly that. Without the environment the token subject never matches, and the login step
		# fails with "no matching federated identity record found".
		gh api -X PUT "repos/$GITHUB_REPOSITORY/environments/develop" --silent && ok "environment 'develop' ready"
		gh secret set AZURE_CLIENT_ID       --repo "$GITHUB_REPOSITORY" --body "$APP_ID"          && ok "AZURE_CLIENT_ID set"
		gh secret set AZURE_TENANT_ID       --repo "$GITHUB_REPOSITORY" --body "$TENANT_ID"       && ok "AZURE_TENANT_ID set"
		gh secret set AZURE_SUBSCRIPTION_ID --repo "$GITHUB_REPOSITORY" --body "$SUBSCRIPTION_ID" && ok "AZURE_SUBSCRIPTION_ID set"
	else
		warn "gh is not installed or not authenticated, so the repository was not configured."
		echo "  Create an environment named 'develop' and add these repository secrets on $GITHUB_REPOSITORY:"
		echo "    AZURE_CLIENT_ID        $APP_ID"
		echo "    AZURE_TENANT_ID        $TENANT_ID"
		echo "    AZURE_SUBSCRIPTION_ID  $SUBSCRIPTION_ID"
	fi
fi

# ------------------------------------------------------------------------------------------ first deploy

if [[ "$SKIP_FIRST_DEPLOY" != "true" ]]; then
	step "First image"
	# Built in the registry rather than locally, so this works from a machine without Docker.
	TAG="$(git -C "$ROOT" rev-parse --short HEAD | tr -d '\r')"
	IMAGE="$ACR_LOGIN_SERVER/kompaz-web-backend:$TAG"
	az acr build --registry "$ACR_NAME" --image "kompaz-web-backend:$TAG" \
		--file src/Presentation/Dockerfile "$ROOT" || die "Image build failed."
	echo "  built $IMAGE"

	step "Application"
	APP="$(az deployment group create \
		--resource-group "$RESOURCE_GROUP" \
		--name kompaz-app \
		--template-file "$ROOT/infra/app.bicep" \
		--parameters image="$IMAGE" identityResourceId="$IDENTITY_RESOURCE_ID" keyVaultUri="$KEY_VAULT_URI" \
		--query properties.outputs -o json)" || die "Application deployment failed."

	API_URL="$(printf '%s' "$APP" | python -c 'import json,sys; print(json.load(sys.stdin)["containerAppUrl"]["value"])')"
	WEB_HOST="$(printf '%s' "$APP" | python -c 'import json,sys; print(json.load(sys.stdin)["staticWebAppHostname"]["value"])')"

	echo ""
	ok "API        $API_URL"
	ok "frontend   https://$WEB_HOST"
	echo ""
	warn "Point the igne-proxy clusters at those two addresses. See docs/deployment.md."
fi

step "Done"
