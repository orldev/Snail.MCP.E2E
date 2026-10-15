namespace Snail.MCP.E2E.Tools;

/// <summary>A request to the app made from the arguments a tool took.</summary>
public static class Calls
{
    /// <summary>The request, with JSON as the body's type when none is named and each header split at its first colon.</summary>
    public static HttpCall Of(string method, string path, string? body, string? contentType, string[]? headers)
    {
        IReadOnlyList<KeyValuePair<string, string>> pairs =
        [
            .. (headers ?? [])
                .Select(header => header.Split(':', 2))
                .Where(parts => parts.Length == 2 && parts[0].Trim().Length > 0)
                .Select(parts => KeyValuePair.Create(parts[0].Trim(), parts[1].Trim()))
        ];

        return new HttpCall(method.Trim(), path.Trim(), string.IsNullOrEmpty(body) ? null : body, contentType ?? "application/json", pairs);
    }
}
