using System.Text.Json;
using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Application.Ports;
using Snail.MCP.E2E.Configuration;

namespace Snail.MCP.E2E.Adapters;

/// <summary>The manifest read from the working copy on the volume this server sees it on.</summary>
/// <remarks>
/// <para>
/// The copy is written by a model, and this server sees every task's copy: whatever the model makes of the manifest's
/// path, the server opens. A link — the manifest's or its folder's — is refused, since through it the server read
/// another task's manifest and quoted its values back to the task that made the link.
/// </para>
/// <para>
/// A named pipe, a socket or a device reports a length of 0, and opening a pipe blocks until a writer comes, out of
/// reach of any cancellation: an empty file is refused before it is opened. The read runs off the caller's thread and
/// within <see cref="ReadTimeout"/>, so a file swapped for a pipe between the look and the open costs a pool thread
/// rather than the call, and <c>e2e_down</c> still reaches the environment.
/// </para>
/// </remarks>
public sealed class FileManifests(IOptions<E2EOptions> options) : IManifests
{
    /// <summary>Largest manifest read; a bigger file is not a manifest.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>The longest a manifest of at most <see cref="MaxBytes"/> takes to read from a local volume, and more.</summary>
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task<Answer<EnvironmentManifest>> ReadAsync(string copy, CancellationToken cancellationToken)
    {
        string relative = options.Value.ManifestPath;
        var file = new FileInfo(Path.Combine(copy, relative));

        if (IsLinkOnTheWay(copy, relative))
            return Answer.Failed(Messages.BadManifest(relative, "it is a link, and the manifest is read only from a file of the copy itself"), Messages.NoManifestHint);

        if (!file.Exists)
            return Answer.Failed(Messages.NoManifest(relative), Messages.NoManifestHint);

        if (file.Length == 0)
            return Answer.Failed(Messages.BadManifest(relative, "it is empty or not a regular file"), Messages.NoManifestHint);

        if (file.Length > MaxBytes)
            return Answer.Failed(Messages.BadManifest(relative, $"it is {file.Length} bytes, more than {MaxBytes}"));

        ManifestFile? read;
        try
        {
            read = await Task.Run(() => DeserializeAsync(file, cancellationToken), cancellationToken).WaitAsync(ReadTimeout, cancellationToken);
        }
        catch (JsonException exception)
        {
            return Answer.Failed(Messages.BadManifest(relative, exception.Message), Messages.NoManifestHint);
        }
        catch (TimeoutException)
        {
            return Answer.Failed(Messages.BadManifest(relative, $"it was not read in {ReadTimeout.TotalSeconds:0} seconds, which a regular file is"), Messages.NoManifestHint);
        }

        if (read is null)
            return Answer.Failed(Messages.BadManifest(relative, "it is empty"), Messages.NoManifestHint);

        var manifest = read.ToManifest();
        var problems = ManifestRules.Problems(manifest);

        return problems.Count == 0 ? Answer.Ok(manifest) : Answer.Failed(Messages.ManifestProblems(relative, problems));
    }

    /// <summary>Whether the manifest, or a folder between the copy and it, is a link.</summary>
    private static bool IsLinkOnTheWay(string copy, string relative)
    {
        string path = copy;

        foreach (string part in relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, part);

            if (new FileInfo(path).LinkTarget is not null)
                return true;
        }

        return false;
    }

    private static async Task<ManifestFile?> DeserializeAsync(FileInfo file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenRead();

        return await JsonSerializer.DeserializeAsync(stream, ManifestJson.Default.ManifestFile, cancellationToken);
    }
}
