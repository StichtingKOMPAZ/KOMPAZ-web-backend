// Stage one of two.
//
// Everything here has to exist before a secret can be written, and every secret has to exist before the container
// app that references it can start. Splitting the deployment at that seam is what keeps the app template free of
// secret values: it names vault URIs, never credentials, so a redeploy is idempotent and nothing sensitive passes
// through the deployment history.
//
//   az deployment group create -g Kompaz -f infra/foundation.bicep -p deployerPrincipalId=...
//
// Note the vault uses access policies rather than RBAC. That is not a style preference. Owner on this subscription
// carries an ABAC condition permitting only Reader and Contributor to be assigned, so `Key Vault Secrets User`
// cannot be granted here by anyone who would realistically run this. Access policies are properties of the vault
// resource, need no role assignment, and reach the same place. It is also why no other project in this
// subscription uses a key vault at all.

targetScope = 'resourceGroup'

@description('Region for resources created here. The container apps environment and the reused PostgreSQL server are both in West Europe, so this should match.')
param location string = resourceGroup().location

@description('Name of the user-assigned identity the container app runs as.')
param identityName string = 'id-kompaz-develop'

@description('Name of the key vault holding the signing key, database connection string, registry password and SMTP password.')
param keyVaultName string = 'kv-kompaz-develop'

@description('Object id of the human or service principal that runs the bootstrap script, so it can write the secrets. Empty grants nobody but the application.')
param deployerPrincipalId string = ''

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: false

    // A vault holding the token signing key is worth being able to undelete. Purge protection is deliberately off:
    // it cannot be turned back off once on, and a develop environment gets torn down and rebuilt.
    enableSoftDelete: true
    softDeleteRetentionInDays: 7

    publicNetworkAccess: 'Enabled'

    accessPolicies: concat(
      [
        {
          // The application only ever reads. It resolves these at revision start.
          tenantId: subscription().tenantId
          objectId: identity.properties.principalId
          permissions: {
            secrets: ['get']
          }
        }
      ],
      empty(deployerPrincipalId) ? [] : [
        {
          tenantId: subscription().tenantId
          objectId: deployerPrincipalId
          permissions: {
            secrets: ['get', 'list', 'set', 'delete']
          }
        }
      ]
    )
  }
}

output identityResourceId string = identity.id
output identityPrincipalId string = identity.properties.principalId
output identityClientId string = identity.properties.clientId
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
