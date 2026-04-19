# Azure Resources — hq-agent

**Subscription:** Be Concrete Main (`e70b3a39-d6a6-43c9-9c42-90bb2ed1a393`)
**Resource Group:** `hq-agent-resource-group`
**Primary Location:** northeurope
**Last updated:** 2026-04-19 (issue #24 pending)

## Resources

| Resource | Name | Type | Location | Notes |
|---|---|---|---|---|
| Resource Group | `hq-agent-resource-group` | Microsoft.Resources/resourceGroups | northeurope | Primary resource group |
| Storage Account | `hqagentstorage` | Microsoft.Storage/storageAccounts | northeurope | Blob (contracts), Queue (work queue), Table (extracted data + alerts) |
| Static Web App | `hq-agent-static-web-app` | Microsoft.Web/staticSites | westeurope | SWA only available in westeurope for EU. CDN-distributed globally. URL: `wonderful-ground-0acacb103.7.azurestaticapps.net` |
| Function App | `hq-agent-function-app` | Microsoft.Web/sites | northeurope | C# isolated worker, Consumption plan (Y1). Hosts all functions and agents — `functions/HqAgentFunctions` and `agents/contract-orchestrator-agent`. |
| Function App Plan | `NorthEuropePlan` | Microsoft.Web/serverFarms | northeurope | Consumption (Y1) — auto-created with `hq-agent-function-app` |
| Application Insights | `hq-agent-function-app` | microsoft.insights/components | northeurope | Auto-created with Function App |

## Storage Containers / Queues / Tables

| Type | Name | Purpose |
|---|---|---|
| Blob Container | `contracts` | Uploaded contract files (PDF, DOCX) |
| Queue | `contract-processing` | Work queue — ContractIngestion function reads from here |
| Queue | `contract-completed` | Completion notifications — written after processing, consumed by WebSocket handler (future) |
| Table | `ContractExtractions` | Extracted contract fields — open schema, `status` is `completed` or `pending_review` |
| Table | `ContractAlerts` | Contract expiry alerts — written by timer function, read by /api/contract-alerts |

## Ingestion flow

```
Frontend → POST /api/upload-contract
  → [SWA /api/] uploads blob to hqagentstorage/contracts/{correlationId}/{name}
  → sends ContractMessage to contract-processing queue          ← pending issue #24
                                                                   (today: BlobTrigger does this)
  → returns 200 { correlationId }

  → [hq-agent-function-app] ContractIngestion queue trigger fires
  → triage (fast model) + extraction (quality model)
  → writes ExtractionResult to ContractExtractions table
  → sends completion notification to contract-completed queue
```

## Notable Decisions

- **No containers, no App Service, no Container Registry, no Dapr** — all deleted 2026-04-17. All agent logic runs as Azure Functions on the Consumption plan using native queue/blob bindings.
- **API enqueues directly (pending issue #24)** — after issue #24, `UploadContract` sends the queue message itself. Until then, `ContractBlobTrigger` handles this via blob-event polling.
- **`hq-agent-function-app` hosts `functions/HqAgentFunctions`** — queue trigger for contract ingestion pipeline. Blob trigger will be removed in issue #24.
- **One Function App for everything** — both `functions/HqAgentFunctions` and `agents/` deploy to `hq-agent-function-app`. This may be split in the future but requires an explicit architectural decision.
- **Shared library `HqAgent.Shared`** — `net8.0` class library referenced by `api/`, `functions/`, and `agents/`. Contains `BlobStorageService`, `TableStorageService`, `ContractMessage`, `ExtractionResult`, `IAIModelClient`.
- **SWA uses westeurope** — Azure Static Web Apps are not available in northeurope. Content is CDN-distributed so latency for end users is unaffected.
- **Function App uses Consumption plan** — scales to zero, ~$0-2/month at low volume.
- **`AzureWebJobsStorage`** — required by the Functions runtime (lease containers, distributed locks). Does not need to match `hqagentstorage`. After issue #24 removes the BlobTrigger, this setting is purely an internal runtime concern.
