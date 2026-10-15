using System.Text.Json;
using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Application.Ports;
using Snail.MCP.E2E.Configuration;

namespace Snail.MCP.E2E.Adapters;

/// <summary>The manifest read from the working copy on the volume this server sees it on.</summary>
public sealed class FileManifests(IOptions<E2EOptions> options) : IManifests
{
    /// <summary>Largest manifest read; a bigger file is not a manifest.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <inheritdoc />
    public async Task<Answer<EnvironmentManifest>> ReadAsync(string copy, CancellationToken cancellationToken)
    {
        string relative = options.Value.ManifestPath;
        var file = new FileInfo(Path.Combine(copy, relative));

        if (!file.Exists)
            return Answer.Failed<EnvironmentManifest>(Messages.NoManifest(relative), Messages.NoManifestHint);

        if (file.Length > MaxBytes)
            return Answer.Failed<EnvironmentManifest>(Messages.BadManifest(relative, $"it is {file.Length} bytes, more than {MaxBytes}"));

        ManifestFile? read;
        try
        {
            await using var stream = file.OpenRead();
            read = await JsonSerializer.DeserializeAsync(stream, ManifestJson.Default.ManifestFile, cancellationToken);
        }
        catch (JsonException exception)
        {
            return Answer.Failed<EnvironmentManifest>(Messages.BadManifest(relative, exception.Message), Messages.NoManifestHint);
        }

        if (read is null)
            return Answer.Failed<EnvironmentManifest>(Messages.BadManifest(relative, "it is empty"), Messages.NoManifestHint);

        var manifest = read.ToManifest();
        var problems = ManifestRules.Problems(manifest);

        return problems.Count == 0 ? Answer.Ok(manifest) : Answer.Failed<EnvironmentManifest>(Messages.ManifestProblems(relative, problems));
    }
}
