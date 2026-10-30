using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Nomad;

namespace Snail.MCP.E2E.Tests.Adapters;

/// <summary>Nomad's HTTP API as a test scripts it: an answer per method and path, and every request it was sent.</summary>
/// <remarks>The adapter is tested through the real client of Snail.Toolkit.Nomad, so what reaches the adapter is what the client makes of an answer.</remarks>
internal sealed class FakeNomad : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _answers = new(StringComparer.Ordinal);

    public List<(string Method, string Path, string? Body)> Sent { get; } = [];

    /// <summary>Thrown for every request instead of an answer: Nomad not reachable.</summary>
    public Exception? Down { get; set; }

    public INomadClient Client()
    {
        var http = new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("http://nomad:4646/") };

        return new NomadClient(http, Options.Create(new NomadOptions { Address = "http://nomad:4646" }));
    }

    /// <summary>Answers a request with JSON; the last answer of a path is repeated once its queue runs out. A log path ending in <c>?stderr</c> answers the standard error stream alone.</summary>
    public FakeNomad Json(string method, string path, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        Answer(method, path, () => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public FakeNomad Text(string method, string path, string text) =>
        Answer(method, path, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/plain") });

    public FakeNomad Throw(string method, string path, Exception failure) =>
        Answer(method, path, () => throw failure);

    public FakeNomad Answer(string method, string path, Func<HttpResponseMessage> answer)
    {
        string key = $"{method} {path}";
        if (!_answers.TryGetValue(key, out var queue))
            _answers[key] = queue = new Queue<Func<HttpResponseMessage>>();

        queue.Enqueue(answer);

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Sent.Add((request.Method.Method, path, body));

        if (Down is { } down)
            throw down;

        string stream = request.RequestUri.Query.Contains("type=stderr", StringComparison.Ordinal) ? "?stderr" : string.Empty;
        bool isAnswered = _answers.TryGetValue($"{request.Method.Method} {path}{stream}", out var queue) || (stream.Length == 0 && _answers.TryGetValue($"{request.Method.Method} {path}", out queue));
        if (!isAnswered || queue is null)
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(string.Empty) };

        var answer = queue.Count > 1 ? queue.Dequeue() : queue.Peek();

        return answer();
    }
}
