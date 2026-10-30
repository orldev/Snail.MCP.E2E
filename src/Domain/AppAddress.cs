namespace Snail.MCP.E2E.Domain;

/// <summary>Where a request to the app goes: a path of the app, never another host.</summary>
/// <remarks>
/// The path was once joined with its leading slash cut, and <c>/http://nomad:4646/v1/jobs</c> became the absolute address of
/// Nomad's API, which registers any job it is sent. The path is now joined as it is — from the root of the app — and the
/// joined address is checked, not the text: it keeps the app's scheme, host and port, or the path is refused. A path
/// starting with <c>//</c> names a host of its own and is refused by that check.
/// </remarks>
public static class AppAddress
{
    private static readonly Uri Probe = new("http://app:1/");

    /// <summary>The address a path reaches on the app, or null when the path would leave the app for another host.</summary>
    public static Uri? Of(Uri app, string path)
    {
        if (!path.StartsWith('/') || !Uri.TryCreate(app, path, out var target))
            return null;

        bool isSameScheme = string.Equals(target.Scheme, app.Scheme, StringComparison.Ordinal);
        bool isSameAuthority = string.Equals(target.Authority, app.Authority, StringComparison.OrdinalIgnoreCase);

        return isSameScheme && isSameAuthority ? target : null;
    }

    /// <summary>Whether a path stays on whatever app it is joined to.</summary>
    public static bool IsAppPath(string path) => Of(Probe, path) is not null;
}
