using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    [Fact]
    public void Split_HeadWithABodyAfterIt_KeepsTheBodyToForward()
    {
        var head = RestoreProxy.Split("POST http://api.nuget.org/x HTTP/1.1\r\nContent-Length: 2\r\n\r\nhi"u8);

        Assert.EndsWith("\r\n\r\n", Encoding.ASCII.GetString(head!.Bytes), StringComparison.Ordinal);
        Assert.Equal("hi", Encoding.ASCII.GetString(head.Rest));
    }

    [Fact]
    public void Split_HeadNotEndedYet_IsNull()
    {
        Assert.Null(RestoreProxy.Split("CONNECT api.nuget.org:443 HTTP/1.1\r\nHost: x\r\n"u8));
    }

    /// <summary>A client that connected and sent nothing used to hold its socket for as long as it liked.</summary>
    [Fact]
    public async Task Serve_ClientThatSendsNoHead_IsClosedOnceTheHeadTimeoutPasses()
    {
        await using var proxy = await ProxyAsync(maxConnections: 4);
        using var client = await ConnectAsync(proxy.Port);

        int got = await client.GetStream().ReadAsync(new byte[16]).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, got);
    }

    [Fact]
    public async Task Serve_OneConnectionPastTheLimit_IsAnswered503()
    {
        await using var proxy = await ProxyAsync(maxConnections: 1);
        using var holding = await ConnectAsync(proxy.Port);
        await Task.Delay(100);
        using var extra = await ConnectAsync(proxy.Port);

        string answer = await ReadAsync(extra);

        Assert.StartsWith("HTTP/1.1 503", answer, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CONNECT github.com:443 HTTP/1.1\r\nHost: github.com:443\r\n\r\n", "HTTP/1.1 403")]
    [InlineData("GET /v3/index.json HTTP/1.1\r\nHost: api.nuget.org\r\n\r\n", "HTTP/1.1 400")]
    public async Task Serve_RequestNotForNuGet_IsRefused(string request, string expected)
    {
        await using var proxy = await ProxyAsync(maxConnections: 4);
        using var client = await ConnectAsync(proxy.Port);

        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(request));

        Assert.StartsWith(expected, await ReadAsync(client), StringComparison.Ordinal);
    }

    private static async Task<Running> ProxyAsync(int maxConnections)
    {
        var options = Options.Create(new E2EOptions { RestoreProxyPort = Ports.Free() });
        var proxy = new RestoreProxy(options, NullLogger<RestoreProxy>.Instance) { MaxConnections = maxConnections, HeadTimeout = TimeSpan.FromMilliseconds(300) };
        await proxy.StartAsync(CancellationToken.None);

        while (proxy.Port == 0)
            await Task.Delay(10);

        return new Running(proxy);
    }

    private static async Task<TcpClient> ConnectAsync(int port)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);

        return client;
    }

    private static async Task<string> ReadAsync(TcpClient client)
    {
        var buffer = new byte[256];
        int got = await client.GetStream().ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        return Encoding.ASCII.GetString(buffer, 0, got);
    }

    private sealed class Running(RestoreProxy proxy) : IAsyncDisposable
    {
        public int Port => proxy.Port;

        public async ValueTask DisposeAsync()
        {
            await proxy.StopAsync(CancellationToken.None);
            proxy.Dispose();
        }
    }
}
