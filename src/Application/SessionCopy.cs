using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Configuration;

namespace Snail.MCP.E2E.Application;

/// <summary>The working copy this session works on, read from the address it opened, and the task it belongs to.</summary>
/// <remarks>
/// The platform names a step's copy in the address — <c>/mcp?root=/workspaces/&lt;task&gt;/step-3</c> — the same way it
/// does for its file, git and Roslyn servers, and passes nothing else about the task. The task is the copy's first folder
/// under the workspaces root: every step of a task lands in the same environment. A copy outside the root is refused,
/// whatever the address says, because the environment mounts it from a volume that holds nothing else.
/// </remarks>
public sealed class SessionCopy(IHttpContextAccessor requests, IOptions<E2EOptions> options)
{
    /// <summary>What the address calls the working copy.</summary>
    public const string Parameter = "root";

    /// <summary>The copy and its task's environment, or why this session has none.</summary>
    public Answer<CopyPlace> Current()
    {
        string? root = requests.HttpContext?.Request.Query[Parameter].FirstOrDefault();

        return Of(root, options.Value.WorkspacesRoot);
    }

    /// <summary>The copy a root names under the workspaces folder, or why it names none.</summary>
    public static Answer<CopyPlace> Of(string? root, string workspaces)
    {
        if (string.IsNullOrWhiteSpace(root))
            return Answer.Failed<CopyPlace>(Messages.NoCopy, Messages.NoCopyHint);

        string under = workspaces.TrimEnd('/');
        string relative = root.StartsWith($"{under}/", StringComparison.Ordinal) ? root[(under.Length + 1)..].Trim('/') : string.Empty;
        string[] segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            return Answer.Failed<CopyPlace>(Messages.CopyOutside(root, workspaces));

        if (TaskEnvironment.Of(segments[0]) is not { } name)
            return Answer.Failed<CopyPlace>(Messages.CopyOutside(root, workspaces));

        return Answer.Ok(new CopyPlace(name, $"{under}/{string.Join('/', segments)}"));
    }
}

/// <summary>A working copy as a path under the workspaces root, and the environment of its task.</summary>
public sealed record CopyPlace(TaskEnvironment Environment, string Path);
