using System.Text.Json.Serialization;

namespace Snail.MCP.E2E.Adapters;

/// <summary>The manifest as its JSON holds it, every part optional, before the rules judge it.</summary>
public sealed record ManifestFile(AppFile? App, Dictionary<string, ServiceFile>? Services, TestFile? Tests)
{
    /// <summary>The manifest with defaults filled in: port 8080, health at /health.</summary>
    public EnvironmentManifest ToManifest() =>
        new(
            new AppPart(App?.Project ?? string.Empty, App?.Port ?? 8080, App?.Health ?? "/health", App?.Env ?? []),
            (Services ?? []).ToDictionary(service => service.Key, service => service.Value.ToPart(), StringComparer.Ordinal),
            Tests is null ? null : new TestPart(Tests.Project ?? string.Empty, Tests.Env ?? []));
}

/// <summary>The app part of the manifest file.</summary>
public sealed record AppFile(string? Project, int? Port, string? Health, Dictionary<string, string>? Env);

/// <summary>One service of the manifest file.</summary>
public sealed record ServiceFile(string? Image, int? Port, Dictionary<string, string>? Env, List<string>? Args)
{
    /// <summary>The service with empty settings and arguments where the file names none.</summary>
    public ServicePart ToPart() => new(Image ?? string.Empty, Port, Env ?? [], Args ?? []);
}

/// <summary>The tests part of the manifest file.</summary>
public sealed record TestFile(string? Project, Dictionary<string, string>? Env);

/// <summary>The manifest's JSON, read without reflection.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(ManifestFile))]
public sealed partial class ManifestJson : JsonSerializerContext;
