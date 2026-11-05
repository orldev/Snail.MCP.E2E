using System.Net;
using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Application.Ports;
using Snail.MCP.E2E.Configuration;
using Snail.Toolkit.Nomad;
using Snail.Toolkit.Nomad.Models;

namespace Snail.MCP.E2E.Adapters;

/// <summary>TaskEnvironments as Nomad jobs: a service job per task, and a batch job per test run beside it.</summary>
/// <remarks>
/// Whatever Nomad's client throws — a refused connection, a timeout, an error status — leaves this adapter as a
/// <see cref="SchedulerException"/>, the one failure its port knows; a cancellation the caller asked for stays one.
/// </remarks>
public sealed class NomadEnvironmentJobs(INomadClient nomad, IOptions<E2EOptions> options, TimeProvider clock) : IEnvironmentJobs
{
    /// <summary>How often a test run is looked at while it runs.</summary>
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);

    /// <summary>Bytes of each stream of a test run read back; the summary is at the end, and the answer keeps less.</summary>
    /// <remarks>
    /// The failed tests are named from everything read, and a run of normal verbosity prints every test it ran: at 64 KB the
    /// names of failures early in a long run fell outside the window. A quarter of the client's limit keeps the read short.
    /// </remarks>
    private const int TestLogBytes = 262144;

    /// <summary>How many times the logs are read before a dropped stream counts as Nomad's failure.</summary>
    /// <remarks>A client node drops a log stream now and then while the server is fine; the test run's result is only in that stream.</remarks>
    private const int LogAttempts = 2;

    /// <summary>Bytes of the end of a run's output looked at for the line that says the tests ended.</summary>
    private const int EndBytes = 256;

    /// <summary>How long a test run left behind is given to be stopped.</summary>
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(15);

    private static readonly string[] Ended = ["complete", "failed", "lost"];

    /// <inheritdoc />
    public Task<RaiseMark> RaiseAsync(EnvironmentPlan plan, CancellationToken cancellationToken) =>
        AskAsync(() => RegisterAsync(plan, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<EnvironmentRun?> RunAsync(TaskEnvironment name, RaiseMark since, CancellationToken cancellationToken) =>
        AskAsync(() => ReadRunAsync(name, since, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasStoppedAsync(TaskEnvironment name, CancellationToken cancellationToken) =>
        AskAsync(() => HasStoppedJobsAsync(name, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<string> LogsAsync(TaskEnvironment name, string part, bool isError, int bytes, CancellationToken cancellationToken) =>
        AskAsync(() => ReadLogsAsync(name, part, isError, bytes, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<TestRun> TestAsync(TestPlan plan, TimeSpan timeout, CancellationToken cancellationToken) =>
        AskAsync(() => RunTestsAsync(plan, timeout, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<int> StopTestRunsAsync(CancellationToken cancellationToken) =>
        AskAsync(() => StopRunningTestsAsync(cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<TaskEnvironment>> ListAsync(CancellationToken cancellationToken) =>
        AskAsync(() => ListEnvironmentsAsync(cancellationToken), cancellationToken);

    /// <inheritdoc />
    public async Task<Answer<string>> ReachAsync(CancellationToken cancellationToken)
    {
        try
        {
            var agent = await AskAsync(() => nomad.GetAgentAsync(cancellationToken), cancellationToken);

            return Answer.Ok(agent.Version);
        }
        catch (SchedulerException failure)
        {
            return Answer.Failed<string>(Messages.SchedulerDown(options.Value.NomadAddress, failure.Message), Messages.SchedulerDownHint);
        }
    }

    /// <summary>The allocation that stands for the job now: the newest one meant to run, or the newest of all once none is.</summary>
    /// <remarks>Nomad lists allocations in its own order, so the newest is found by creation time.</remarks>
    internal static Allocation? Latest(NomadPage<Allocation> allocations)
    {
        var newestFirst = allocations.Items.OrderByDescending(allocation => allocation.CreateTime).ToList();
        var running = newestFirst.FirstOrDefault(allocation => string.Equals(allocation.DesiredStatus, "run", StringComparison.Ordinal));

        return running ?? newestFirst.FirstOrDefault();
    }

    /// <summary>A job Nomad gave up on is dead but not gone: its last allocation still holds the app's failure, and the caller is told that rather than that nothing runs.</summary>
    private async Task<RaiseMark> RegisterAsync(EnvironmentPlan plan, CancellationToken cancellationToken)
    {
        var earlier = await nomad.ListAllocationsAsync(plan.Name.Value, cancellationToken: cancellationToken);
        await nomad.RegisterJobAsync(EnvironmentJob.Of(plan, Guid.NewGuid().ToString("N"), options.Value), cancellationToken);

        return new RaiseMark([.. earlier.Items.Select(allocation => allocation.Id)]);
    }

    private async Task<EnvironmentRun?> ReadRunAsync(TaskEnvironment name, RaiseMark since, CancellationToken cancellationToken)
    {
        var job = await nomad.GetJobAsync(name.Value, cancellationToken);
        if (job is null || job.IsStopped)
            return null;

        var listed = await nomad.ListAllocationsAsync(name.Value, cancellationToken: cancellationToken);
        var allocation = Latest(new NomadPage<Allocation>([.. listed.Items.Where(item => !since.Earlier.Contains(item.Id, StringComparer.Ordinal))]));
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

    private async Task<bool> HasStoppedJobsAsync(TaskEnvironment name, CancellationToken cancellationToken)
    {
        var stopped = await nomad.StopJobAsync(name.Value, purge: true, cancellationToken);
        await nomad.StopJobAsync(name.Tests, purge: true, cancellationToken);

        return stopped is not null;
    }

    private async Task<string> ReadLogsAsync(TaskEnvironment name, string part, bool isError, int bytes, CancellationToken cancellationToken)
    {
        string job = part == Parts.Tests ? name.Tests : name.Value;
        var allocation = Latest(await nomad.ListAllocationsAsync(job, cancellationToken: cancellationToken));
        if (allocation is null)
            return string.Empty;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await nomad.ReadLogsAsync(allocation.Id, part, isError ? LogType.Stderr : LogType.Stdout, bytes, cancellationToken);
            }
            catch (NomadException exception) when (exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.InternalServerError)
            {
                return string.Empty;
            }
            catch (HttpRequestException) when (attempt < LogAttempts)
            {
            }
        }
    }

    /// <summary>The run's job is stopped on every way out, once its output is read: a run nobody waits for any more would hold the node until its timeout.</summary>
    /// <remarks>
    /// The run does not end its allocation when the tests end: it prints <see cref="EnvironmentJob.TestsEnded"/> and waits
    /// to be stopped. A node short of disk collects an allocation the moment it ends, and with it the output: on a stand
    /// whose Docker disk was 81 % full, against Nomad's threshold of 80, every run that passed answered that it printed
    /// nothing. So the end is read from the output, the output is read while the allocation still holds it, and only
    /// then is the run stopped.
    /// </remarks>
    private async Task<TestRun> RunTestsAsync(TestPlan plan, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await nomad.StopJobAsync(plan.Name.Tests, purge: true, cancellationToken);
        await nomad.RegisterJobAsync(EnvironmentJob.TestsOf(plan, Guid.NewGuid().ToString("N"), options.Value), cancellationToken);

        try
        {
            bool hasEnded = await HasEndedAsync(plan.Name, timeout, cancellationToken);
            string stdout = await ReadLogsAsync(plan.Name, Parts.Tests, false, TestLogBytes, cancellationToken);
            string stderr = await ReadLogsAsync(plan.Name, Parts.Tests, true, TestLogBytes, cancellationToken);

            return new TestRun(Joined(stderr, Unmarked(stdout)), !hasEnded);
        }
        finally
        {
            await StopLeftAsync(plan.Name.Tests);
        }
    }

    /// <summary>Both streams as one output, the standard one last: the answer keeps the end, where dotnet test prints its summary.</summary>
    /// <remarks>Joined the other way, a run that wrote to standard error had its summary pushed out of the end the answer keeps.</remarks>
    internal static string Joined(string stderr, string stdout) =>
        stderr.Length == 0 ? stdout : $"{stderr.TrimEnd()}\n{stdout}";

    /// <summary>The output without the line that says the tests ended, which is the script's, not the tests'.</summary>
    internal static string Unmarked(string stdout)
    {
        int mark = stdout.LastIndexOf(EnvironmentJob.TestsEnded, StringComparison.Ordinal);

        return mark < 0 ? stdout : stdout[..mark];
    }

    /// <summary>Whether the run ended within the time: its output says the tests ended, or its allocation ended on its own.</summary>
    private async Task<bool> HasEndedAsync(TaskEnvironment name, TimeSpan timeout, CancellationToken cancellationToken)
    {
        long started = clock.GetTimestamp();

        while (clock.GetElapsedTime(started) < timeout)
        {
            await Task.Delay(Poll, clock, cancellationToken);
            var allocation = Latest(await nomad.ListAllocationsAsync(name.Tests, cancellationToken: cancellationToken));

            if (allocation is null)
                continue;

            if (Ended.Contains(allocation.ClientStatus, StringComparer.Ordinal))
                return true;

            string tail = await ReadLogsAsync(name, Parts.Tests, false, EndBytes, cancellationToken);
            if (tail.Contains(EnvironmentJob.TestsEnded, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Stops a test run, its logs kept, on a time of its own: the caller's may be what ended.</summary>
    /// <remarks>A stop that fails is left to the run's own timeout in its script and to the sweep at the next start.</remarks>
    private async Task StopLeftAsync(string job)
    {
        using var grace = new CancellationTokenSource(StopGrace, clock);

        try
        {
            await nomad.StopJobAsync(job, purge: false, grace.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or NomadException or OperationCanceledException)
        {
        }
    }

    private async Task<int> StopRunningTestsAsync(CancellationToken cancellationToken)
    {
        var jobs = await nomad.ListJobsAsync(TaskEnvironment.Prefix, cancellationToken: cancellationToken);
        List<string> running = [.. jobs.Items.Where(job => TaskEnvironment.IsTestRun(job.Id) && !job.IsStopped && job.Status != "dead").Select(job => job.Id)];

        foreach (string job in running)
            await nomad.StopJobAsync(job, purge: false, cancellationToken);

        return running.Count;
    }

    private async Task<IReadOnlyList<TaskEnvironment>> ListEnvironmentsAsync(CancellationToken cancellationToken)
    {
        var jobs = await nomad.ListJobsAsync(TaskEnvironment.Prefix, cancellationToken: cancellationToken);

        return [.. jobs.Items.Where(job => !job.IsStopped && TaskEnvironment.IsEnvironment(job.Id)).Select(job => new TaskEnvironment(job.Id))];
    }

    private static async Task<T> AskAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (exception is HttpRequestException or NomadException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new SchedulerException(exception.Message, exception);
        }
    }
}
