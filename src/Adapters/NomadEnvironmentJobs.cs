using System.Net;
using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Application.Ports;
using Snail.MCP.E2E.Configuration;
using Snail.Toolkit.Nomad;
using Snail.Toolkit.Nomad.Models;

namespace Snail.MCP.E2E.Adapters;

/// <summary>TaskEnvironments as Nomad jobs: a service job per task, and a batch job per test run beside it.</summary>
public sealed class NomadEnvironmentJobs(INomadClient nomad, IOptions<E2EOptions> options, TimeProvider clock) : IEnvironmentJobs
{
    /// <summary>How often a test run is looked at while it runs.</summary>
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);

    /// <summary>Bytes of each stream of a test run read back; the summary is at the end, and the answer keeps less.</summary>
    private const int TestLogBytes = 65536;

    private static readonly string[] Ended = ["complete", "failed", "lost"];

    /// <inheritdoc />
    public async Task RaiseAsync(EnvironmentPlan plan, CancellationToken cancellationToken) =>
        await nomad.RegisterJobAsync(EnvironmentJob.Of(plan, options.Value), cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// A job Nomad gave up on is dead but not gone: its last allocation still holds the app's failure, and the caller is
    /// told that rather than that nothing runs.
    /// </remarks>
    public async Task<EnvironmentRun?> RunAsync(TaskEnvironment name, CancellationToken cancellationToken)
    {
        var job = await nomad.GetJobAsync(name.Value, cancellationToken);
        if (job is null || job.Stop)
            return null;

        var allocation = Latest(await nomad.ListAllocationsAsync(name.Value, cancellationToken));
        if (allocation is null)
            return new EnvironmentRun(null, []);

        IReadOnlyList<PartState> parts =
        [
            .. (allocation.TaskStates ?? new Dictionary<string, AllocationTaskState>())
                .Select(task => new PartState(task.Key, task.Value.State, task.Value.Restarts, task.Value.Failed))
                .OrderBy(part => part.Name, StringComparer.Ordinal)
        ];

        return new EnvironmentRun(allocation.Id, parts);
    }

    /// <inheritdoc />
    public async Task<bool> HasStoppedAsync(TaskEnvironment name, CancellationToken cancellationToken)
    {
        var stopped = await nomad.StopJobAsync(name.Value, purge: true, cancellationToken);
        await nomad.StopJobAsync(name.Tests, purge: true, cancellationToken);

        return stopped is not null;
    }

    /// <inheritdoc />
    public async Task<string> LogsAsync(TaskEnvironment name, string part, bool isError, int bytes, CancellationToken cancellationToken)
    {
        string job = part == Parts.Tests ? name.Tests : name.Value;
        var allocation = Latest(await nomad.ListAllocationsAsync(job, cancellationToken));
        if (allocation is null)
            return string.Empty;

        try
        {
            return await nomad.ReadLogsAsync(allocation.Id, part, isError ? LogType.Stderr : LogType.Stdout, bytes, cancellationToken);
        }
        catch (NomadException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.InternalServerError)
        {
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public async Task<TestRun> TestAsync(TestPlan plan, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await nomad.StopJobAsync(plan.Name.Tests, purge: true, cancellationToken);
        await nomad.RegisterJobAsync(EnvironmentJob.TestsOf(plan, Guid.NewGuid().ToString("N"), options.Value), cancellationToken);

        long started = clock.GetTimestamp();
        bool hasEnded = false;

        while (!hasEnded && clock.GetElapsedTime(started) < timeout)
        {
            await Task.Delay(Poll, clock, cancellationToken);
            var allocation = Latest(await nomad.ListAllocationsAsync(plan.Name.Tests, cancellationToken));
            hasEnded = allocation is not null && Ended.Contains(allocation.ClientStatus, StringComparer.Ordinal);
        }

        if (!hasEnded)
            await nomad.StopJobAsync(plan.Name.Tests, purge: false, cancellationToken);

        string stdout = await LogsAsync(plan.Name, Parts.Tests, false, TestLogBytes, cancellationToken);
        string stderr = await LogsAsync(plan.Name, Parts.Tests, true, TestLogBytes, cancellationToken);

        return new TestRun($"{stdout}{stderr}", !hasEnded);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaskEnvironment>> ListAsync(CancellationToken cancellationToken)
    {
        var jobs = await nomad.ListJobsAsync(TaskEnvironment.Prefix, cancellationToken);

        return [.. jobs.Where(job => !job.Stop && TaskEnvironment.IsEnvironment(job.Id)).Select(job => new TaskEnvironment(job.Id))];
    }

    /// <inheritdoc />
    public async Task<Answer<string>> ReachAsync(CancellationToken cancellationToken)
    {
        try
        {
            var agent = await nomad.GetAgentAsync(cancellationToken);

            return Answer.Ok(agent.Version);
        }
        catch (Exception exception) when (exception is HttpRequestException or NomadException or TaskCanceledException)
        {
            return Answer.Failed<string>(Messages.SchedulerDown(options.Value.NomadAddress, exception.Message), Messages.SchedulerDownHint);
        }
    }

    /// <summary>The allocation that stands for the job now: the newest one meant to run, or the newest of all once none is.</summary>
    private static Allocation? Latest(IReadOnlyList<Allocation> allocations) =>
        allocations.FirstOrDefault(allocation => string.Equals(allocation.DesiredStatus, "run", StringComparison.Ordinal)) ?? allocations.FirstOrDefault();
}
