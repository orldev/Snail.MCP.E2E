using Snail.MCP.E2E.Configuration;
using Snail.MCP.E2E.Extensions;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Http;

try
{
    var settings = E2ESettings.Read();
    var endpoint = McpEndpoint.ReadHttp(settings, E2ESettings.DefaultUrl, new McpEndpointOptions { Reach = McpReach.Unrestricted, Server = E2ESettings.Name });

    var app = McpHttpHost.CreateBuilder(args, endpoint, settings)
        .ConfigureEnvironmentServer()
        .Build()
        .UseEnvironmentEndpoints();

    await app.RunAsync();

    return 0;
}
catch (StartRefusedException refused)
{
    await Console.Error.WriteLineAsync(refused.Message);

    return 1;
}
