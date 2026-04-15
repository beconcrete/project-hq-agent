# Azure Resources — hq-agent

**Subscription:** Be Concrete Main (`e70b3a39-d6a6-43c9-9c42-90bb2ed1a393`)
**Resource Group:** `hq-agent-resource-group`
**Primary Location:** northeurope
**Last updated:** 2026-04-15

## Resources

| Resource | Name | Type | Location | Notes |
|---|---|---|---|---|
| Resource Group | `hq-agent-resource-group` | Microsoft.Resources/resourceGroups | northeurope | Primary resource group |
| Storage Account | `hqagentstorage` | Microsoft.Storage/storageAccounts | northeurope | Blob (contracts), Queue (work queue), Table (extracted data) |
| Static Web App | `hq-agent-static-web-app` | Microsoft.Web/staticSites | westeurope | SWA only available in westeurope for EU. CDN-distributed globally. URL: `wonderful-ground-0acacb103.7.azurestaticapps.net` |
| Function App | `hq-agent-function-app` | Microsoft.Web/sites | northeurope | C# isolated worker, Windows consumption plan. Blob-triggered event handler → Queue |
| App Service Plan | `hq-agent-app-service-plan` | Microsoft.Web/serverfarms | westeurope | Linux B1 — hosts containerized .NET agents (used westeurope due to northeurope quota limit) |
| App Service | `hq-agent-app-service` | Microsoft.Web/sites | westeurope | Containerized .NET agents with Dapr. URL: `hq-agent-app-service.azurewebsites.net` |
| Container Registry | `hqagentregistry` | Microsoft.ContainerRegistry/registries | northeurope | Basic tier. Login server: `hqagentregistry.azurecr.io` |
| Application Insights | `hq-agent-function-app` | microsoft.insights/components | northeurope | Auto-created with Function App |

## Storage Containers / Queues / Tables (to create)

| Type | Name | Purpose |
|---|---|---|
| Blob Container | `contracts` | Uploaded contract files (PDF, DOCX) |
| Queue | `contract-processing` | Work queue — contract orchestrator reads from here |
| Table | `ContractExtractions` | Extracted contract fields (schema-flexible) |

## Notable Decisions

- **SWA uses westeurope** — Azure Static Web Apps are not available in northeurope. Content is CDN-distributed so latency for end users is unaffected.
- **App Service in westeurope** — northeurope has 0 Basic/Standard VM quota in this subscription. westeurope has limit 10.
- **Function App uses Windows consumption plan** — Linux consumption (dynamic) plan is not available in northeurope resource groups that contain other resource types.
- **App Service Plan is Free trial** — expires 2026-05-15, upgrade to B1 paid before then.
