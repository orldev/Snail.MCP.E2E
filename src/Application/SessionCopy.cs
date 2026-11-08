using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Configuration;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.MCP.E2E.Application;

/// <summary>The working copy this session works on, read from the address it opened, and the task it belongs to.</summary>
/// <remarks>
/// The platform names a step's copy in the address — <c>/mcp?root=/workspaces/&lt;task&gt;/step-3</c> — the same way it
/// does for its file, git and Roslyn servers, and passes nothing else about the task. The task is the copy's first folder
/// under the workspaces root (<see cref="WorkspaceCopy"/>): every step of a task lands in the same environment. A copy
/// outside the root is refused, whatever the address says, because the environment mounts it from a volume that holds
/// nothing else.
/// </remarks>
public sealed class SessionCopy(ISessionAddress address, IOptions<E2EOptions> options)
{
    /// <summary>What the address calls the working copy.</summary>
    public const string Parameter = SessionRoots.Parameter;

    /// <summary>The copy and its task's environment, or why this session has none.</summary>
    public Answer<WorkspacePlace<TaskEnvironment>> Current()
    {
        string? root = address.Values(Parameter).FirstOrDefault();

        return Of(root, options.Value.WorkspacesRoot);
    }

    /// <summary>The copy a root names under the workspaces folder, or why it names none, in this server's words.</summary>
    /// <remarks>The refusal is typed by the SDK's reason — <c>noRoot</c>, <c>outside</c>, <c>malformed</c>, <c>notATask</c> — so a program tells a missing copy from a wrong one.</remarks>
    public static Answer<WorkspacePlace<TaskEnvironment>> Of(string? root, string workspaces)
    {
        var copy = WorkspaceCopy.Of(root, workspaces, TaskEnvironment.Of);

        return copy.ToAnswer(refusal => refusal == CopyRefusal.NoRoot
            ? Answer.Failed(Messages.NoCopy, Messages.NoCopyHint)
            : Answer.Failed(Messages.CopyOutside(root ?? string.Empty, workspaces)));
    }
}
