# The server as a container image, built by .github/workflows/image.yml through the shared mcp-image.yml and pushed
# from a version tag as ghcr.io/<owner>/s3l-e2e-mcp.
#
#   docker build -t s3l-e2e-mcp:local .
#
# The server listens on every interface at 8828 for the platform's workers (McpHttpHost.CreateContainerBuilder, on the
# port of SNAIL_MCP_E2E_PORT: the published port is the boundary, and a browser's request is still refused), and its
# restore proxy at 3128 for the environments it raises. The SDK is pinned to the band global.json asks for: on the
# floating sdk:10.0 a newer feature band would one day refuse the build.
#
# The server is native: the SDK compiles it ahead of time (Native AOT), which needs clang and zlib in the build stage,
# and the image runs the one executable on runtime-deps, with no .NET runtime in it — half the start and the memory of
# the framework-dependent build. It is compiled for the platform the image is built for (TARGETARCH, which buildx sets:
# amd64 is linux-x64, arm64 is linux-arm64): ahead-of-time compilation does not cross architectures in the SDK image.
#
# VERSION is the release's when the shared mcp-image.yml builds the image (0.0.0-<branch>.<commit> from a branch)
# and 0.0.0-local otherwise: the server reports it in initialize and the image carries it as its OCI version.
ARG VERSION=0.0.0-local

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
ARG VERSION
ARG TARGETARCH
RUN apt-get update \
    && apt-get install --yes --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /source
COPY . .
RUN case "$TARGETARCH" in amd64|"") rid=linux-x64 ;; *) rid="linux-$TARGETARCH" ;; esac \
    && dotnet publish src/Snail.MCP.E2E.csproj -c Release -r "$rid" -o /app -p:Version=$VERSION

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
ARG VERSION
LABEL org.opencontainers.image.version=$VERSION
# tini runs as process 1 and starts the program under it: .NET ends an unhandled exception with SIGABRT, which process
# 1 of a container ignores, so run as process 1 itself a program that fails at start spins at a full core and never
# exits, and nothing restarts it. Under tini it exits with 134.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends tini \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
EXPOSE 8828 3128
USER app

ENTRYPOINT ["/usr/bin/tini", "--", "/app/Snail.MCP.E2E"]
