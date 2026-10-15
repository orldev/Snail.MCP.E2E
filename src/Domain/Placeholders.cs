using System.Text.RegularExpressions;

namespace Snail.MCP.E2E.Domain;

/// <summary>A setting's value with its <c>{name}</c> marks filled: a service's host, or the app's own address.</summary>
/// <remarks>
/// The project cannot know the host names its environment gets — they carry the task's id — so its manifest names the
/// service and the server fills in where it lives.
/// </remarks>
public static partial class Placeholders
{
    /// <summary>The marks in a value that name nothing the environment holds.</summary>
    public static IReadOnlyList<string> Unknown(string value, IReadOnlyCollection<string> services) =>
        [.. Mark().Matches(value).Select(match => match.Groups[1].Value).Where(name => name != Parts.App && !services.Contains(name)).Distinct(StringComparer.Ordinal)];

    /// <summary>The value with every mark replaced; a mark naming nothing stays as it was.</summary>
    public static string Fill(string value, TaskEnvironment environment, AppPart app, IReadOnlyCollection<string> services) =>
        Mark().Replace(value, match => match.Groups[1].Value switch
        {
            Parts.App => $"http://{environment.App}:{app.Port}",
            var service when services.Contains(service) => environment.HostOf(service),
            _ => match.Value
        });

    /// <summary>Every setting of a part with its marks filled.</summary>
    public static IReadOnlyDictionary<string, string> Fill(IReadOnlyDictionary<string, string> settings, TaskEnvironment environment, AppPart app, IReadOnlyCollection<string> services) =>
        settings.ToDictionary(setting => setting.Key, setting => Fill(setting.Value, environment, app, services), StringComparer.Ordinal);

    [GeneratedRegex(@"\{([a-z][a-z0-9-]*)\}")]
    private static partial Regex Mark();
}
