using System.Text;
using Snail.MCP.E2E.Adapters;
using Snail.MCP.E2E.Configuration;

namespace Snail.MCP.E2E.Tests.Adapters;

public sealed class RestoreProxyTests
{
    private static readonly string[] Hosts = new E2EOptions().RestoreHosts;

    [Theory]
    [InlineData("api.nuget.org", 443, true)]
    [InlineData("API.NUGET.ORG", 443, true)]
    [InlineData("globalcdn.nuget.org", 443, true)]
    [InlineData("api.nuget.org", 22, false)]
    [InlineData("github.com", 443, false)]
    [InlineData("nuget.org.evil.example", 443, false)]
    [InlineData("minio", 9000, false)]
    public void Allows_HostAndPort_OnlyNuGetOverHttpOrHttps(string host, int port, bool isAllowed)
    {
        Assert.Equal(isAllowed, RestoreProxy.Allows(Hosts, host, port));
    }

    [Fact]
    public void RequestOf_Tunnel_ReadsTheHostAndPort()
    {
        var request = RestoreProxy.Request.Of(Encoding.ASCII.GetBytes("CONNECT api.nuget.org:443 HTTP/1.1\r\nHost: api.nuget.org:443\r\n\r\n"));

        Assert.Equal(("api.nuget.org", 443, true), (request!.Host, request.Port, request.IsTunnel));
    }

    [Fact]
    public void RequestOf_PlainRequestWithoutAnAbsoluteAddress_IsNotAProxyRequest()
    {
        Assert.Null(RestoreProxy.Request.Of(Encoding.ASCII.GetBytes("GET /v3/index.json HTTP/1.1\r\nHost: api.nuget.org\r\n\r\n")));
    }
}
