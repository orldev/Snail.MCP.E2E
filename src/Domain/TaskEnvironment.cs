using System.Text.RegularExpressions;

namespace Snail.MCP.E2E.Domain;

/// <summary>The name of one task's environment, and the host names its parts answer at inside the environment's network.</summary>
/// <remarks>
/// <para>
/// One environment per task: the name comes from the task's id, so every step of the task reaches the environment the
/// first one raised, and a second raise replaces it rather than standing beside it.
/// </para>
/// <para>
/// A part is joined to the name with two dashes, which neither a task's id nor a service's name may hold: with one dash
/// the task <c>foo-tests</c> was the test run of the task <c>foo</c>, whose next run purged it, and the service
/// <c>b-app</c> of the task <c>a</c> answered at the app's host of the task <c>a-b</c>. A host is a DNS label of at most
/// 63 characters — <c>e2e-</c>, the id, <c>--</c> and a service name of up to <see cref="ManifestRules.MaxServiceName"/>
/// — so the id is held to <see cref="MaxTaskId"/>; the platform's ids are GUIDs of 32.
/// </para>
/// </remarks>
public readonly partial record struct TaskEnvironment(string Value)
{
    /// <summary>What every environment's name begins with, so the server finds its own among Nomad's jobs.</summary>
    public const string Prefix = "e2e-";

    /// <summary>What joins a part to the environment's name.</summary>
    public const string Joint = "--";

    /// <summary>The longest task id whose hosts still fit a DNS label.</summary>
    public const int MaxTaskId = 63 - 4 - 2 - ManifestRules.MaxServiceName;

    /// <summary>The environment of the task, or null for an id that cannot name a host.</summary>
    public static TaskEnvironment? Of(string taskId) =>
        taskId.Length <= MaxTaskId && Label().IsMatch(taskId) ? new($"{Prefix}{taskId}") : null;

    /// <summary>The host a part of the environment answers at: <c>e2e-…--app</c>, <c>e2e-…--mongo</c>.</summary>
    public string HostOf(string part) => $"{Value}{Joint}{part}";

    /// <summary>The host of the app.</summary>
    public string App => HostOf(Parts.App);

    /// <summary>The batch job a test run of this environment goes in.</summary>
    public string Tests => HostOf(Parts.Tests);

    /// <summary>Whether a Nomad job is an environment of this server rather than one of its test runs.</summary>
    public static bool IsEnvironment(string job) =>
        job.StartsWith(Prefix, StringComparison.Ordinal) && !job.Contains(Joint, StringComparison.Ordinal);

    /// <summary>Whether a Nomad job is a test run of an environment of this server.</summary>
    public static bool IsTestRun(string job) =>
        job.StartsWith(Prefix, StringComparison.Ordinal) && job.EndsWith($"{Joint}{Parts.Tests}", StringComparison.Ordinal);

    /// <summary>Whether a name is lower-case letters and digits in groups joined by single dashes, as a DNS label is.</summary>
    public static bool IsLabel(string name) => Label().IsMatch(name);

    /// <inheritdoc />
    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Label();
}

/// <summary>The names an environment keeps for itself: no service may take them.</summary>
public static class Parts
{
    /// <summary>The app's task and host.</summary>
    public const string App = "app";

    /// <summary>The test run's job and task.</summary>
    public const string Tests = "tests";
}
