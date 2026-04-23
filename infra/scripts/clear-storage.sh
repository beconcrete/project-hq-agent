#!/usr/bin/env bash
# Clears all contract data from blob, queues, and table storage.
# Safe to run between test uploads — does not delete containers, queues, or tables.
set -euo pipefail

STORAGE_ACCOUNT="hqagentstorage"
RESOURCE_GROUP="hq-agent-resource-group"

KEY=$(az storage account keys list \
  --account-name "$STORAGE_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --query "[0].value" -o tsv)

ARGS=(--account-name "$STORAGE_ACCOUNT" --account-key "$KEY")

# --- Blobs ---
az storage blob delete-batch "${ARGS[@]}" --source contracts --delete-snapshots include >/dev/null
echo "Blobs - done"

# --- Queues ---
for QUEUE in contract-processing contract-processing-poison; do
  EXISTS=$(az storage queue exists "${ARGS[@]}" --name "$QUEUE" --query "exists" -o tsv 2>/dev/null || echo "false")
  if [ "$EXISTS" = "true" ]; then
    if ! ERROR=$(az storage message clear "${ARGS[@]}" --queue-name "$QUEUE" >/dev/null 2>&1); then
      echo "Failed to clear queue: $QUEUE" >&2
      echo "$ERROR" >&2
      exit 1
    fi
  fi
done
echo "Queues - done"

# --- Tables ---
for TABLE in Contracts ContractChatHistory; do
  TABLE_EXISTS=$(az storage table exists "${ARGS[@]}" --name "$TABLE" --query "exists" -o tsv 2>/dev/null || echo "false")
  if [ "$TABLE_EXISTS" = "true" ]; then
    while IFS=$'\t' read -r PARTITION_KEY ROW_KEY; do
      if [ -z "${PARTITION_KEY:-}" ] || [ -z "${ROW_KEY:-}" ]; then
        continue
      fi

      az storage entity delete "${ARGS[@]}" \
        --table-name "$TABLE" \
        --partition-key "$PARTITION_KEY" \
        --row-key "$ROW_KEY" \
        --if-match "*" \
        >/dev/null
    done < <(az storage entity query "${ARGS[@]}" --table-name "$TABLE" \
      --query "items[].[PartitionKey,RowKey]" -o tsv)
  fi
done
echo "Tables - done"
