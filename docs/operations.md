# Operations

This is the minimum operating guide for the current production setup:

- Frontend and SWA-managed `/api`: Azure Static Web App `hq-agent-static-web-app`
- Agents: Azure Function App `hq-agent-function-app`
- Storage: `hqagentstorage`
- Tables: `Contracts`, `ContractChatHistory`
- Queues: `contract-processing`, `contract-processing-poison`

## Quick Health Checks

```bash
bash infra/scripts/storage-status.sh
az functionapp function list \
  --resource-group hq-agent-resource-group \
  --name hq-agent-function-app \
  --query "[].name" -o table
```

Expected active functions:

- `ContractIngestion`
- `ContractChat`

## Application Insights Queries

Recent ingestion and chat traces:

```kusto
traces
| where timestamp > ago(24h)
| where cloud_RoleName == "hq-agent-function-app"
| where message has_any ("ContractIngestion", "contract chat", "Tool", "stored", "Rejected out-of-domain")
| project timestamp, severityLevel, message, operation_Id
| order by timestamp desc
```

Follow one contract by correlation ID:

```kusto
traces
| where timestamp > ago(7d)
| where message contains "<correlation-id>"
| project timestamp, severityLevel, message, operation_Id
| order by timestamp asc
```

Failures:

```kusto
exceptions
| where timestamp > ago(24h)
| order by timestamp desc
```

Guardrail rejections:

```kusto
traces
| where timestamp > ago(7d)
| where message contains "Rejected out-of-domain contract chat message"
| summarize count() by bin(timestamp, 1d)
```

## Poison Queue

If ingestion fails repeatedly, Azure Storage will move the message to `contract-processing-poison`.

Inspect queue counts:

```bash
bash infra/scripts/storage-status.sh
```

Peek poison messages:

```bash
az storage message peek \
  --account-name hqagentstorage \
  --queue-name contract-processing-poison \
  --num-messages 5 \
  --auth-mode login
```

Do not clear the poison queue until the failed contract correlation IDs have been inspected in Application Insights.

## Safe Maintenance Scripts

| Script | Safe for production? | Use |
|---|---:|---|
| `infra/scripts/storage-status.sh` | Yes | Lists container, queue, and table state |
| `infra/scripts/stream-logs.sh` | Yes | Polls Application Insights traces |
| `infra/scripts/test-openai-key.sh` | Local only | Tests OpenAI extraction with a fixture PDF |
| `infra/scripts/clear-storage.sh` | No | Deletes contract blobs, queues, and the `Contracts` table after explicit confirmation |

## Incident Checklist

1. Check GitHub Actions for the latest deployment result.
2. Run `bash infra/scripts/storage-status.sh`.
3. If a contract is stuck, find its correlation ID in the UI and run `bash infra/scripts/stream-logs.sh <correlationId>`.
4. Check `contract-processing-poison` before re-uploading the same document.
5. If chat fails but ingestion works, verify SWA app setting `CHAT_AGENT_BASE_URL` and Function App setting `CONTRACT_CHAT_FUNCTION_KEY`.
6. If `/api` works but ingestion does not, verify Function App runtime and that `ContractIngestion` is loaded.
