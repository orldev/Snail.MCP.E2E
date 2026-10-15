namespace Snail.MCP.E2E.Application.Ports;

/// <summary>Requests to an app inside its environment's network.</summary>
public interface IAppCalls
{
    /// <summary>Sends the request and reads the answer up to the limit; null when the app did not answer at all.</summary>
    Task<HttpExchange?> SendAsync(Uri app, HttpCall call, int limit, CancellationToken cancellationToken);
}
