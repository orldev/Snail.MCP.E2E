namespace Snail.MCP.E2E.Domain;

/// <summary>What makes a manifest one an environment can be raised from, each broken rule said in words.</summary>
public static class ManifestRules
{
    /// <summary>Most services one environment may hold.</summary>
    public const int MaxServices = 8;

    /// <summary>The longest name a service may take: its host is the environment's name, two dashes and this name.</summary>
    public const int MaxServiceName = 20;

    /// <summary>The rules the manifest breaks; empty when it can be raised.</summary>
    public static IReadOnlyList<string> Problems(EnvironmentManifest manifest)
    {
        var services = manifest.Services.Keys.ToList();
        List<string> problems = [];

        problems.AddRange(ProjectProblems("app.project", manifest.App.Project));

        if (manifest.App.Port is < 1 or > 65535)
            problems.Add($"app.port {manifest.App.Port} is not a port");

        if (!AppAddress.IsAppPath(manifest.App.Health))
            problems.Add($"app.health \"{manifest.App.Health}\" is not a path of the app: it starts with / and names no other host");

        if (services.Count > MaxServices)
            problems.Add($"{services.Count} services; an environment holds at most {MaxServices}");

        problems.AddRange(services.Where(name => !IsServiceName(name)).Select(name => $"\"{name}\" cannot name a service: up to {MaxServiceName} lower-case letters and digits, starting with a letter, in groups joined by single dashes, not app or tests"));
        problems.AddRange(manifest.Services.Where(service => string.IsNullOrWhiteSpace(service.Value.Image)).Select(service => $"services.{service.Key} names no image"));
        problems.AddRange(UnknownMarks("app.env", manifest.App.Env, services));

        foreach (var service in manifest.Services)
            problems.AddRange(UnknownMarks($"services.{service.Key}.env", service.Value.Env, services));

        if (manifest.Tests is { } tests)
        {
            problems.AddRange(ProjectProblems("tests.project", tests.Project));
            problems.AddRange(UnknownMarks("tests.env", tests.Env, services));
        }

        return problems;
    }

    /// <summary>A project path stays inside the working copy and names a project file.</summary>
    private static IEnumerable<string> ProjectProblems(string where, string project)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            yield return $"{where} is empty";
            yield break;
        }

        if (project.StartsWith('/') || project.Contains('\\', StringComparison.Ordinal) || project.Split('/').Contains(".."))
            yield return $"{where} \"{project}\" leaves the working copy: a relative path with forward slashes";

        if (!project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && !project.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
            yield return $"{where} \"{project}\" is not a .csproj or .fsproj";
    }

    private static IEnumerable<string> UnknownMarks(string where, IReadOnlyDictionary<string, string> settings, IReadOnlyCollection<string> services) =>
        settings.SelectMany(setting => Placeholders.Unknown(setting.Value, services).Select(mark => $"{where}.{setting.Key} names {{{mark}}}, which is neither app nor a service"));

    private static bool IsServiceName(string name) =>
        name is [var first, ..] && char.IsAsciiLetterLower(first) && name.Length <= MaxServiceName && TaskEnvironment.IsLabel(name) && name is not (Parts.App or Parts.Tests);
}
