using System.Diagnostics;
using System.Text;
using Snail.MCP.E2E.Application.Ports;

namespace Snail.MCP.E2E.Adapters;

/// <summary>Requests to an app over the environment's network, never through a proxy: the app is on the same network as this server.</summary>
public sealed class HttpAppCalls(IHttpClientFactory clients) : IAppCalls
{
    /// <summary>Name of the HTTP client this adapter is given.</summary>
    public const string Client = "app";

    /// <summary>How long one request to the app may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public async Task<HttpExchange?> SendAsync(Uri app, HttpCall call, int limit, CancellationToken cancellationToken)
    {
        var target = AppAddress.Of(app, call.Path) ?? throw new ArgumentException($"\"{call.Path}\" leaves the app at {app}.", nameof(call));
        using var request = new HttpRequestMessage(new HttpMethod(call.Method), target);

        if (call.Body is { } body)
            request.Content = new StringContent(body, Encoding.UTF8, call.ContentType);

        foreach (var header in call.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var watch = Stopwatch.StartNew();
        try
        {
            using var client = clients.CreateClient(Client);
            using var answer = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var (text, isCut) = await ReadAsync(answer.Content, limit, cancellationToken);
            IReadOnlyDictionary<string, string> headers = answer.Headers.Concat(answer.Content.Headers)
                .ToDictionary(header => header.Key, header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase);

            return new HttpExchange((int)answer.StatusCode, headers, text, isCut, Math.Round(watch.Elapsed.TotalMilliseconds, 1));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>The body as text up to <paramref name="limit"/> bytes, and whether more was there.</summary>
    private static async Task<(string Text, bool IsCut)> ReadAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
            return (string.Empty, false);

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[limit + 1];
        int read = 0;

        while (read < buffer.Length)
        {
            int got = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (got == 0)
                break;

            read += got;
        }

        bool isCut = read > limit;

        return (Encoding.UTF8.GetString(buffer, 0, Math.Min(read, limit)), isCut);
    }
}
