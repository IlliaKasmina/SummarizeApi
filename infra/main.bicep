// Provisions the complete runtime environment for SummarizeApi:
//   - Linux App Service plan (B1) + Web App (.NET 10, system-assigned identity)
//   - Azure OpenAI account (kind OpenAI) + gpt-4o-mini deployment
//   - Role assignment: web app identity -> "Cognitive Services OpenAI User"
//   - Log Analytics workspace + Application Insights
//   - App settings wiring (OpenAI endpoint/deployment, API key, App Insights)
//
// Local auth on the OpenAI account is disabled: the API authenticates with
// DefaultAzureCredential / managed identity only, never with account keys.

@description('Base name used to derive resource names. Lowercase letters and digits.')
@minLength(3)
@maxLength(17)
param baseName string = 'summarizeapi'

@description('Region for the App Service plan, web app, and monitoring resources.')
param location string = resourceGroup().location

@description('Region for the Azure OpenAI account (model availability varies by region).')
param openAILocation string = 'swedencentral'

@description('Chat model to deploy.')
param openAIModelName string = 'gpt-4o-mini'

@description('Model version for the deployment.')
param openAIModelVersion string = '2024-07-18'

@description('Deployment SKU for the model.')
@allowed(['Standard', 'GlobalStandard'])
param openAIDeploymentSku string = 'GlobalStandard'

@description('Throughput capacity (thousands of tokens per minute) for the model deployment.')
param openAIDeploymentCapacity int = 20

@description('API key clients must send in the X-Api-Key header. Min 16 characters.')
@secure()
@minLength(16)
param apiKey string

var suffix = 'dev'
var appServicePlanName = 'plan-${baseName}-${suffix}'
var webAppName = 'app-${baseName}-${suffix}'
var openAIAccountName = 'oai-${baseName}-${suffix}'
var logAnalyticsName = 'log-${baseName}-${suffix}'
var appInsightsName = 'appi-${baseName}-${suffix}'

// Built-in role: Cognitive Services OpenAI User
var openAIUserRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
)

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

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource openAIAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAIAccountName
  location: openAILocation
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: openAIAccountName
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true // Entra ID (managed identity) auth only — no account keys.
  }
}

resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAIAccount
  name: openAIModelName
  sku: {
    name: openAIDeploymentSku
    capacity: openAIDeploymentCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: openAIModelName
      version: openAIModelVersion
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true // required for Linux plans
  }
}

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      appSettings: [
        {
          name: 'AzureOpenAI__Endpoint'
          value: openAIAccount.properties.endpoint
        }
        {
          name: 'AzureOpenAI__DeploymentName'
          value: modelDeployment.name
        }
        {
          name: 'ApiKey__Key'
          value: apiKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
      ]
    }
  }
}

// Allow the web app's managed identity to call the OpenAI data plane.
resource openAIRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(openAIAccount.id, webApp.id, openAIUserRoleId)
  scope: openAIAccount
  properties: {
    roleDefinitionId: openAIUserRoleId
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output openAIEndpoint string = openAIAccount.properties.endpoint
output appInsightsName string = appInsights.name
