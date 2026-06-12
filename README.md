# SummarizeApi

A small ASP.NET Core Web API (.NET 10 LTS) that summarizes text using **Azure OpenAI** (`gpt-4o-mini`), designed to run on **Azure App Service** with a system-assigned managed identity. No OpenAI API keys anywhere — authentication to Azure OpenAI uses `DefaultAzureCredential`.

## Project structure

```
SummarizeApi.slnx                  Solution (XML-based .slnx format)
src/SummarizeApi/
  Program.cs                       Composition root: DI, options, pipeline (kept minimal)
  Endpoints/SummarizeEndpoints.cs  Thin HTTP layer: binding, validation, status codes
  Services/                        ISummarizationService + SummarizationService
                                   (business logic, prompt construction, model params)
  Infrastructure/                  IOpenAIClientWrapper + OpenAIClientWrapper
                                   (Azure.AI.OpenAI SDK behind a testable interface),
                                   UpstreamServiceException
  Middleware/                      ApiKeyMiddleware (X-Api-Key auth),
                                   GlobalExceptionHandler (ProblemDetails for 500/502)
  Models/                          Request/response DTOs
  Options/                         AzureOpenAIOptions, ApiKeyOptions (options pattern)
  Validation/                      SummarizeRequestValidator (FluentValidation)
  OpenApi/                         Swagger registration with X-Api-Key security scheme
tests/SummarizeApi.Tests/          xUnit + NSubstitute unit tests
infra/main.bicep                   App Service + Azure OpenAI + App Insights + RBAC
infra/main.parameters.json         Deployment parameters
```

## API contract

### `POST /summarize` (requires header `X-Api-Key: <key>`)

Request:

```json
{
  "text": "string, required, ≤ 50,000 chars",
  "maxWords": 100
}
```

| Status | Meaning |
|--------|---------|
| 200 | `{ "summary": "...", "originalLength": 1234, "summaryLength": 321 }` |
| 400 | Validation error (empty text, text > 50,000 chars, maxWords outside 10–500) — RFC 7807 ProblemDetails |
| 401 | Missing/wrong `X-Api-Key` |
| 502 | Azure OpenAI failure after SDK retries (no upstream internals leaked) |
| 500 | Unhandled error via global exception handler |

### `GET /health` (anonymous)

Returns `200 { "status": "healthy" }`. Used as the App Service health-check path.

Swagger UI: `/swagger` (anonymous, with the `X-Api-Key` scheme documented — use the **Authorize** button).

## Run locally

Prerequisites: .NET 10 SDK, Azure CLI, access to an Azure OpenAI account with a `gpt-4o-mini` deployment, and the **Cognitive Services OpenAI User** role on that account for your user.

```powershell
# 1. Sign in — DefaultAzureCredential picks this up locally
az login

# 2. Configure secrets (never in appsettings.json)
cd src/SummarizeApi
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://<your-account>.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:DeploymentName" "gpt-4o-mini"
dotnet user-secrets set "ApiKey:Key" "local-dev-key-0123456789"

# 3. Run
dotnet run
# Swagger: http://localhost:5210/swagger
```

## Run tests

```powershell
dotnet test SummarizeApi.slnx
```

## Deploy to Azure

```powershell
# 1. Resource group
az group create --name rg-summarizeapi --location swedencentral

# 2. Infrastructure (pass the API key as a secure parameter — do not commit it)
az deployment group create `
  --resource-group rg-summarizeapi `
  --template-file infra/main.bicep `
  --parameters apiKey="<your-strong-api-key>"

# Grab the web app name from the deployment outputs:
az deployment group show -g rg-summarizeapi -n main --query properties.outputs

