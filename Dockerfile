# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0-bookworm-slim AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SonarCopilotFix/SonarCopilotFix.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained false \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-bookworm-slim AS runtime
ARG GH_CLI_VERSION=2.74.2
ARG INSTALL_GH_COPILOT_EXTENSION=true

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl git jq unzip \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL "https://github.com/cli/cli/releases/download/v${GH_CLI_VERSION}/gh_${GH_CLI_VERSION}_linux_amd64.deb" -o /tmp/gh.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends /tmp/gh.deb \
    && rm -f /tmp/gh.deb \
    && rm -rf /var/lib/apt/lists/*

# GitHub Copilot CLI support is still environment-dependent. The image attempts to
# install the official gh extension, while the app fails clearly if no configured
# non-interactive Copilot command can run.
RUN if [ "$INSTALL_GH_COPILOT_EXTENSION" = "true" ]; then gh extension install github/gh-copilot || true; fi

WORKDIR /github/workspace
COPY --from=build /app/publish /app
ENTRYPOINT ["dotnet", "/app/SonarCopilotFix.dll"]
