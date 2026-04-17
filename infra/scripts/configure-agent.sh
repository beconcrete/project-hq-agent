#!/usr/bin/env bash
# configure-agent.sh — One-time setup for the contract-orchestrator-agent on App Service.
#
# What it does:
#   1. Reads the storage connection string from Azure and sets it as an app setting
#   2. Enables a system-assigned managed identity on the App Service
#   3. Grants the identity AcrPull access to the container registry
#   4. Configures App Service to pull the agent image from ACR using managed identity
#   5. Prompts you to set ANTHROPIC_API_KEY once you have the key
#
# Prerequisites:
#   - az CLI installed and logged in
#   - Contributor access to hq-agent-resource-group

set -euo pipefail

RESOURCE_GROUP="hq-agent-resource-group"
APP_SERVICE="hq-agent-app-service"
STORAGE_ACCOUNT="hqagentstorage"
ACR_NAME="hqagentregistry"
IMAGE="hqagentregistry.azurecr.io/contract-orchestrator:latest"

GREEN='\033[0;32m'; CYAN='\033[0;36m'; BOLD='\033[1m'; RESET='\033[0m'
ok()  { echo -e "  ${GREEN}✓${RESET} $*"; }
info(){ echo -e "  ${CYAN}→${RESET} $*"; }
hdr() { echo -e "\n${BOLD}$*${RESET}"; }

# ── 1. Storage connection string ──────────────────────────────────────────────
hdr "1/5  Fetching storage connection string"
STORAGE_CONN_STR=$(az storage account show-connection-string \
  --name "$STORAGE_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --query connectionString -o tsv)
ok "Got connection string"

hdr "2/5  Setting App Service app settings"
az webapp config appsettings set \
  --name "$APP_SERVICE" \
  --resource-group "$RESOURCE_GROUP" \
  --settings \
    "STORAGE_CONNECTION_STRING=$STORAGE_CONN_STR" \
    "ASPNETCORE_URLS=http://+:8080" \
  --output none
ok "STORAGE_CONNECTION_STRING and ASPNETCORE_URLS set"

# ── 2. Managed identity → AcrPull ────────────────────────────────────────────
hdr "3/5  Enabling managed identity on App Service"
PRINCIPAL_ID=$(az webapp identity assign \
  --name "$APP_SERVICE" \
  --resource-group "$RESOURCE_GROUP" \
  --query principalId -o tsv)
ok "Principal ID: $PRINCIPAL_ID"

hdr "4/5  Granting AcrPull to managed identity"
ACR_ID=$(az acr show --name "$ACR_NAME" --query id -o tsv)
az role assignment create \
  --assignee "$PRINCIPAL_ID" \
  --role AcrPull \
  --scope "$ACR_ID" \
  --output none
ok "AcrPull granted"

# ── 3. Configure container image ──────────────────────────────────────────────
hdr "5/5  Configuring App Service to pull from ACR"
az webapp config container set \
  --name "$APP_SERVICE" \
  --resource-group "$RESOURCE_GROUP" \
  --docker-custom-image-name "$IMAGE" \
  --docker-registry-server-url "https://hqagentregistry.azurecr.io" \
  --output none

az webapp config set \
  --name "$APP_SERVICE" \
  --resource-group "$RESOURCE_GROUP" \
  --generic-configurations '{"acrUseManagedIdentityCreds":true}' \
  --output none
ok "Container image set to $IMAGE"

# ── Reminder for ANTHROPIC_API_KEY ────────────────────────────────────────────
echo ""
echo -e "${BOLD}Action required:${RESET} Set the Anthropic API key once you have it:"
echo ""
echo "  az webapp config appsettings set \\"
echo "    --name $APP_SERVICE \\"
echo "    --resource-group $RESOURCE_GROUP \\"
echo "    --settings ANTHROPIC_API_KEY=<your-key>"
echo ""
