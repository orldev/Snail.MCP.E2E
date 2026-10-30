namespace Snail.MCP.E2E.Domain;

/// <summary>An environment to raise: its name, the working copy it runs from, and what the manifest holds.</summary>
/// <remarks><paramref name="Copy"/> is the copy's path under the workspaces root, the same for this server and for Nomad's volume.</remarks>
public sealed record EnvironmentPlan(TaskEnvironment Name, string Copy, EnvironmentManifest Manifest);

/// <summary>A test run to start in an environment: which copy, the tests the manifest names, and a filter when the caller gave one.</summary>
public sealed record TestPlan(TaskEnvironment Name, string Copy, EnvironmentManifest Manifest, TestPart Tests, string? Filter);

/// <summary>The allocations that stood before a raise: the raise waits for one that is not among them.</summary>
/// <remarks>
/// Right after a raise Nomad has not placed the new allocation yet, and the old one still answers its health check: a
/// raise that looked at the newest allocation answered ready in no time, with the app it was replacing.
/// </remarks>
public sealed record RaiseMark(IReadOnlyList<string> Earlier)
{
    /// <summary>No raise to wait for: every allocation counts.</summary>
    public static RaiseMark None { get; } = new([]);
}

/// <summary>An environment's job as Nomad runs it: each part's task state, or null when no job runs.</summary>
public sealed record EnvironmentRun(string? Allocation, IReadOnlyList<PartState> Parts)
{
    /// <summary>The app's state, or null while Nomad has not started it.</summary>
    public PartState? App => Parts.FirstOrDefault(part => part.Name == Domain.Parts.App);
}

/// <summary>How a test run ended, as the job left it: its output, and whether it ran out of time.</summary>
public sealed record TestRun(string Output, bool HasTimedOut);
