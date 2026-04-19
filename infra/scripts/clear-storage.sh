#!/usr/bin/env bash
# Clears all contract data from blob, queues, and table storage.
# Safe to run between test uploads — does not delete the containers/queues/table themselves.
set -euo pipefail

STORAGE_ACCOUNT="hqagentstorage"
RESOURCE_GROUP="hq-agent-resource-group"

echo "Fetching storage key..."
KEY=$(az storage account keys list \
  --account-name "$STORAGE_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --query "[0].value" -o tsv)

ARGS=(--account-name "$STORAGE_ACCOUNT" --account-key "$KEY")

# --- Blobs ---
echo "Deleting all blobs in contracts container..."
az storage blob delete-batch "${ARGS[@]}" --source contracts --delete-snapshots include
echo "  Done."

# --- Queues ---
for QUEUE in contract-processing contract-processing-poison; do
  EXISTS=$(az storage queue exists "${ARGS[@]}" --name "$QUEUE" --query "exists" -o tsv 2>/dev/null || echo "false")
  if [ "$EXISTS" = "true" ]; then
    echo "Clearing queue: $QUEUE..."
    az storage message clear "${ARGS[@]}" --queue-name "$QUEUE"
    echo "  Done."
  else
    echo "Queue $QUEUE does not exist, skipping."
  fi
done

# --- Table ---
echo "Deleting ContractExtractions table (will be recreated on next write)..."
TABLE_EXISTS=$(az storage table exists "${ARGS[@]}" --name ContractExtractions --query "exists" -o tsv 2>/dev/null || echo "false")
if [ "$TABLE_EXISTS" = "true" ]; then
  az storage table delete "${ARGS[@]}" --name ContractExtractions
  echo "  Deleted. Azure will recreate it on first write."
else
  echo "  Table does not exist, nothing to delete."
fi

echo ""
echo "Storage cleared."
