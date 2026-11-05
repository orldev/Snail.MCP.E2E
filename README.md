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
  folder, so every step of a task reaches the same environment, and `e2e_up` replaces it with the current files. A
  task's id is a DNS label of at most 37 characters; a part's host is `e2e-<task>--<part>`.
- The environment is the Nomad job `e2e-<task>`: one task per service from its image, and the app built and run with
  the .NET SDK image. Before them the server's own task `s3l_prepare` copies the task's working copy and packages into
  the allocation; the app sees neither the other tasks' copies nor the shared package volume. After them `s3l_keep`
  writes the task's packages back to its own folder of that volume, so the next raise restores from disk.
- Everything sits on one internal Docker network. The way out is the server's proxy, which lets through NuGet's hosts
  only, with limits of its own: 256 connections, 10 s for a request head, 2 minutes of silence.
- `e2e_up` and `e2e_test` run as jobs: the call answers within its `wait` (60 s unless asked otherwise) with the result,
  or with the job's `job://e2e/…` resource, whose text holds the result once it ends. One environment does one thing
  at a time: the same call while it runs — a test run with the same filter — waits on the same job, another waits its
  turn, `e2e_down` cancels them. A job of one task is unknown to the sessions of another; the last twenty ended jobs of
  each task stay readable.
- The tests run as the batch job `e2e-<task>--tests` in the same network, with `APP_URL` pointing at the app. A run
  ends on its own time limit, and one a restart left going is stopped at the next start.
- An environment nobody called for 60 minutes is stopped, unless a raise or test run is at work on it.
- The manifest is read only from a file of the copy itself: a link, or anything but a regular file, is refused.
- The server is native (Native AOT) on `runtime-deps`: it answers its health check about 40 ms after it starts and
  holds about 40 MB, against 130 ms and 100 MB framework-dependent. It listens on every interface of its container,
  and refuses a request a browser makes (one with `Origin`) with 403.

## Running it

```bash
docker build --build-context feed=../../nuget -t s3l-e2e-mcp:local .
```

The Snail.Sdk version `global.json` asks for is in the local feed until it is released, so the build takes that feed
as the build context `feed`. In Snail.Swarm.Agent
the server stands beside the MCP kit: `SWARM_ENVIRONMENTS=1 sh deploy/stand.sh up` (`deploy/compose.environments.yml`).

| Setting | Default | |
| --- | --- | --- |
| `SNAIL_MCP_E2E_PORT` | `8828` | port of the MCP endpoint and of `/healthz` |
| `SNAIL_MCP_E2E_NOMAD_ADDRESS` | `http://nomad:4646` | Nomad's API |
| `SNAIL_MCP_E2E_NETWORK` | `s3l-e2e` | internal Docker network of the environments; the server joins it too |
| `SNAIL_MCP_E2E_WORKSPACES_ROOT` | `/workspaces` | working copies as the server sees them, and where environments mount them |
| `SNAIL_MCP_E2E_WORKSPACES_VOLUME` | `workspaces` | Nomad host volume of the same copies |
| `SNAIL_MCP_E2E_RESTORE_PROXY` | none | the proxy's address as environments reach it, such as `http://mcp-e2e:3128` |
| `SNAIL_MCP_E2E_RESTORE_PROXY_PORT` | `0` (off) | port the proxy listens on |
| `SNAIL_MCP_E2E_SDK_IMAGE` | `mcr.microsoft.com/dotnet/sdk:10.0` | image apps and tests are built in |
