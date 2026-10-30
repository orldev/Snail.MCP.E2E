# Snail.MCP.E2E

Task environments for .NET apps: run the app with its services, call its API, read logs, run tests

An agent that changed an app can check that the app works, not only that it compiles. The server raises the task's
environment — the app built from the working copy, beside the services it needs — and lets the agent call it, read what
it printed and run the project's end-to-end tests against it. Built for Snail.Swarm.Agent; each environment is a Nomad
job.

Its tools: [docs/reference/tools](docs/reference/tools/index.md), written from the server itself. Built on
[Snail.Sdk](https://github.com/orldev/Snail.Sdk).

## The manifest

A project says what its environment holds in `.snail/environment.json`:

```json
{
  "app": {
    "project": "src/Shop.Api/Shop.Api.csproj",
    "port": 8080,
    "health": "/health",
    "env": { "Mongo__Url": "mongodb://{mongo}:27017", "Rabbit__Host": "{rabbit}" }
  },
  "services": {
    "mongo": { "image": "mongo:8.0", "port": 27017 },
    "rabbit": { "image": "rabbitmq:4-alpine", "port": 5672 }
  },
  "tests": { "project": "tests/Shop.E2E/Shop.E2E.csproj", "env": { "APP_URL": "{app}" } }
}
```

`{name}` in a setting becomes that service's host inside the environment, `{app}` the app's address. Port and health
default to 8080 and `/health`; comments and trailing commas are allowed. Snail.Swarm.Agent keeps a whole sample in
`tests/Samples/Shop`.

## How an environment runs

- The session's address names the working copy, `/mcp?root=/workspaces/<task>/step-N`; the task is the copy's first
  folder, so every step of a task reaches the same environment, and `e2e_up` replaces it with the current files.
- The environment is the Nomad job `e2e-<task>`: one task per service from its image, and the app built and run with
  the .NET SDK image from a fresh copy of the working copy, which is mounted read-only.
- Everything sits on one internal Docker network. The way out is the server's proxy, which lets through NuGet's hosts
  only; packages are kept in a shared volume.
- The tests run as the batch job `e2e-<task>--tests` in the same network, with `APP_URL` pointing at the app.
- An environment nobody called for 60 minutes is stopped.

## Running it

```bash
docker build --build-context feed=../../nuget -t snail/s3l-e2e-mcp:latest .
```

Snail.Sdk is not on nuget.org yet, so the build takes the local feed as the build context `feed`. In Snail.Swarm.Agent
the server stands beside the MCP kit: `SWARM_ENVIRONMENTS=1 sh deploy/stand.sh up` (`deploy/compose.environments.yml`).

| Setting | Default | |
| --- | --- | --- |
| `SNAIL_MCP_E2E_NOMAD_ADDRESS` | `http://nomad:4646` | Nomad's API |
| `SNAIL_MCP_E2E_NETWORK` | `s3l-e2e` | internal Docker network of the environments; the server joins it too |
| `SNAIL_MCP_E2E_WORKSPACES_ROOT` | `/workspaces` | working copies as the server sees them, and where environments mount them |
| `SNAIL_MCP_E2E_WORKSPACES_VOLUME` | `workspaces` | Nomad host volume of the same copies |
| `SNAIL_MCP_E2E_RESTORE_PROXY` | none | the proxy's address as environments reach it, such as `http://mcp-e2e:3128` |
| `SNAIL_MCP_E2E_RESTORE_PROXY_PORT` | `0` (off) | port the proxy listens on |
| `SNAIL_MCP_E2E_SDK_IMAGE` | `mcr.microsoft.com/dotnet/sdk:10.0` | image apps and tests are built in |
