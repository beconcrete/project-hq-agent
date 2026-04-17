# Build context: repo root (docker build -f agents/contract-orchestrator-agent/Dockerfile .)
# This is needed so the infra/dapr components can be bundled into the image.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Install Dapr runtime (daprd) so the sidecar runs in the same container.
# For a production setup, prefer a separate sidecar container via Docker Compose or ACA.
ARG DAPR_VERSION=1.14.4
RUN apt-get update && apt-get install -y --no-install-recommends wget ca-certificates \
    && wget -qO /tmp/daprd.tar.gz \
       "https://github.com/dapr/dapr/releases/download/v${DAPR_VERSION}/daprd_linux_amd64.tar.gz" \
    && tar -xzf /tmp/daprd.tar.gz -C /tmp \
    && mv /tmp/daprd /usr/local/bin/daprd \
    && chmod +x /usr/local/bin/daprd \
    && rm -rf /tmp/* \
    && apt-get purge -y wget \
    && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY agents/contract-orchestrator-agent/ContractOrchestratorAgent.csproj .
RUN dotnet restore
COPY agents/contract-orchestrator-agent/. .
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY infra/dapr /dapr/components
COPY agents/contract-orchestrator-agent/entrypoint.sh .
RUN chmod +x entrypoint.sh
ENTRYPOINT ["./entrypoint.sh"]
