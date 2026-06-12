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
tests/SummarizeApi.Tests/          xUnit v3 + NSubstitute unit tests
infra/main.bicep                   App Service + Azure OpenAI + App Insights + RBAC
.github/workflows/ci-cd.yml        Build/test/deploy pipeline (OIDC to Azure)
```

All Bicep parameters have defaults except the `@secure()` API key, which is passed on the command line / pipeline — there is no parameters file.

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
# 1. Resource group (skip if it already exists)
az group create --name <resource-group> --location swedencentral

# 2. Infrastructure (pass the API key as a secure parameter — do not commit it)
az deployment group create `
  --resource-group <resource-group> `
  --template-file infra/main.bicep `
  --parameters apiKey="<your-strong-api-key>"

# Grab the web app name from the deployment outputs:
az deployment group show -g <resource-group> -n main --query properties.outputs

# 3. Publish and zip-deploy
dotnet publish src/SummarizeApi -c Release -o publish
Compress-Archive -Path publish/* -DestinationPath app.zip -Force
az webapp deploy `
  --resource-group <resource-group> `
  --name app-summarizeapi-dev `
  --src-path app.zip `
  --type zip
```

The Bicep template provisions: Linux B1 App Service plan, the web app (.NET 10, system-assigned identity, `/health` health check), an Azure OpenAI account (**local auth disabled** — Entra ID only) with a `gpt-4o-mini` deployment (GlobalStandard, 20K TPM), the `Cognitive Services OpenAI User` role assignment for the web app identity, and Application Insights backed by Log Analytics. App settings (`AzureOpenAI__Endpoint`, `AzureOpenAI__DeploymentName`, `ApiKey__Key`, `APPLICATIONINSIGHTS_CONNECTION_STRING`) are wired automatically.

Resource names are deterministic — `<type>-summarizeapi-dev` (e.g. web app `app-summarizeapi-dev`, OpenAI account `oai-summarizeapi-dev`) — so repeat deployments update the same resources in place (ARM incremental mode; nothing outside the template is touched).

## CI/CD with GitHub Actions

[`.github/workflows/ci-cd.yml`](.github/workflows/ci-cd.yml):

- **Pull requests / pushes to `main`:** restore → build → test (results uploaded as artifact) → Bicep compile check → publish artifact.
- **Pushes to `main` that changed `src/`:** zip deploy of the published app to the existing web app → `/health` smoke test with retries. Docs/infra-only pushes build and test but skip the deploy; a manual `workflow_dispatch` run always deploys.

The pipeline ships **application code only**. Infrastructure is provisioned manually with `az deployment group create` (see "Deploy to Azure" above) — infra changes are infrequent, deliberate operations, and keeping them out of the pipeline means the workflow identity needs only deploy rights on the web app instead of Owner on the resource group.

Authentication uses **OIDC federated credentials** — GitHub exchanges its workflow token for an Azure token at run time; no client secret is stored.

### One-time setup

```powershell
# 1. Provision the infrastructure manually first — see "Deploy to Azure" above.

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

# 4. RBAC: the pipeline only zip-deploys, so "Website Contributor" on the
#    resource group is enough (least privilege — no infra rights).
#    Without any role assignment azure/login fails with "No subscriptions found".
az role assignment create --assignee <appId> --role "Website Contributor" `
  --scope /subscriptions/<subscriptionId>/resourceGroups/<resource-group>
```

### GitHub repository secrets and variables

Secrets (Settings → Secrets and variables → Actions → **Secrets**):

| Secret | Value |
|--------|-------|
| `AZURE_CLIENT_ID` | `appId` from step 2 |
| `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| `AZURE_SUBSCRIPTION_ID` | `az account show --query id -o tsv` |

Variables (same page, **Variables** tab — non-sensitive config, kept out of the repo but visible in logs for debuggability):

| Variable | Value |
|----------|-------|
| `AZURE_RESOURCE_GROUP` | resource group the web app lives in |
| `AZURE_WEBAPP_NAME` | web app name, e.g. `app-summarizeapi-dev` |

Add these as **repository secrets** (Settings → Secrets and variables → Actions → Repository secrets). Secrets stored in a GitHub *Environment* are only visible to jobs that declare `environment: <name>` — the deploy job doesn't, so environment secrets resolve empty and `azure/login` fails with "client-id and tenant-id not supplied." (If you prefer environments, add `environment:` to the deploy job *and* create a second federated credential with subject `repo:<owner>/<repo>:environment:<name>`.)

The workflow assumes the repository root is this folder (where `SummarizeApi.slnx` lives). The deploy target (resource group + web app name) comes entirely from the repository variables above — nothing environment-specific lives in the workflow file.

## Example request

```bash
curl -s -X POST "https://app-summarizeapi-dev.azurewebsites.net/summarize" \
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
- **Max output tokens derived from `maxWords`:** prose averages ~1.3–1.5 tokens per word, so the budget is `maxWords * 2 + 32` (margin for punctuation), clamped at 1,500. The model is never cut off mid-sentence, but also can't ramble far past the requested length. Note `summaryLength` in the response is **characters**, not words.

### Retry strategy

The `Azure.AI.OpenAI` SDK's `System.ClientModel` pipeline retries transient failures (408, 429, 500, 502, 503, 504) with **exponential backoff** and honors `Retry-After` headers; the retry count is raised to 4 in `Program.cs`. Once retries are exhausted, the wrapper converts the SDK exception into `UpstreamServiceException`, which the global exception handler maps to **502** with a generic ProblemDetails body — upstream status codes, messages, and stack traces are logged (Application Insights) but never returned to callers.

### Input length guard

Requests over 50,000 characters are rejected with 400 — the text must fit a single model call.
