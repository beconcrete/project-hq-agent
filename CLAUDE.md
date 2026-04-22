# HQ Agent — Project Guide

## Overview

HQ Agent is the company headquarters platform. A modular Azure Static Web App with an expandable left-side navigation. Each module is an independent feature area. Contracts is the first module, covering contract ingestion, extraction, and chat.

## Repository Structure

```
/frontend                    # Azure Static Web App — Vue 3 + Vite
  /public                    # Static assets copied as-is to dist/ (staticwebapp.config.json, sample.html, images)
  /src                       # Vue app source
    /composables             # useAuth.js, useSidebar.js
    /components              # AppNav.vue, AppHeader.vue
    /pages                   # HomePage.vue, ContractsPage.vue, AuthTestPage.vue
    /router                  # Vue Router (index.js)
    /styles                  # app.css
/api                         # SWA-managed HTTP functions — served at /api/* (C#, isolated worker)
  /Middleware                # RequireAccessMiddleware (auth on every HTTP request)
/agents                      # Standalone Azure Function App — agent HTTP + queue triggers (C#, isolated worker)
  /Functions/Contract        # ContractIngestion, ContractChat, ContractOrchestratorAgent, ContractChatAgent
/shared                      # Shared C# library — referenced by api/ and agents/
  /HqAgent.Shared            # HqAgent.Shared.csproj — targets net8.0 for SWA compatibility
  /HqAgent.Shared.Tests      # Unit tests for the shared library
/infra                       # Infrastructure scripts and config
/docs                        # Architecture docs
/.github/workflows           # CI/CD pipelines
```

## Shared Library (`shared/HqAgent.Shared`)

`HqAgent.Shared` targets `net8.0` for compatibility with the SWA-managed `api/` project, because Azure Static Web Apps managed Functions do not support .NET 10. The standalone `agents/` Function App targets `net10.0` and references the shared `net8.0` library. Shared contains everything that crosses project boundaries.

### What lives in shared

| Namespace | Contents |
|---|---|
| `HqAgent.Shared.Models` | `ContractMessage` (queue message), `ExtractionResult` (open-ended), `ContractExtractionEntity` (Table entity), normalized contract fact helpers |
| `HqAgent.Shared.Storage` | `BlobStorageService` (download blobs), `TableStorageService` (write/read `Contracts`) |

### Rules

- **Never duplicate** `BlobStorageService`, `TableStorageService`, `ExtractionResult`, or `ContractMessage` in any project — always reference shared.
- `ExtractionResult` uses an open `Dictionary<string, JsonElement>` for extracted fields — no fixed schema. The model decides what fields are relevant.
- `Contracts` stores both the full open extraction JSON and normalized query fields such as expiry date, notice deadline, counterparties, people, renewal status, assignment dates, payment facts, review state, and risk flags.
- `TableStorageService.WriteExtractionAsync` automatically sets `status = "pending_review"` and `ReviewState = "pending_review"` when `ExtractionResult.PendingReview = true`.

### CI/CD — shared triggers all three pipelines

A change to `shared/**` triggers **all** deployment workflows:

| Workflow | Deploys | Triggered by |
|---|---|---|
| `deploy-frontend.yml` | `api/` + frontend | `frontend/**`, `api/**`, `shared/**` |
| `deploy-agent.yml` | `agents/` standalone Function App | `agents/**`, `shared/**` |

## Azure Resources

See [azure-resources.md](./azure-resources.md) for all Azure resource names, the resource group, subscription, and portal dashboard link for this project.

## Architectural Decision Rules

**Never make architectural decisions unilaterally.** Always ask before:
- Creating new Azure resources (Function Apps, Storage accounts, plans, etc.)
- Changing project SDK types (e.g. Web → Functions Worker)
- Deciding where code is hosted (which Function App, which service)
- Adding new deployment pipelines or workflows
- Changing how services communicate

