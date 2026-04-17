#!/bin/bash
# Generate Dapr component files with actual env-var values, then start the app and sidecar.
# No set -e here — if daprd crashes we want to log it, not kill the whole container.

# ── Write component files with resolved values ────────────────────────────────
mkdir -p /tmp/dapr-components

cat > /tmp/dapr-components/contract-queue-binding.yaml << 'YAML'
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: contract-processing-queue
spec:
  type: bindings.azure.storagequeues
  version: v1
  metadata:
    - name: storageAccount
      value: hqagentstorage
    - name: queue
      value: contract-processing
YAML

cat > /tmp/dapr-components/pubsub.yaml << 'YAML'
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: hq-pubsub
spec:
  type: pubsub.azure.storagequeues
  version: v1
  metadata:
    - name: storageAccount
      value: hqagentstorage
YAML

echo "[entrypoint] Dapr components written to /tmp/dapr-components"

# ── Start the .NET app ────────────────────────────────────────────────────────
dotnet ContractOrchestratorAgent.dll &
APP_PID=$!
echo "[entrypoint] .NET app started (PID $APP_PID)"

# Give the app time to bind to port 8080 before Dapr starts forwarding traffic
sleep 3

# ── Start the Dapr sidecar ────────────────────────────────────────────────────
daprd \
  --app-id        contract-orchestrator \
  --app-port      8080 \
  --dapr-http-port 3500 \
  --components-path /tmp/dapr-components \
  --log-level     info \
  &
DAPRD_PID=$!
echo "[entrypoint] daprd started (PID $DAPRD_PID)"

# ── Keep the container alive ──────────────────────────────────────────────────
trap "echo '[entrypoint] Shutting down'; kill $APP_PID $DAPRD_PID 2>/dev/null; exit 0" TERM INT

# Wait for the .NET app — it is the primary process.
# If it exits, we shut down the sidecar too.
wait $APP_PID
echo "[entrypoint] .NET app exited. Stopping daprd."
kill $DAPRD_PID 2>/dev/null
wait $DAPRD_PID 2>/dev/null
