#!/usr/bin/env bash
# Stream live logs from hq-agent-function-app to your terminal.
#
# WHERE LOGS END UP
# -----------------
# _logger.LogInformation(...) writes to two places:
#   1. Application Insights (Azure portal) — search in Logs → traces table
#      Portal: https://portal.azure.com → hq-agent-resource-group → hq-agent-function-app → Application Insights
#      Useful queries:
#        traces | order by timestamp desc | take 100
#        traces | where message contains "<correlationId>"
#        traces | where severityLevel >= 2  // Warning+
#   2. Live console stream (this script)
#
# Live Metrics (real-time dashboard with zero-lag):
#   Portal → Application Insights → Live Metrics
#
# FOLLOWING A DOCUMENT END-TO-END
# --------------------------------
# Search Application Insights Logs with a correlationId:
#   traces | where message contains "YOUR_CORRELATION_ID" | order by timestamp asc
#
# Expected log sequence for a healthy upload:
#   [ContractBlobTrigger]  "New contract uploaded: {correlationId}/{name} ({size} bytes)"
#   [ContractBlobTrigger]  "Enqueued processing message for correlationId: {correlationId}"
#   [ContractIngestion]    "ContractIngestion triggered for {correlationId}"
#   [ContractWorkflow]     "Processing contract {correlationId} — {blobName}"
#   [ContractWorkflow]     "PDF text extracted: {charCount} chars"
#   [ContractWorkflow]     "Workflow complete for {correlationId}"
#   [ContractIngestion]    "Contract {correlationId} stored — type:... pendingReview:... model:..."

set -euo pipefail

FUNCTION_APP="hq-agent-function-app"
RESOURCE_GROUP="hq-agent-resource-group"

echo "Streaming logs from $FUNCTION_APP..."
echo "Press Ctrl+C to stop."
echo ""

az webapp log tail \
  --name "$FUNCTION_APP" \
  --resource-group "$RESOURCE_GROUP"