# 3. Publish and zip-deploy
dotnet publish src/SummarizeApi -c Release -o publish
Compress-Archive -Path publish/* -DestinationPath app.zip -Force
az webapp deploy `
  --resource-group rg-summarizeapi `
  --name <webAppName-from-outputs> `
  --src-path app.zip `
  --type zip
```

The Bicep template provisions: Linux B1 App Service plan, the web app (.NET 10, system-assigned identity, `/health` health check), an Azure OpenAI account (**local auth disabled** — Entra ID only) with a `gpt-4o-mini` deployment, the `Cognitive Services OpenAI User` role assignment for the web app identity, and Application Insights backed by Log Analytics. App settings (`AzureOpenAI__Endpoint`, `AzureOpenAI__DeploymentName`, `ApiKey__Key`, `APPLICATIONINSIGHTS_CONNECTION_STRING`) are wired automatically.

## CI/CD with GitHub Actions

[`.github/workflows/ci-cd.yml`](.github/workflows/ci-cd.yml):

- **Pull requests / pushes to `main`:** restore → build → test (results uploaded as artifact) → Bicep compile check → publish artifact.
- **Pushes to `main` only:** Bicep deployment into the resource group → zip deploy of the published app → `/health` smoke test with retries.

Authentication uses **OIDC federated credentials** — GitHub exchanges its workflow token for an Azure token at run time; no client secret is stored.

### One-time setup

```powershell
# 1. Resource group (the pipeline deploys into it; it does not create it)
az group create --name rg-summarizeapi --location swedencentral

# 2. App registration + service principal for GitHub
az ad app create --display-name "github-summarizeapi"   # note the appId
az ad sp create --id <appId>

# 3. Federated credential trusting your repo's main branch
az ad app federated-credential create --id <appId> --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<owner>/<repo>:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

# 4. RBAC on the resource group. Owner because the Bicep template creates a
#    role assignment (web app -> OpenAI account); plain Contributor cannot do
#    that. Alternative: Contributor + "Role Based Access Control Administrator".
az role assignment create --assignee <appId> --role Owner `
  --scope /subscriptions/<subscriptionId>/resourceGroups/rg-summarizeapi
```

### GitHub repository secrets

| Secret | Value |
|--------|-------|
| `AZURE_CLIENT_ID` | `appId` from step 2 |
| `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| `AZURE_SUBSCRIPTION_ID` | `az account show --query id -o tsv` |
| `SUMMARIZE_API_KEY` | the X-Api-Key value (≥ 16 chars) |

The workflow assumes the repository root is this folder (where `SummarizeApi.slnx` lives). The resource group name is set in the workflow's `env:` block (`rg-summarizeapi`) — change it there if yours differs.

## Example request

```bash
curl -s -X POST "https://<webAppName>.azurewebsites.net/summarize" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: <your-strong-api-key>" \
  -d '{
    "text": "Azure App Service is a fully managed platform for building, deploying and scaling web apps. It supports .NET, Java, Node.js, Python and PHP, offers built-in CI/CD integration, autoscaling, and TLS termination, and integrates with Azure Monitor for observability.",
    "maxWords": 30
  }'
```

Response:

```json
{
  "summary": "Azure App Service is a fully managed platform for building, deploying and scaling web apps in .NET, Java, Node.js, Python and PHP, with CI/CD, autoscaling, TLS, and Azure Monitor integration.",
  "originalLength": 271,
  "summaryLength": 191
}
```

## Design decisions

### Prompt design

- **System prompt as a rule list.** The model is pinned to: summarize only the provided text, no added information or speculation (anti-hallucination); preserve named entities, dates, and numbers verbatim; hard word budget; single-paragraph output; reply in the input's language; output the summary only.
- **Prompt-injection containment.** The document is wrapped in a `<document>` fence inside the *user* message, and the system prompt explicitly says to treat anything inside it as content, not instructions. Instructions and data never share a channel.
- **Temperature 0** (greedy decoding) — same document gives the same summary on every call, with no creative drift.
- **Max output tokens derived from `maxWords`:** prose averages ~1.3–1.5 tokens per word, so the budget is `maxWords * 2 + 32` (margin for punctuation/bullet markers), clamped at 1,500. The model is never cut off mid-sentence, but also can't ramble far past the requested length.

### Retry strategy

The `Azure.AI.OpenAI` SDK's `System.ClientModel` pipeline retries transient failures (408, 429, 500, 502, 503, 504) with **exponential backoff** and honors `Retry-After` headers; the retry count is raised to 4 in `Program.cs`. Once retries are exhausted, the wrapper converts the SDK exception into `UpstreamServiceException`, which the global exception handler maps to **502** with a generic ProblemDetails body — upstream status codes, messages, and stack traces are logged (Application Insights) but never returned to callers.

### Input length guard

Requests over 50,000 characters are rejected with 400 — the text must fit a single model call.
