#!/bin/bash
# Start the .NET app and the Dapr sidecar in the same container.
# Both processes run concurrently; if either dies the container exits.
set -e

# Start the .NET app first so Dapr can reach it immediately
dotnet ContractOrchestratorAgent.dll &
APP_PID=$!

# Give the app a moment to bind to port 8080
sleep 3

# Start the Dapr sidecar — it will poll the queue and POST to the app
daprd \
  --app-id        contract-orchestrator \
  --app-port      8080 \
  --dapr-http-port 3500 \
  --components-path /dapr/components \
  --log-level     info \
  &
DAPRD_PID=$!

# Forward SIGTERM/SIGINT to both processes
trap "kill $APP_PID $DAPRD_PID 2>/dev/null; exit 0" TERM INT

# Wait — if either process exits unexpectedly, exit the container
wait $APP_PID $DAPRD_PID
