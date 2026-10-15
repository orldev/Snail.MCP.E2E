namespace Snail.MCP.E2E.Domain;

/// <summary>A request to the app as the caller described it.</summary>
public sealed record HttpCall(string Method, string Path, string? Body, string ContentType, IReadOnlyList<KeyValuePair<string, string>> Headers);

/// <summary>What the app answered: status, headers, the body up to the server's limit, and how long it took.</summary>
public sealed record HttpExchange(int Status, IReadOnlyDictionary<string, string> Headers, string Body, bool IsBodyCut, double Milliseconds);
