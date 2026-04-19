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
/functions                   # Separate Azure Function App — background/event-driven only (C#, isolated worker)
  /HqAgentFunctions          # Blob triggers, queue triggers, timer triggers — NOT browser-facing
/agents                      # Agent projects — each agent is its own project
  /contract-orchestrator-agent   # HTTP-triggered contract analysis (tool-use pipeline)
  /contract-chat-agent           # (in progress)
/shared                      # Shared C# library — referenced by api/, functions/, and agents/
  /HqAgent.Shared            # HqAgent.Shared.csproj — targets net8.0 for broad compatibility
  /HqAgent.Shared.Tests      # Unit tests for the shared library
/infra                       # Infrastructure scripts and config
/docs                        # Architecture docs
/.github/workflows           # CI/CD pipelines
```

## Shared Library (`shared/HqAgent.Shared`)

`HqAgent.Shared` is a `net8.0` class library referenced by `api/`, `functions/HqAgentFunctions`, `agents/contract-orchestrator-agent`, and `agents/contract-chat-agent`. It contains everything that crosses project boundaries.

### What lives in shared

| Namespace | Contents |
|---|---|
| `HqAgent.Shared.Models` | `ContractMessage` (queue message), `ExtractionResult` (open-ended), `ContractExtractionEntity` (Table entity) |
| `HqAgent.Shared.Storage` | `BlobStorageService` (download blobs), `TableStorageService` (write/read `ContractExtractions`) |
| `HqAgent.Shared.Abstractions` | `IAIModelClient` (interface for AI model calls; implemented by `AnthropicHttpClient` in agents) |

### Rules

- **Never duplicate** `BlobStorageService`, `TableStorageService`, `ExtractionResult`, or `ContractMessage` in any project — always reference shared.
- `IAIModelClient` is the injection point for AI calls in agents. DI registers: `AddHttpClient<IAIModelClient, AnthropicHttpClient>()`.
- `ExtractionResult` uses an open `Dictionary<string, JsonElement>` for extracted fields — no fixed schema. The model decides what fields are relevant.
- `TableStorageService.WriteExtractionAsync` automatically sets `status = "pending_review"` when `ExtractionResult.PendingReview = true`.

### CI/CD — shared triggers all three pipelines

A change to `shared/**` triggers **all** deployment workflows:

| Workflow | Deploys | Triggered by |
|---|---|---|
| `deploy-frontend.yml` | `api/` + frontend | `frontend/**`, `api/**`, `shared/**` |
| `deploy-functions.yml` | `functions/HqAgentFunctions` | `functions/**`, `shared/**` |
| `deploy-agent.yml` | `agents/contract-orchestrator-agent` | `agents/contract-orchestrator-agent/**`, `shared/**` |

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
- **API (`/api/`)**: SWA-managed HTTP functions, C# isolated worker. Deployed by the SWA workflow (`api_location: api`). Served at `/api/*`. HTTP triggers only — no blob/queue triggers here.
- **Functions (`/functions/`)**: Separate Azure Function App (`hq-agent-function-app`, Consumption plan). Background and event-driven work only — blob triggers, queue triggers, timer triggers. Never called directly by the browser.
- **No containers, no App Service, no Container Registry, no Dapr** — all agent logic runs as Azure Functions.
- **Storage**: Azure Blob (contract files), Queue (work queue), Table (extracted data + alerts)
- **Real-time**: WebSockets (NOT SignalR, NOT polling)
- **Queue**: Azure Queue Storage (NOT Service Bus)
- **NO**: Containers, App Service, Container Registry, Dapr, Kubernetes, Logic Apps, Service Bus, SignalR

### What goes where: api/ vs functions/

| Belongs in `api/` | Belongs in `functions/` |
|---|---|
| Anything the browser calls (`/api/*`) | Blob triggers (e.g. new contract uploaded) |
| Auth helpers (`GetConfig`, future session endpoints) | Queue triggers (e.g. contract processing) |
| Data read/write endpoints for the UI | Timer triggers |
| File upload endpoints | Anything that runs in the background without a browser request |

**Rule of thumb**: if the frontend JavaScript calls it, it goes in `api/`. If it reacts to a storage event or a queue message, it goes in `functions/`. Never put blob/queue triggers in `api/` and never put HTTP endpoints meant for the browser in `functions/`.

## AI Model Usage

| Step | Model | Reason |
|---|---|---|
| Triage / classification | Claude Haiku 4.5 | Fastest, cheapest |
| Contract extraction | Claude Sonnet 4.6 | Best default speed/quality |
| Contract chat Q&A | Claude Sonnet 4.6 | Quality matters |
| Escalation / second-pass | Claude Opus 4.6 | Fallback only |

Always use prompt caching for system prompts and extraction schemas. Use Message Batches API for bulk jobs.

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

## GitHub Workflows

All workflow files include `concurrency` to prevent race conditions:
```yaml
concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true
```
