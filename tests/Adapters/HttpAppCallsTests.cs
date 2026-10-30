using System.Net;
using Snail.MCP.E2E.Adapters;

namespace Snail.MCP.E2E.Tests.Adapters;

public sealed class HttpAppCallsTests
{
    private static readonly Uri App = new("http://e2e-0c2b9e1f--app:8080/");

    private readonly Recorder _recorder = new();

    private HttpAppCalls Calls() => new(new Clients(_recorder));

    private static HttpCall Call(string method, string path, string? body = null) =>
        new(method, path, body, "application/json", [KeyValuePair.Create("X-Trace", "1")]);

    /// <summary>Joined with its slash cut, this path was once the absolute address of Nomad's API.</summary>
    [Fact]
    public async Task SendAsync_PathSpellingAnAddress_IsSentToTheApp()
    {
        await Calls().SendAsync(App, Call("POST", "/http://nomad:4646/v1/jobs", "{}"), 100, CancellationToken.None);

        Assert.Equal("e2e-0c2b9e1f--app:8080", Assert.Single(_recorder.Sent).Authority);
    }

    [Fact]
    public async Task SendAsync_PathNamingAHostOfItsOwn_SendsNothing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Calls().SendAsync(App, Call("GET", "//nomad:4646/v1/jobs"), 100, CancellationToken.None));

        Assert.Empty(_recorder.Sent);
    }

    [Fact]
    public async Task SendAsync_BodyPastTheLimit_IsCutAndSaysSo()
    {
        _recorder.Body = new string('x', 50);

        var exchange = await Calls().SendAsync(App, Call("GET", "/orders"), 10, CancellationToken.None);

        Assert.Equal((10, true), (exchange!.Body.Length, exchange.IsBodyCut));
    }

    [Fact]
    public async Task SendAsync_AppThatRefusesTheConnection_IsNull()
    {
        _recorder.Failure = new HttpRequestException("Connection refused");

        Assert.Null(await Calls().SendAsync(App, Call("GET", "/orders"), 10, CancellationToken.None));
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public List<Uri> Sent { get; } = [];

        public string Body { get; set; } = "ok";

        public Exception? Failure { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Failure is { } failure)
                throw failure;

            Sent.Add(request.RequestUri!);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
        }
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
