# Environments

## <code>e2e_<wbr>up</code>

Raise the task's environment from the working copy and wait until the app answers its health check.
The project describes it in .snail/environment.json: the app's project, port and health path, the services it
needs (a database, a broker) by image, and its end-to-end tests. The app is built and run with .NET in a network
of its own, beside its services; only NuGet is reachable from it. The first raise restores packages and builds,
which takes minutes; the wait is up to 300 seconds. Raise again after changing the code: the environment is
replaced by one running the current files. The state is one of ready, starting, failed or stopped; when it is failed, the answer carries
the end of the app's output with the reason.

*only adds · repeat-safe · stays on this machine.*

Takes no parameters.

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {}
}
```

</details>

## <code>e2e_<wbr>status</code>

Say where the task's environment stands now without waiting: ready, starting, failed or stopped, each part's task state and restarts.

*read-only · repeat-safe · stays on this machine.*

Takes no parameters.

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {}
}
```

</details>

## <code>e2e_<wbr>down</code>

Stop the task's environment and its test runs. An environment unused for 60 minutes is stopped on its own.

*may overwrite or delete · repeat-safe · stays on this machine.*

Takes no parameters.

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {}
}
```

</details>

## <code>e2e_<wbr>http</code>

Send one HTTP request to the running app, as a client inside its network would, and get back the status, the
headers and the body. The body is cut at 16384 bytes. Raise the environment first with e2e_up.

*only adds · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `method` | `string` | yes | — | GET, POST, PUT, PATCH, DELETE or HEAD. |
| `path` | `string` | yes | — | The path and query of the app, starting with /, such as /orders?status=new. |
| `body` | `string?` | — | `null` | The request body as text, such as a JSON document; empty sends none. |
| `contentType` | `string?` | — | `null` | The media type of the body; application/json when not given. |
| `headers` | `array?` | — | `null` | Extra request headers, each as "Name: value", such as "Authorization: Bearer abc". |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "method": {
      "description": "GET, POST, PUT, PATCH, DELETE or HEAD.",
      "type": "string"
    },
    "path": {
      "description": "The path and query of the app, starting with /, such as /orders?status=new.",
      "type": "string"
    },
    "body": {
      "description": "The request body as text, such as a JSON document; empty sends none.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "contentType": {
      "description": "The media type of the body; application/json when not given.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "headers": {
      "description": "Extra request headers, each as \u0022Name: value\u0022, such as \u0022Authorization: Bearer abc\u0022.",
      "type": [
        "array",
        "null"
      ],
      "items": {
        "type": [
          "string",
          "null"
        ]
      },
      "default": null
    }
  },
  "required": [
    "method",
    "path"
  ]
}
```

</details>

## <code>e2e_<wbr>logs</code>

Read the end of what one part of the environment printed: the app, one of its services by name, or tests for the
last test run. Both standard output and standard error come back, each up to 8192 bytes unless asked for more,
at most 65536.

*read-only · repeat-safe · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `part` | `string` | yes | — | app, a service name from the manifest, or tests. |
| `bytes` | `integer?` | — | `null` | How many bytes of the end of each stream to read; 8192 when not given, at most 65536. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "part": {
      "description": "app, a service name from the manifest, or tests.",
      "type": "string"
    },
    "bytes": {
      "description": "How many bytes of the end of each stream to read; 8192 when not given, at most 65536.",
      "type": [
        "integer",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "part"
  ]
}
```

</details>

## <code>e2e_<wbr>test</code>

Run the project's end-to-end tests against the running app: dotnet test of the test project the manifest names,
in the environment's network, with the settings that point it at the app. The run gets up to 900 seconds,
restore and build included. The outcome is one of passed, failed, buildFailed, noTests or timedOut; the answer counts the tests, names up to 20 that
failed, and carries the end of the output, up to 16384 bytes.

*only adds · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `filter` | `string?` | — | `null` | A dotnet test filter, such as FullyQualifiedName~Orders; empty runs every test. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "filter": {
      "description": "A dotnet test filter, such as FullyQualifiedName~Orders; empty runs every test.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  }
}
```

</details>

