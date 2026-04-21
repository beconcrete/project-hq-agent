# Azure Resources — hq-agent

**Subscription:** Be Concrete Main (`e70b3a39-d6a6-43c9-9c42-90bb2ed1a393`)
**Resource Group:** `hq-agent-resource-group`
**Primary Location:** northeurope
**Last verified:** 2026-04-21 using `az`

## Resources

| Resource | Name | Type | Location | Notes |
|---|---|---|---|---|
| Resource Group | `hq-agent-resource-group` | Microsoft.Resources/resourceGroups | northeurope | Primary resource group |
| Storage Account | `hqagentstorage` | Microsoft.Storage/storageAccounts | northeurope | Blob, Queue, and Table storage for contracts and Function runtime state |
| Static Web App | `hq-agent-static-web-app` | Microsoft.Web/staticSites | westeurope | Hosts the Vue frontend and SWA-managed `/api`. Default URL: `wonderful-ground-0acacb103.7.azurestaticapps.net` |
| Agents Function App | `hq-agent-function-app` | Microsoft.Web/sites | northeurope | Standalone Azure Functions app for contract agents. C# isolated worker, Functions v4, .NET 10 |
| Function App Plan | `NorthEuropePlan` | Microsoft.Web/serverFarms | northeurope | Windows Consumption plan (Y1). Hosts `hq-agent-function-app` |
| Application Insights | `hq-agent-function-app` | Microsoft.Insights/components | northeurope | Telemetry for `hq-agent-function-app` |

## Storage

### Blob Containers

| Container | Purpose |
|---|---|
| `contracts` | Uploaded contract files at `contracts/{correlationId}/{fileName}` |
| `azure-webjobs-hosts` | Azure Functions runtime state |
| `azure-webjobs-secrets` | Azure Functions host/function keys |
| `scm-releases` | Deployment/runtime artifact storage |

### Queues

| Queue | Purpose |
|---|---|
| `contract-processing` | Work queue. SWA `/api/upload-contract` writes `ContractMessage`; `ContractIngestion` consumes it |
| `contract-processing-poison` | Azure Functions poison queue for failed `contract-processing` messages |

### Tables

| Table | Purpose |
|---|---|
| `Contracts` | Contract records with original extraction JSON plus normalized query fields such as dates, counterparties, people, renewal, assignment, payment, risk, status, blob path, owner, and visibility |
| `ContractChatHistory` | Contract chat turns stored by `ContractChatAgent` per chat session |

## Runtime Split

| Area | Hosted By | Runtime | Deployment |
|---|---|---|---|
| Frontend | Azure Static Web Apps | Vue/Vite static assets | `.github/workflows/deploy-frontend.yml` |
| Browser API (`/api/*`) | SWA-managed Azure Functions from `api/` | C# isolated worker, `net8.0` | `.github/workflows/deploy-frontend.yml` with `api_location: api` |
| Contract agents | `hq-agent-function-app` from `agents/` | C# isolated worker, `net10.0`, Functions v4 | `.github/workflows/deploy-agent.yml` |
| Shared library | `shared/HqAgent.Shared` | `net8.0` | Referenced by both `api/` and `agents/`; agents can consume the `net8.0` assembly from .NET 10 |

`api/` intentionally stays on `net8.0` because Azure Static Web Apps managed Functions currently do not support .NET 10. We are staying with SWA-managed `/api`, so this runtime split is intentional.

## App Settings

### Static Web App

Production SWA app settings include:

| Setting | Purpose |
|---|---|
| `APP_ID` | Be Concrete app identifier (`hqagents`) |
| `STORAGE_CONNECTION_STRING` | Access to `hqagentstorage` for uploads, tables, and queues |
| `CHAT_AGENT_BASE_URL` | Base URL for `hq-agent-function-app` |
| `CHAT_AGENT_KEY` | Function key used by the SWA `/api/contract-chat` proxy |

### Agents Function App

Production Function App settings include:

| Setting | Purpose |
|---|---|
| `FUNCTIONS_EXTENSION_VERSION` | Functions runtime, currently `~4` |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED` | Cold-start optimization for isolated .NET Functions |
| `APP_ID` | Be Concrete app identifier (`hqagents`) |
| `STORAGE_CONNECTION_STRING` | Access to `hqagentstorage` for contract blobs, queues, and tables |
| `AzureWebJobsStorage` | Azure Functions runtime storage |
| `OPENAI_API_KEY` | OpenAI API key for `ContractChatAgent` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Telemetry connection for Application Insights |

## Production Flows

### Upload And Ingestion

```
Frontend
  → POST /api/upload-contract
  → [SWA-managed api/UploadContract]
      - validates caller/admin role
      - uploads file to hqagentstorage/contracts/{correlationId}/{fileName}
      - writes ContractMessage to hqagentstorage/contract-processing
      - returns { correlationId, blobName, fileName, status: "processing" }

  → [hq-agent-function-app / ContractIngestion]
      - queue trigger reads contract-processing
      - ContractOrchestratorAgent triages and extracts facts
      - writes result to Contracts table
```

There is no active `contract-completed` queue in production.

### Contract Chat

```
Frontend
  → POST /api/contract-chat
  → [SWA-managed api/ContractChat]
      - validates caller/user role
      - forwards identity headers to hq-agent-function-app
      - calls /api/contract-chat using CHAT_AGENT_KEY

  → [hq-agent-function-app / ContractChat]
      - ContractChatAgent answers using contract tools
      - reads Contracts table and contract blobs as needed
      - writes chat turns to ContractChatHistory
      - returns answer plus contract references
```

## Functions In Production

`hq-agent-function-app` currently exposes:

| Function | Trigger | Route/Queue | Entry Point |
|---|---|---|---|
| `ContractChat` | HTTP trigger, function auth | `POST /api/contract-chat` | `HqAgent.Agents.Contract.Triggers.ContractChatFunction.Run` |
| `ContractIngestion` | Queue trigger | `contract-processing` | `HqAgent.Agents.Contract.Triggers.ContractIngestion.Run` |

## Notable Decisions

- **SWA-managed `/api` stays** — browser-facing endpoints remain in the Static Web App managed Functions model.
- **Agents are separate** — agent runtime is hosted in `hq-agent-function-app` so it can run .NET 10 and long-lived agent dependencies without forcing SWA `/api` off its supported runtime.
- **No active blob-trigger ingestion** — uploads enqueue directly from `api/UploadContract`; ingestion is queue-triggered.
- **No active alert timer resources** — `ContractAlerts` is not present in production storage.
- **Unused Azure resources removed** — `NorthEuropeLinuxDynamicPlan`, the orphaned `hq-agent-orchestrator-app` Application Insights component, and the old `azure-webjobs-blobtrigger-hq-agent-function-app` queue were deleted on 2026-04-21.
- **No containers, App Service web apps, Container Registry, Dapr, Kubernetes, Logic Apps, Service Bus, or SignalR** are part of the current production runtime.
- **Storage table name is `Contracts`** — the old `ContractExtractions` name is no longer used.