If a task requires an architectural decision to proceed, stop and ask. Do not pick an approach and implement it.

---

## Architecture Decisions

- **Frontend**: Azure Static Web App (CDN-distributed, free tier) — Vue 3 + Vite, built to `dist/`
- **API (`/api/`)**: SWA-managed HTTP functions, C# isolated worker, currently `net8.0`. Deployed by the SWA workflow (`api_location: api`). Served at `/api/*`. HTTP triggers only — no blob/queue triggers here.
- **Agents Function App (`/agents/`)**: Separate Azure Function App (`hq-agent-function-app`, Consumption plan), currently `net10.0` isolated worker. Background and agent-driven work only. Never called directly by the browser.
- **No containers, no App Service, no Container Registry, no Dapr** — all agent logic runs as Azure Functions.
- **Storage**: Azure Blob (contract files), Queue (work queue), Table (`Contracts`, `ContractChatHistory`)
- **Status updates**: the frontend polls status for uploaded contracts.
- **Queue**: Azure Queue Storage (NOT Service Bus)
- **NO**: Containers, App Service, Container Registry, Dapr, Kubernetes, Logic Apps, Service Bus, SignalR

### What goes where: api/ vs agents/

| Belongs in `api/` | Belongs in `agents/` |
|---|---|
| Anything the browser calls (`/api/*`) | Queue triggers (e.g. contract processing) |
| Auth helpers (`GetConfig`, session endpoints) | Internal agent HTTP endpoints called by SWA `/api` |
| Data read/write endpoints for the UI | Timer triggers |
| File upload endpoints | Anything that runs in the background without a browser request |

**Rule of thumb**: if the frontend JavaScript calls it, it goes in `api/`. If it processes queue work or hosts internal agents, it goes in `agents/`. Never put queue triggers in SWA-managed `api/`, and never expose internal agent endpoints directly to the browser.

## AI Model Usage

| Step | Model | Reason |
|---|---|---|
| PDF text extraction | `gpt-4.1-mini` | Cheap, fast, supports OpenAI file content |
| Triage / classification | `gpt-4.1-mini` | Cheap, fast |
| Contract extraction | `gpt-4.1` | Better extraction accuracy |
| Contract chat Q&A | `gpt-4.1-mini` | Fast interactive chat with tools |

MAF handoff workflows use OpenAI only. Anthropic rejects the assistant-message prefill pattern that MAF handoff uses.

## App Settings

These must be configured in two places: the Azure SWA portal (Settings → Configuration → Application settings) and `api/local.settings.json` for local development.

| Setting | Description |
|---|---|
| `APP_ID` | Be Concrete ID app identifier — `hqagents` |
| `STORAGE_CONNECTION_STRING` | Connection string for `hqagentstorage` (Azure portal → Storage account → Access keys) |

`AUTH0_DOMAIN` and `AUTH0_CLIENT_ID` are GitHub secrets only — injected at build time, not runtime app settings.

## Azure Function API

- Custom auth tokens go in `X-Auth-Token` header (NOT `Authorization` — SWA intercepts that)
- Admin-facing functions must be named `management-*` (NOT `admin-*` — reserved by SWA)
- `WriteAsJsonAsync` resets the HTTP status to 200. Always set `res.StatusCode` **after** calling it:
  ```csharp
  var res = req.CreateResponse();
  await res.WriteAsJsonAsync(body);
  res.StatusCode = HttpStatusCode.Forbidden; // set last
  return res;
  ```

### Role-based authorization

`RequireAccessMiddleware` gates app access (is the user in `hqagents`?). For feature-level role checks inside a function, use the `RoleGuard` helper — do not inline your own roles fetch.

Roles are fetched **fresh on every API call** — no caching. Role changes take effect immediately.

```csharp
var guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.Admin);
if (!guard.Allowed)
    return req.CreateResponse(HttpStatusCode.Forbidden);
```

