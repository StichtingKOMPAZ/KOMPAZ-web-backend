// Stage two of two. Requires infra/foundation.bicep and the secrets it guards to exist already.
//
//   az deployment group create -g Kompaz -f infra/app.bicep \
//     -p image=... identityResourceId=... keyVaultUri=...
//
// Resource names are defaults on the parameters below, so only the three values that change between deployments
// have to be supplied.
//
// This template names secrets by vault URI and never carries a value, so it is safe to redeploy from CI on every
// push. The workflow does exactly that, passing a new `image`.

targetScope = 'resourceGroup'

param location string = resourceGroup().location

@description('Container apps environment. Free to run: a consumption-only environment has no standing charge, only the apps inside it.')
param environmentName string = 'cae-kompaz'

@description('Log Analytics workspace for container stdout and system logs.')
param logAnalyticsName string = 'log-kompaz'

@description('The container app serving the API.')
param containerAppName string = 'ca-kompaz-api-develop'

@description('Fully qualified image reference, including the tag. CI passes an immutable commit SHA so a rollback is a redeploy of the previous tag.')
param image string

@description('Registry the image is pulled from, e.g. acrphase2.azurecr.io.')
param acrLoginServer string = 'acrphase2.azurecr.io'

@description('Registry admin username. Equal to the registry name for an Azure Container Registry.')
param acrUsername string = 'acrphase2'

@description('Resource id of the user-assigned identity from the foundation deployment. Reads the vault secrets, including the registry password.')
param identityResourceId string

@description('Vault URI from the foundation deployment, with trailing slash.')
param keyVaultUri string

@description('ASPNETCORE_ENVIRONMENT. Deliberately not "Development": that name would load the committed throwaway signing key and turn on the migrations endpoint.')
param aspNetCoreEnvironment string = 'Develop'

@description('Replicas to keep warm. One replica keeps Database:MigrateOnStartup safe, since concurrent migrations would race. Zero costs almost nothing but adds a cold start that includes the migration check.')
@minValue(0)
param minReplicas int = 1

@description('Maximum replicas. Above one, turn Database:MigrateOnStartup off and apply migrations as a separate step.')
@minValue(1)
param maxReplicas int = 1

@description('Name of the static web app serving the frontend. Empty skips it.')
param staticWebAppName string = 'swa-kompaz-develop'

var acrPasswordSecretName = 'acr-password'
var dbSecretName = 'kompazdb-connection-string'
var signingKeySecretName = 'authentication-signing-key'
var smtpUserNameSecretName = 'smtp-username'
var smtpPasswordSecretName = 'smtp-password'
var storageSecretName = 'storage-connection-string'

// The aspnet:10.0 base image listens here by default; the Dockerfile does not override it.
var containerPort = 8080

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        // Consumption only. A workload-profiles environment with a dedicated profile carries a management charge of
        // roughly EUR 90/month, which is more than everything else here put together.
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityResourceId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        // Public, but only ever reached through igne-proxy in practice. The app trusts any proxy's forwarded
        // headers, so anyone hitting this FQDN directly can claim a fresh client address and dodge the rate
        // limiter. Acceptable for develop; tighten with ipSecurityRestrictions before this pattern goes further.
        external: true
        targetPort: containerPort
        transport: 'auto'

        // Envoy redirects http to https at the edge. The app's own UseHttpsRedirection is a no-op inside the
        // container, which never learns an https port, so this is where the redirect actually happens.
        allowInsecure: false
      }
      registries: [
        {
          // Admin credentials rather than the managed identity, because granting AcrPull needs a role assignment
          // that this subscription's constrained Owner condition forbids. The password is still a vault secret,
          // resolved by the identity below, so nothing is stored here in the clear.
          server: acrLoginServer
          username: acrUsername
          passwordSecretRef: acrPasswordSecretName
        }
      ]
      secrets: [
        {
          name: acrPasswordSecretName
          keyVaultUrl: '${keyVaultUri}secrets/${acrPasswordSecretName}'
          identity: identityResourceId
        }
        {
          name: dbSecretName
          keyVaultUrl: '${keyVaultUri}secrets/${dbSecretName}'
          identity: identityResourceId
        }
        {
          name: signingKeySecretName
          keyVaultUrl: '${keyVaultUri}secrets/${signingKeySecretName}'
          identity: identityResourceId
        }
        {
          name: smtpUserNameSecretName
          keyVaultUrl: '${keyVaultUri}secrets/${smtpUserNameSecretName}'
          identity: identityResourceId
        }
        {
          name: smtpPasswordSecretName
          keyVaultUrl: '${keyVaultUri}secrets/${smtpPasswordSecretName}'
          identity: identityResourceId
        }
        {
          name: storageSecretName
          keyVaultUrl: '${keyVaultUri}secrets/${storageSecretName}'
          identity: identityResourceId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: aspNetCoreEnvironment
            }
            {
              name: 'ConnectionStrings__KompazDb'
              secretRef: dbSecretName
            }
            {
              name: 'Authentication__SigningKey'
              secretRef: signingKeySecretName
            }
            {
              name: 'Email__Smtp__UserName'
              secretRef: smtpUserNameSecretName
            }
            {
              name: 'Email__Smtp__Password'
              secretRef: smtpPasswordSecretName
            }
            {
              // Without this the application refuses to start outside Development, rather than keeping uploaded
              // files on a filesystem the next revision throws away.
              name: 'Storage__ConnectionString'
              secretRef: storageSecretName
            }
          ]
          probes: [
            {
              // Generous, because EF migrations run before the first request is served and a develop database on a
              // burstable server is not fast. Thirty attempts ten seconds apart is five minutes to come up.
              type: 'Startup'
              httpGet: {
                path: '/health'
                port: containerPort
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              failureThreshold: 30
            }
            {
              // /health includes a DbContext check, so an instance that has lost the database stops taking traffic
              // rather than answering every request with a 500.
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: containerPort
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              failureThreshold: 3
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: containerPort
              }
              initialDelaySeconds: 10
              periodSeconds: 30
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = if (!empty(staticWebAppName)) {
  name: staticWebAppName
  location: location
  sku: {
    // Free covers 100 GB of bandwidth a month and issues its own certificates. The frontend is reached through
    // igne-proxy rather than directly, which is what makes it portable: moving it elsewhere later is a one-line
    // change to the proxy's cluster address, with no DNS, certificate, CORS or token-audience consequences.
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    // Deployed with a token from the frontend repository's own pipeline, not linked to a repository here.
    allowConfigFileUpdates: true
  }
}

output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output containerAppUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output staticWebAppHostname string = empty(staticWebAppName) ? '' : staticWebApp!.properties.defaultHostname
