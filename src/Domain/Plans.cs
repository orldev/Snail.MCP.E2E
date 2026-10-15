namespace Snail.MCP.E2E.Domain;

/// <summary>An environment to raise: its name, the working copy it runs from, and what the manifest holds.</summary>
/// <remarks><paramref name="Copy"/> is the copy's path under the workspaces root, the same for this server and for Nomad's volume.</remarks>
public sealed record EnvironmentPlan(TaskEnvironment Name, string Copy, EnvironmentManifest Manifest);

/// <summary>A test run to start in an environment: which copy, the tests the manifest names, and a filter when the caller gave one.</summary>
public sealed record TestPlan(TaskEnvironment Name, string Copy, EnvironmentManifest Manifest, TestPart Tests, string? Filter);

/// <summary>An environment's job as Nomad runs it: each part's task state, or null when no job runs.</summary>
public sealed record EnvironmentRun(string? Allocation, IReadOnlyList<PartState> Parts)
{
    /// <summary>The app's state, or null while Nomad has not started it.</summary>
    public PartState? App => Parts.FirstOrDefault(part => part.Name == Domain.Parts.App);
}

/// <summary>How a test run ended, as the job left it: its output, and whether it ran out of time.</summary>
public sealed record TestRun(string Output, bool HasTimedOut);
