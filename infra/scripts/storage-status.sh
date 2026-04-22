#!/usr/bin/env bash
# Lists the current production storage objects that matter for HQ Agent.
set -euo pipefail

STORAGE_ACCOUNT="hqagentstorage"
RESOURCE_GROUP="hq-agent-resource-group"

KEY=$(az storage account keys list \
  --account-name "$STORAGE_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --query "[0].value" -o tsv)

ARGS=(--account-name "$STORAGE_ACCOUNT" --account-key "$KEY")

echo "Storage account: $STORAGE_ACCOUNT"
echo ""

echo "Containers"
az storage container list "${ARGS[@]}" \
  --query "[].{name:name, publicAccess:properties.publicAccess}" -o table

echo ""
echo "Queues"
for QUEUE in contract-processing contract-processing-poison; do
  EXISTS=$(az storage queue exists "${ARGS[@]}" --name "$QUEUE" --query "exists" -o tsv 2>/dev/null || echo "false")
  if [ "$EXISTS" = "true" ]; then
    PEEK=$(az storage message peek "${ARGS[@]}" --queue-name "$QUEUE" --num-messages 1 --query "length(@)" -o tsv 2>/dev/null || echo "unknown")
    if [ "$PEEK" = "0" ]; then
      echo "$QUEUE: exists, no visible messages"
    elif [ "$PEEK" = "1" ]; then
      echo "$QUEUE: exists, at least one visible message"
    else
      echo "$QUEUE: exists"
    fi
  else
    echo "$QUEUE: missing"
  fi
done

echo ""
echo "Tables"
for TABLE in Contracts ContractChatHistory; do
  EXISTS=$(az storage table exists "${ARGS[@]}" --name "$TABLE" --query "exists" -o tsv 2>/dev/null || echo "false")
  echo "$TABLE: $EXISTS"
done
