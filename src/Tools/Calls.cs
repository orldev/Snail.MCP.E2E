using System.Net.Http.Headers;
using Snail.MCP.E2E.Application;

namespace Snail.MCP.E2E.Tools;

/// <summary>A request to the app made from the arguments a tool took.</summary>
/// <remarks>
/// Every argument the request is made of is checked here, before anything is sent: a media type that does not parse
/// used to throw from the adapter, and a header without a colon, or one the request cannot carry, was dropped without a
/// word — the model believed it had sent what it had not.
/// </remarks>
public static class Calls
{
    /// <summary>The body's media type when the caller names none.</summary>
    public const string DefaultContentType = "application/json";

    /// <summary>The request, with JSON as the body's type when none is named and each header split at its first colon; or why it cannot be sent.</summary>
    public static Answer<HttpCall> Of(string method, string path, string? body, string? contentType, string[]? headers)
    {
        string? sent = string.IsNullOrEmpty(body) ? null : body;
        string type = string.IsNullOrWhiteSpace(contentType) ? DefaultContentType : contentType.Trim();

        if (!MediaTypeHeaderValue.TryParse(type, out _))
            return Answer.Failed<HttpCall>(Messages.BadContentType(type));

        var pairs = new List<KeyValuePair<string, string>>();
        foreach (string header in headers ?? [])
        {
            if (HeaderOf(header, hasBody: sent is not null) is not { } pair)
                return Answer.Failed<HttpCall>(Messages.BadHeader(header));

            pairs.Add(pair);
        }

        var call = new HttpCall(method.Trim(), path.Trim(), sent, type, pairs);

        return Answer.Ok(call);
    }

    /// <summary>The header split at its first colon, when its name is one the request can carry; null otherwise.</summary>
    /// <remarks>A content header, such as Content-Language, goes with the body and needs one.</remarks>
    private static KeyValuePair<string, string>? HeaderOf(string header, bool hasBody)
    {
        string[] parts = header.Split(':', 2);
        if (parts.Length != 2 || parts[0].Trim().Length == 0)
            return null;

        string name = parts[0].Trim();
        string value = parts[1].Trim();
        using var probe = new HttpRequestMessage { Content = new ByteArrayContent([]) };

        bool isCarried = probe.Headers.TryAddWithoutValidation(name, value) || (hasBody && probe.Content.Headers.TryAddWithoutValidation(name, value));

        return isCarried ? KeyValuePair.Create(name, value) : null;
    }
}