Use the `Roles` constants (`Roles.Admin`, `Roles.User`) — no magic strings.

| Role | Rule |
|---|---|
| `admin` | Admin only |
| `user` | User or admin |

## Authorization

This app uses Auth0 for sign-in and Be Concrete ID (`https://id.beconcrete.se`) for access control.
App ID is `hqagents`. A user must have `hqagents` in their Be Concrete ID `apps[]` to access the app.
The `admin` role grants admin-level privileges within the app but is separate from basic access.

### Auth flow

1. Frontend calls `Auth0.loginWithRedirect()` → Auth0 login page
2. Auth0 redirects back with `?code=` → `handleRedirectCallback()` exchanges it for tokens (ID token kept in memory only)
3. Frontend calls `GET /api/me` with the ID token in `X-Auth-Token`
4. `RequireAccessMiddleware` forwards the token to Be Concrete ID server-to-server, checks the user has the `hqagents` app
5. `GetMe` (`api/GetMe.cs`) returns `{ userId, apps }` — frontend grants access

The frontend never calls Be Concrete ID directly. Routing through `/api/me` avoids browser CORS restrictions.

If the user is authenticated but lacks the role, the auth gate shows a "Sign out" button instead of "Sign in" so they can switch accounts.

### API auth middleware

All Azure Functions validate the token via `RequireAccessMiddleware` (registered in `Program.cs`).
It reads `X-Auth-Token`, calls Be Concrete ID, and blocks with 403 if the user is missing or lacks the app.
`context.Items["userId"]` is set for downstream functions to use.

### Auth0 config

`AUTH0_DOMAIN` and `AUTH0_CLIENT_ID` are **GitHub secrets only**. The deploy workflow passes them as `VITE_AUTH0_DOMAIN` and `VITE_AUTH0_CLIENT_ID` environment variables during `npm run build` — Vite bakes them into the bundle at build time. They are not needed as Azure SWA application settings and must not be added there.

### CSP

The `staticwebapp.config.json` CSP includes `worker-src blob:` to allow Auth0's SDK to spawn its token-cache web worker.

## Microsoft Agents Framework (MAF)

See [docs/MAF.md](./docs/MAF.md) for patterns, gotchas, and working examples covering:
- Use `AgentWorkflowBuilder.CreateHandoffBuilderWith` — not `HandoffWorkflowBuilder` (deprecated, `MAAIW001`)
- Execute with `InProcessExecution.OpenStreamingAsync` — do not call `.RunAsync()` on a wrapped agent
- Shared `ChatHistoryProvider` on all agents in a workflow so they see each other's messages
- Tools: `[Description]` attribute + `AIFunctionFactory.Create(method)`
- What to persist vs skip in `StoreChatHistoryAsync` (never persist tool call/result pairs)

### Current Contract agent shape

- `ContractOrchestratorAgent` uses MAF for queue-triggered ingestion: triage → extraction.
- `ContractChatAgent` intentionally uses a direct OpenAI tool-calling loop for interactive chat, but its tools delegate to `IContractIntelligence` instead of owning storage queries directly.
- `IContractIntelligence` is the internal capability layer future agents should call for contract questions such as expiring contracts, renewal windows, people/person impact, counterparty lookup, and document fallback.
- `DocumentTextExtractor` is shared by ingestion and chat document fallback. It extracts PDF text through OpenAI file input and DOCX text locally from `word/document.xml`.
- See [docs/contract-capabilities.md](./docs/contract-capabilities.md) for the durable fact model, review states, and cross-domain capability pattern.
- See [docs/contracts-eval.md](./docs/contracts-eval.md) for the manual ingestion/chat evaluation checklist.
- See [docs/operations.md](./docs/operations.md) for production health checks, Application Insights queries, and safe maintenance scripts.

## GitHub Workflows

All workflow files include `concurrency` to prevent race conditions:
```yaml
concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true
```
