# Azure Resources — hq-agent

**Subscription:** Be Concrete Main (`e70b3a39-d6a6-43c9-9c42-90bb2ed1a393`)
**Resource Group:** `hq-agent-resource-group`
**Primary Location:** northeurope
**Last updated:** 2026-04-17

## Resources

| Resource | Name | Type | Location | Notes |
|---|---|---|---|---|
| Resource Group | `hq-agent-resource-group` | Microsoft.Resources/resourceGroups | northeurope | Primary resource group |
| Storage Account | `hqagentstorage` | Microsoft.Storage/storageAccounts | northeurope | Blob (contracts), Queue (work queue), Table (extracted data + alerts) |
| Static Web App | `hq-agent-static-web-app` | Microsoft.Web/staticSites | westeurope | SWA only available in westeurope for EU. CDN-distributed globally. URL: `wonderful-ground-0acacb103.7.azurestaticapps.net` |
| Function App | `hq-agent-function-app` | Microsoft.Web/sites | northeurope | C# isolated worker, Consumption plan (Y1). Queue triggers, blob triggers, timer triggers. |
| Function App Plan | `NorthEuropePlan` | Microsoft.Web/serverFarms | northeurope | Consumption (Y1) — auto-created with Function App |
| Application Insights | `hq-agent-function-app` | microsoft.insights/components | northeurope | Auto-created with Function App |

## Storage Containers / Queues / Tables

| Type | Name | Purpose |
|---|---|---|
| Blob Container | `contracts` | Uploaded contract files (PDF, DOCX) |
| Queue | `contract-processing` | Work queue — ContractIngestion function reads from here |
| Table | `ContractExtractions` | Extracted contract fields (schema-flexible) |
| Table | `ContractAlerts` | Contract expiry alerts — written by timer function, read by /api/contract-alerts |

## Notable Decisions

- **No containers, no App Service, no Container Registry** — deleted 2026-04-17. All agent logic runs as Azure Functions on the Consumption plan.
- **No Dapr** — deleted 2026-04-17. Native Azure Functions queue/blob bindings replace Dapr components entirely.
- **SWA uses westeurope** — Azure Static Web Apps are not available in northeurope. Content is CDN-distributed so latency for end users is unaffected.
- **Function App uses Consumption plan** — scales to zero, ~$0-2/month at low volume.
