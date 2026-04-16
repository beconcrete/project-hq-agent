# HQ Agent — Project Guide

## Overview

HQ Agent is the company headquarters platform. A modular Azure Static Web App with an expandable left-side navigation. Each module is an independent feature area. Contracts is the first module, covering contract ingestion, extraction, and chat.

## Repository Structure

```
/frontend                    # Azure Static Web App (HTML/CSS/JS)
  /public                    # Static assets
  /src                       # App source (components, pages, styles)
/functions                   # Azure Functions (C#, isolated worker model)
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
- **API Functions**: Azure Functions (C#, isolated worker) hosted at `/api` on the SWA
- **Agents**: Containerized .NET services on Azure App Service with Dapr sidecar
- **Storage**: Azure Blob (contract files), Queue (work queue), Table (extracted data)
- **Real-time**: WebSockets (NOT SignalR, NOT polling)
- **Queue**: Azure Queue Storage (NOT Service Bus)
- **NO**: Kubernetes, Logic Apps, Service Bus, SignalR

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

## Authorization

This app uses Auth0 for sign-in and usermgmt (`https://usermanagement.beconcrete.se`) for access control.
App ID is `hqagents`. Only users with the `admin` role may access the app.

After login, the frontend calls `GET /api/v1/me` on usermgmt with the Auth0 ID token:

```js
const res = await fetch("https://usermanagement.beconcrete.se/api/v1/me", {
  headers: { "X-Auth-Token": `Bearer ${idToken}` },
});
const { apps } = await res.json();
if (!apps.includes("hqagents")) // deny access
```

All Azure Functions validate the same token via `RequireAccessMiddleware` (registered in `Program.cs`).
The public exception is `GetConfig` (`/api/config`), which returns Auth0 domain + client ID to the SPA before login.

Auth0 config is stored as Azure SWA application settings: `AUTH0_DOMAIN`, `AUTH0_CLIENT_ID`, `APP_ID`.

## GitHub Workflows

All workflow files include `concurrency` to prevent race conditions:
```yaml
concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true
```
