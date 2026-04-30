#!/usr/bin/env bash
# Purge all rows from legacy tables before the new entity model goes live.
# Safe to run multiple times — 404s on missing tables/rows are ignored.
#
# Usage:
#   export AZURE_STORAGE_CONNECTION_STRING="DefaultEndpointsProtocol=..."
#   ./infra/purge-legacy-data.sh
#
# Or pass the connection string directly:
#   ./infra/purge-legacy-data.sh "DefaultEndpointsProtocol=..."

set -euo pipefail

CONN="${1:-${AZURE_STORAGE_CONNECTION_STRING:-}}"
if [[ -z "$CONN" ]]; then
  echo "ERROR: Provide connection string as first arg or set AZURE_STORAGE_CONNECTION_STRING" >&2
  exit 1
fi

TABLES=(
  Contracts
  Employees
  HRChatHistory
  ContractChatHistory
  SalesForecastChatHistory
)

purge_table() {
  local table="$1"
  echo "--- Purging table: $table"

  # Fetch all entity partition+row keys
  local entities
  entities=$(az storage entity query \
    --table-name "$table" \
    --connection-string "$CONN" \
    --select "PartitionKey,RowKey" \
    --query "items[].{pk:PartitionKey, rk:RowKey}" \
    -o json 2>/dev/null) || { echo "  Table $table not found or empty — skipping"; return; }

  local count
  count=$(echo "$entities" | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null || echo "0")

  if [[ "$count" == "0" ]]; then
    echo "  Table $table is already empty"
    return
  fi

  echo "  Found $count rows — deleting..."

  echo "$entities" | python3 -c "
import sys, json, subprocess

rows = json.load(sys.stdin)
conn = sys.argv[1]
table = sys.argv[2]
for row in rows:
    result = subprocess.run([
        'az', 'storage', 'entity', 'delete',
        '--table-name', table,
        '--partition-key', row['pk'],
        '--row-key', row['rk'],
        '--connection-string', conn,
        '--if-exists', 'true',
    ], capture_output=True)
    if result.returncode != 0 and b'ResourceNotFound' not in result.stderr:
        print(f\"  WARN: {row['pk']}/{row['rk']}: {result.stderr.decode().strip()}\")
print(f'  Deleted {len(rows)} rows from $table')
" "$CONN" "$table"
}

for table in "${TABLES[@]}"; do
  purge_table "$table"
done

echo ""
echo "Purge complete. Re-upload contracts and employees to populate the new schema."
