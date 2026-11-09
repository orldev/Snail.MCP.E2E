using System.Collections;
using Snail.MCP.E2E.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.E2E.Tests.Configuration;

/// <summary>Where the server listens, read as <c>Program</c> reads it: every interface by design, under launchd as in its container.</summary>
public sealed class EndpointTests
{
    private static readonly McpEndpointOptions Options = new() { Reach = McpReach.Unrestricted, Server = E2ESettings.Name };

    private static McpEndpoint Read(IDictionary variables) =>
        McpEndpoint.ReadHttp(E2ESettings.Read(null, variables), E2ESettings.DefaultUrl, Options);

    [Fact]
    public void ReadHttp_NoSettings_ListensOnEveryInterfaceAtTheDefaultPort()
    {
        var endpoint = Read(new Hashtable());

        Assert.True(endpoint.ListensOnEveryInterface);
        Assert.Equal(new E2EOptions().Port, endpoint.Url.Port);
    }

    /// <summary>The platform's stand and the launchd job name a port alone; it moves the port and keeps every interface.</summary>
    [Fact]
    public void ReadHttp_PortVariable_MovesThePortOnEveryInterface()
    {
        var endpoint = Read(new Hashtable { ["SNAIL_MCP_E2E_PORT"] = "9123" });

        Assert.True(endpoint.ListensOnEveryInterface);
        Assert.Equal(9123, endpoint.Url.Port);
    }

    /// <summary>
    /// Inside a container (DOTNET_RUNNING_IN_CONTAINER) Snail.Sdk opens every interface whatever the URL, since the
    /// published port does not reach the container's loopback; the test ran red in the Linux build image for that alone.
    /// </summary>
    [Fact]
    public void ReadHttp_LoopbackUrl_KeepsTheServerToLoopbackOutsideAContainer()
    {
        bool isInContainer = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);

        var endpoint = Read(new Hashtable { ["SNAIL_MCP_E2E_URL"] = "http://127.0.0.1:9124/" });

        Assert.Equal(isInContainer, endpoint.ListensOnEveryInterface);
        Assert.Equal("127.0.0.1", endpoint.Url.Host);
    }

    /// <summary>The launchd job names the transport too; HTTP is the only one, and stdio is refused at the start.</summary>
    [Fact]
    public void ReadHttp_StdioTransport_IsRefusedNamingTheVariable()
    {
        var refusal = Assert.Throws<StartRefusedException>(() => Read(new Hashtable { ["SNAIL_MCP_E2E_TRANSPORT"] = "stdio" }));

        Assert.Contains("SNAIL_MCP_E2E_TRANSPORT", refusal.Message, StringComparison.Ordinal);
    }
}
