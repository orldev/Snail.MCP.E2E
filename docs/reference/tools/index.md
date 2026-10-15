# Tools

7 tools in 2 families.
The names, descriptions and schemas are generated from the server itself, so they are exactly what the model receives in `tools/list`.

| Tool | Family | What it does |
| --- | --- | --- |
| [`e2e_up`](environments.md#e2e_up) | Environments | Raise the task's environment from the working copy and wait until the app answers its health check. The project describes it in .snail/environment.json: the … |
| [`e2e_status`](environments.md#e2e_status) | Environments | Say where the task's environment stands now without waiting: ready, starting, failed or stopped, each part's task state and restarts. |
| [`e2e_down`](environments.md#e2e_down) | Environments | Stop the task's environment and its test runs. |
| [`e2e_http`](environments.md#e2e_http) | Environments | Send one HTTP request to the running app, as a client inside its network would, and get back the status, the headers and the body. |
| [`e2e_logs`](environments.md#e2e_logs) | Environments | Read the end of what one part of the environment printed: the app, one of its services by name, or tests for the last test run. |
| [`e2e_test`](environments.md#e2e_test) | Environments | Run the project's end-to-end tests against the running app: dotnet test of the test project the manifest names, in the environment's network, with the settin… |
| [`e2e_diagnose`](diagnostics.md#e2e_diagnose) | Diagnostics | Say whether this server can raise environments at all: Nomad's version, the network environments run in, and whether the restore proxy is on. |
