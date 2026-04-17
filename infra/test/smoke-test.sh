#!/usr/bin/env bash
# smoke-test.sh — End-to-end smoke test for the contract processing pipeline.
#
# What it does:
#   1. Prompts you to pick a sample contract (consulting-assignment or nda)
#   2. Uploads it to Blob Storage at contracts/{correlationId}/{file}
#   3. Waits for the blob trigger to fire and peeks the queue for the message
#   4. Polls ContractExtractions table until the extraction record appears
#
# Prerequisites:
#   - az CLI installed and logged in (az login)
#   - Contributor access to the hq-agent-resource-group

set -euo pipefail

# ─── Config ──────────────────────────────────────────────────────────────────

RESOURCE_GROUP="hq-agent-resource-group"
STORAGE_ACCOUNT="hqagentstorage"
CONTAINER="contracts"
QUEUE="contract-processing"
TABLE="ContractExtractions"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# ─── Colours ─────────────────────────────────────────────────────────────────

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
RESET='\033[0m'

ok()   { echo -e "  ${GREEN}✓${RESET} $*"; }
fail() { echo -e "  ${RED}✗${RESET} $*"; }
info() { echo -e "  ${CYAN}→${RESET} $*"; }
hdr()  { echo -e "\n${BOLD}$*${RESET}"; }

# ─── File picker ─────────────────────────────────────────────────────────────

hdr "Contract Smoke Test"
echo ""
echo "  Select a sample contract to upload:"

mapfile -t DOC_FILES < <(find "$SCRIPT_DIR" -maxdepth 1 \( -name "*.pdf" -o -name "*.docx" \) | sort)

if [[ ${#DOC_FILES[@]} -eq 0 ]]; then
  echo -e "${RED}No PDF or DOCX files found in $SCRIPT_DIR${RESET}" >&2
  echo "  Run: python3 $SCRIPT_DIR/generate-pdfs.py" >&2
  exit 1
fi

for i in "${!DOC_FILES[@]}"; do
  printf "    %d) %s\n" "$((i + 1))" "$(basename "${DOC_FILES[$i]}")"
done

echo ""
read -rp "  Choice [1-${#DOC_FILES[@]}]: " CHOICE

if ! [[ "$CHOICE" =~ ^[0-9]+$ ]] || (( CHOICE < 1 || CHOICE > ${#DOC_FILES[@]} )); then
  echo -e "${RED}Invalid choice.${RESET}" >&2
  exit 1
fi

PDF_FILE="${DOC_FILES[$((CHOICE - 1))]}"

FILENAME="$(basename "$PDF_FILE")"

# ─── Fetch connection string ──────────────────────────────────────────────────

hdr "1/4  Fetching storage connection string"
CONN_STR=$(az storage account show-connection-string \
  --name "$STORAGE_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --query connectionString \
  --output tsv)
ok "Got connection string for $STORAGE_ACCOUNT"

# ─── Upload blob ──────────────────────────────────────────────────────────────

CORRELATION_ID=$(uuidgen | tr '[:upper:]' '[:lower:]')
BLOB_NAME="$CORRELATION_ID/$FILENAME"

hdr "2/4  Uploading blob"
info "correlationId : $CORRELATION_ID"
info "blob path     : $CONTAINER/$BLOB_NAME"

az storage blob upload \
  --connection-string "$CONN_STR" \
  --container-name "$CONTAINER" \
  --name "$BLOB_NAME" \
  --file "$PDF_FILE" \
  --overwrite \
  --output none

ok "Blob uploaded"

# ─── Peek queue ───────────────────────────────────────────────────────────────

hdr "3/4  Waiting for blob trigger → queue message"
info "Giving the Function App a moment to fire..."

QUEUE_MSG=""
for i in $(seq 1 10); do
  sleep 3

  RAW=$(az storage message peek \
    --connection-string "$CONN_STR" \
    --queue-name "$QUEUE" \
    --num-messages 32 \
    --output json 2>/dev/null || echo "[]")

  # Messages from the .NET isolated worker are base64-encoded JSON.
  # Try base64-decode first, fall back to raw content.
  QUEUE_MSG=$(python3 - "$CORRELATION_ID" <<'PYEOF'
import sys, json, base64

target = sys.argv[1]
data   = json.load(sys.stdin)

for msg in data:
    content = msg.get("content", "")
    for candidate in [content, base64.b64decode(content + "==").decode("utf-8", errors="ignore")]:
        try:
            parsed = json.loads(candidate)
            if parsed.get("correlationId") == target:
                print(json.dumps(parsed, indent=2))
                sys.exit(0)
        except Exception:
            pass
PYEOF
  <<< "$RAW" || true)

  if [[ -n "$QUEUE_MSG" ]]; then
    ok "Queue message found (attempt $i):"
    echo "$QUEUE_MSG" | sed 's/^/      /'
    break
  fi

  info "Attempt $i/10 — not yet visible, retrying..."
done

if [[ -z "$QUEUE_MSG" ]]; then
  fail "Queue message not found after 30 seconds."
  echo ""
  echo "  Possible causes:"
  echo "    - Blob trigger cold start (Functions consumption plan can take ~60s)"
  echo "    - STORAGE_CONNECTION_STRING / AzureWebJobsStorage not set in Function App"
  echo "    - Function App is stopped"
  echo ""
  echo "  Check logs: az functionapp logs tail --name hq-agent-function-app --resource-group $RESOURCE_GROUP"
  echo ""
  read -rp "  Continue polling Table Storage anyway? [y/N]: " CONTINUE
  [[ "$CONTINUE" =~ ^[Yy]$ ]] || exit 1
fi

# ─── Poll Table Storage ───────────────────────────────────────────────────────

hdr "4/4  Polling ContractExtractions table"
info "PartitionKey = $CORRELATION_ID  |  RowKey = extraction"
info "Polling every 5 seconds, up to 120 seconds..."
echo ""

FOUND=false
for i in $(seq 1 24); do
  ENTITY=$(az storage entity show \
    --connection-string "$CONN_STR" \
    --table-name "$TABLE" \
    --partition-key "$CORRELATION_ID" \
    --row-key "extraction" \
    --output json 2>/dev/null || true)

  if [[ -n "$ENTITY" && "$ENTITY" != "null" && "$ENTITY" != "{}" ]]; then
    ok "Extraction record found after $((i * 5)) seconds:"
    echo "$ENTITY" | python3 -m json.tool | sed 's/^/      /'
    FOUND=true
    break
  fi

  printf "  [%3ds] Waiting...\r" "$((i * 5))"
  sleep 5
done

echo ""

if [[ "$FOUND" == false ]]; then
  fail "No extraction record appeared within 120 seconds."
  echo ""
  echo "  The contract-orchestrator-agent may not be deployed yet (issue #7)."
  echo "  Once deployed, re-run this script to validate the full pipeline."
  exit 1
fi

echo ""
echo -e "${GREEN}${BOLD}All checks passed.${RESET} Pipeline is working end-to-end."
echo ""
