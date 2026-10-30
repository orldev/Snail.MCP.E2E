using Snail.MCP.E2E.Configuration;
using Snail.MCP.E2E.Extensions;
using Snail.Sdk.Mcp.Http;

var app = McpHttpHost.CreateContainerBuilder(args, E2ESettings.Port())
    .ConfigureEnvironmentServer()
    .Build()
    .UseEnvironmentEndpoints();

await app.RunAsync();
