namespace Snail.MCP.E2E.Application.Ports;

/// <summary>Where environments run: raised, read, stopped and asked for their logs.</summary>
public interface IEnvironmentJobs
{
    /// <summary>Raises the environment, or replaces the one running under its name.</summary>
    Task RaiseAsync(EnvironmentPlan plan, CancellationToken cancellationToken);

    /// <summary>The environment's parts as they run; null when no environment runs under the name.</summary>
    Task<EnvironmentRun?> RunAsync(TaskEnvironment name, CancellationToken cancellationToken);

    /// <summary>Stops the environment and its test runs; answers whether one ran.</summary>
    Task<bool> HasStoppedAsync(TaskEnvironment name, CancellationToken cancellationToken);

    /// <summary>The end of one part's output; empty when the part has said nothing.</summary>
    Task<string> LogsAsync(TaskEnvironment name, string part, bool isError, int bytes, CancellationToken cancellationToken);

    /// <summary>Runs the tests in the environment's network until they end or the time is up.</summary>
    Task<TestRun> TestAsync(TestPlan plan, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Every environment this server raised that still runs.</summary>
    Task<IReadOnlyList<TaskEnvironment>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The version of the scheduler that answered, or why none did.</summary>
    Task<Answer<string>> ReachAsync(CancellationToken cancellationToken);
}
