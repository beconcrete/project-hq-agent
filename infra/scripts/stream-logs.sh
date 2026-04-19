#!/usr/bin/env bash
# Poll Application Insights for recent traces from hq-agent-function-app.
#
# WHY NOT az webapp log tail?
# The Function App runs on the .NET isolated worker model. Application logs
# (_logger.LogInformation etc.) go directly to Application Insights — they
# never reach the Kudu HTTP log stream. az webapp log tail only shows IIS/
# platform HTTP logs, not application traces.
#
# REAL-TIME OPTION (zero-lag, portal only):
#   Portal → hq-agent-resource-group → hq-agent-function-app →
#   Application Insights → Live Metrics
#
# FOLLOWING A DOCUMENT END-TO-END
# --------------------------------
# Run with a correlationId to filter:
#   bash stream-logs.sh <correlationId>
#
# Expected sequence for a healthy upload:
#   [ContractBlobTrigger]  "New contract uploaded: {correlationId}/{name} ({size} bytes)"
#   [ContractBlobTrigger]  "Enqueued processing message for correlationId: {correlationId}"
#   [ContractIngestion]    "ContractIngestion triggered for {correlationId}"
#   [ContractWorkflow]     "Processing contract {correlationId} — {blobName}"
#   [ContractWorkflow]     "PDF text extracted: {charCount} chars"
#   [ContractWorkflow]     "Workflow complete for {correlationId}"
#   [ContractIngestion]    "Contract {correlationId} stored — type:... pendingReview:... model:..."

set -euo pipefail

APP_INSIGHTS="hq-agent-function-app"
RESOURCE_GROUP="hq-agent-resource-group"
POLL_SECONDS=10
CORRELATION_ID="${1:-}"

if [ -n "$CORRELATION_ID" ]; then
  FILTER="| where message contains \"$CORRELATION_ID\""
  echo "Filtering for correlationId: $CORRELATION_ID"
else
  FILTER=""
  echo "Showing all traces. Pass a correlationId as argument to filter: bash stream-logs.sh <id>"
fi

echo "Polling Application Insights every ${POLL_SECONDS}s. Press Ctrl+C to stop."
echo ""

LAST_TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

while true; do
  RESULTS=$(az monitor app-insights query \
    --app "$APP_INSIGHTS" \
    --resource-group "$RESOURCE_GROUP" \
    --analytics-query "traces
      | where timestamp > datetime('$LAST_TIMESTAMP')
      $FILTER
      | project timestamp, message, severityLevel, cloud_RoleName
      | order by timestamp asc" \
    --output json 2>/dev/null \
    | jq -r '.tables[0].rows[] | "\(.[0]) [\(.[3])] \(.[1])"' 2>/dev/null || true)

  if [ -n "$RESULTS" ]; then
    echo "$RESULTS"
    LAST_TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
  fi

  sleep "$POLL_SECONDS"
done
