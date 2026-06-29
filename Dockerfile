# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SonarCopilotFix/SonarCopilotFix.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained false \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS runtime
ARG GH_CLI_VERSION=2.74.2
ARG COPILOT_CLI_VERSION=v1.0.65

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl git jq unzip \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL "https://github.com/cli/cli/releases/download/v${GH_CLI_VERSION}/gh_${GH_CLI_VERSION}_linux_amd64.deb" -o /tmp/gh.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends /tmp/gh.deb \
    && rm -f /tmp/gh.deb \
    && rm -rf /var/lib/apt/lists/*

# Install the standalone GitHub Copilot CLI. The legacy gh-copilot extension does
# not provide the `copilot` executable used for programmatic agent workflows.
RUN curl -fsSL https://gh.io/copilot-install \
    | VERSION="${COPILOT_CLI_VERSION}" PREFIX=/usr/local bash \
    && copilot version

WORKDIR /github/workspace
COPY --from=build /app/publish /app
ENTRYPOINT ["dotnet", "/app/SonarCopilotFix.dll"]
