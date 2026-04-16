# HQ Agent — Project Guide

## Overview

HQ Agent is the company headquarters platform. A modular Azure Static Web App with an expandable left-side navigation. Each module is an independent feature area. Contracts is the first module, covering contract ingestion, extraction, and chat.

## Repository Structure

```
/frontend                    # Azure Static Web App (HTML/CSS/JS)
  /public                    # Static assets
  /src                       # App source (components, pages, styles)
/api                         # SWA-managed HTTP functions — served at /api/* (C#, isolated worker)
  /Middleware                # RequireAccessMiddleware (auth on every HTTP request)
/functions                   # Separate Azure Function App — background/event-driven only (C#, isolated worker)
  /HqAgentFunctions          # Blob triggers, queue triggers — NOT browser-facing
/agents
  /contract-orchestrator-agent   # C# .NET agent: ingestion + extraction
  /contract-chat-agent           # C# .NET agent: contract Q&A
/infra                       # Infrastructure scripts and config
/docs                        # Architecture docs
/.github/workflows           # CI/CD pipelines
```

## Azure Resources

See [azure-resources.md](./azure-resources.md) for all Azure resource names, the resource group, subscription, and portal dashboard link for this project.

## Architecture Decisions

- **Frontend**: Azure Static Web App (CDN-distributed, free tier)
- **API (`/api/`)**: SWA-managed HTTP functions, C# isolated worker. Deployed by the SWA workflow (`api_location: api`). Served at `/api/*`. HTTP triggers only — no blob/queue triggers here.
- **Functions (`/functions/`)**: Separate Azure Function App (`hq-agent-function-app`). Background and event-driven work only — blob triggers, queue triggers. Never called directly by the browser.
- **Agents**: Containerized .NET services on Azure App Service with Dapr sidecar
- **Storage**: Azure Blob (contract files), Queue (work queue), Table (extracted data)
- **Real-time**: WebSockets (NOT SignalR, NOT polling)
- **Queue**: Azure Queue Storage (NOT Service Bus)
- **NO**: Kubernetes, Logic Apps, Service Bus, SignalR

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

`AUTH0_DOMAIN` and `AUTH0_CLIENT_ID` are **GitHub secrets only**. The deploy workflow injects them into `frontend/src/auth.js` at build time via `envsubst` — the values are baked into the static JS before the files are uploaded to SWA. They are not needed as Azure SWA application settings and must not be added there.

### CSP

The `staticwebapp.config.json` CSP includes `worker-src blob:` to allow Auth0's SDK to spawn its token-cache web worker.

## GitHub Workflows

All workflow files include `concurrency` to prevent race conditions:
```yaml
concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true
```
