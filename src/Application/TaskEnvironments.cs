using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Application.Ports;
using Snail.MCP.E2E.Configuration;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.MCP.E2E.Application;

/// <summary>The task's environment: raised from the working copy's manifest, read, called, tested and stopped.</summary>
/// <remarks>
/// <para>
/// Each call reads the copy's manifest again: a step changes both, and the next raise has to run what the step left, not
/// what the first one saw.
/// </para>
/// <para>
/// A raise and a test run outlast a call, so they run as jobs and take the copy as an argument: the session's copy is in
/// the address of the request that started them, which is gone by the time the work runs. The calls that answer at once
/// read it from the session themselves.
/// </para>
/// </remarks>
public sealed class TaskEnvironments(
    SessionCopy session,
    IManifests manifests,
    IEnvironmentJobs jobs,
    IAppCalls calls,
    EnvironmentUse use,
    EnvironmentLeases leases,
    IOptions<E2EOptions> options,
    TimeProvider clock)
{
    /// <summary>How often a raise looks at the app while it waits for it.</summary>
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);

    private static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD"];

    /// <summary>The working copy this session works on, and its task's environment, or why it has none.</summary>
    public Answer<WorkspacePlace<TaskEnvironment>> Session() => session.Current();

    /// <summary>Raises the environment from the copy, or replaces the one running, and waits until the app answers its health check.</summary>
    public Task<Answer<EnvironmentStatus>> UpAsync(WorkspacePlace<TaskEnvironment> copy, CancellationToken cancellationToken) =>
        GuardAsync(() => RaiseAsync(copy, cancellationToken));

    /// <summary>Where the environment stands now, without waiting.</summary>
    public Task<Answer<EnvironmentStatus>> StatusAsync(CancellationToken cancellationToken) =>
        GuardAsync(() => ReadStatusAsync(cancellationToken));

    /// <summary>Stops the environment and its test runs, after cancelling whatever raise or test run works on it.</summary>
    public Task<Answer<bool>> DownAsync(CancellationToken cancellationToken) =>
        GuardAsync(() => StopAsync(cancellationToken));

    /// <summary>Sends one request to the app and answers with what it said.</summary>
    public Task<Answer<HttpExchange>> CallAsync(HttpCall call, CancellationToken cancellationToken) =>
        GuardAsync(() => SendAsync(call, cancellationToken));

    /// <summary>The end of one part's output: the app, a service, or the last test run.</summary>
    public Task<Answer<LogTail>> LogsAsync(string part, int? bytes, CancellationToken cancellationToken) =>
        GuardAsync(() => ReadLogsAsync(part, bytes, cancellationToken));

    /// <summary>Runs the project's end-to-end tests against the running app and reads back how they went.</summary>
    public Task<Answer<TestReport>> TestAsync(WorkspacePlace<TaskEnvironment> copy, string? filter, CancellationToken cancellationToken) =>
        GuardAsync(() => RunTestsAsync(copy, filter, cancellationToken));

    private async Task<Answer<EnvironmentStatus>> RaiseAsync(WorkspacePlace<TaskEnvironment> copy, CancellationToken cancellationToken)
    {
        var read = await ProjectAsync(copy, cancellationToken);
        if (!read.TryGet(out var project))
            return read.As<EnvironmentStatus>();

        use.Touch(copy.Task);
        var raised = await jobs.RaiseAsync(new EnvironmentPlan(copy.Task, copy.Path, project.Manifest), cancellationToken);

        return Answer.Ok(await WaitAsync(project, raised, cancellationToken));
    }

    private async Task<Answer<EnvironmentStatus>> ReadStatusAsync(CancellationToken cancellationToken)
    {
        var place = session.Current();
        if (!place.TryGet(out var copy))
            return place.As<EnvironmentStatus>();

        var read = await ProjectAsync(copy, cancellationToken);
        if (!read.TryGet(out var project))
            return read.As<EnvironmentStatus>();

        return Answer.Ok(await LookAsync(project, RaiseMark.None, cancellationToken));
    }

    private async Task<Answer<bool>> StopAsync(CancellationToken cancellationToken)
    {
        var place = session.Current();
        if (!place.TryGet(out var copy))
            return place.As<bool>();

        leases.CancelAll(copy.Task);
        var lease = leases.Join(copy.Task, LeaseKinds.Down);

        try
        {
            await lease.TurnAsync(cancellationToken);
            bool hadRun = await jobs.HasStoppedAsync(copy.Task, cancellationToken);
            use.Forget(copy.Task);

            return Answer.Ok(hadRun);
        }
        finally
        {
            leases.Release(lease);
        }
    }

    private async Task<Answer<HttpExchange>> SendAsync(HttpCall call, CancellationToken cancellationToken)
    {
        if (!Methods.Contains(call.Method, StringComparer.OrdinalIgnoreCase))
            return Answer.Failed<HttpExchange>(Messages.BadMethod(call.Method));

        if (!AppAddress.IsAppPath(call.Path))
            return Answer.Failed<HttpExchange>(Messages.BadPath(call.Path));

        var read = await RunningAsync(cancellationToken);
        if (!read.TryGet(out var project))
            return read.As<HttpExchange>();

        var exchange = await calls.SendAsync(AppOf(project), call with { Method = call.Method.ToUpperInvariant() }, options.Value.ResponseBytes, cancellationToken);

        return exchange is null ? Answer.Failed<HttpExchange>(Messages.Unreachable, Messages.NotRaisedHint) : Answer.Ok(exchange);
    }

    private async Task<Answer<LogTail>> ReadLogsAsync(string part, int? bytes, CancellationToken cancellationToken)
    {
        var read = await RunningAsync(cancellationToken);
        if (!read.TryGet(out var project))
            return read.As<LogTail>();

        IReadOnlyList<string> parts = [Parts.App, .. project.Manifest.Services.Keys, Parts.Tests];
        if (!parts.Contains(part, StringComparer.Ordinal))
            return Answer.Failed<LogTail>(Messages.UnknownPart(part, parts));

        int wanted = Math.Clamp(bytes ?? options.Value.DefaultLogBytes, 1, options.Value.MaxLogBytes);
        var environment = project.Copy.Task;
        string stdout = await jobs.LogsAsync(environment, part, false, wanted, cancellationToken);
        string stderr = await jobs.LogsAsync(environment, part, true, wanted, cancellationToken);

        return Answer.Ok(new LogTail(part, stdout, stderr));
    }

    private async Task<Answer<TestReport>> RunTestsAsync(WorkspacePlace<TaskEnvironment> copy, string? filter, CancellationToken cancellationToken)
    {
        if (filter is not null && !ManifestRules.IsLiteral(filter))
            return Answer.Failed<TestReport>(Messages.BadFilter(filter));

        var read = await RunningAsync(copy, cancellationToken);
        if (!read.TryGet(out var project))
            return read.As<TestReport>();

        if (project.Manifest.Tests is not { } tests)
            return Answer.Failed<TestReport>(Messages.NoTests, Messages.NoManifestHint);

        long started = clock.GetTimestamp();
        var plan = new TestPlan(copy.Task, copy.Path, project.Manifest, tests, string.IsNullOrWhiteSpace(filter) ? null : filter.Trim());
        var run = await jobs.TestAsync(plan, TimeSpan.FromSeconds(options.Value.TestSeconds), cancellationToken);
        var (outcome, counts) = run.HasTimedOut ? (TestOutcome.TimedOut, null) : TestSummary.Of(run.Output);
        var (output, isCut) = Tail(run.Output.Length > 0 ? run.Output : Messages.TestsSilent, options.Value.TestOutputBytes);

        return Answer.Ok(new TestReport(outcome, counts, output, isCut, clock.GetElapsedTime(started).TotalSeconds));
    }

    private async Task<EnvironmentStatus> WaitAsync(Project project, RaiseMark raised, CancellationToken cancellationToken)
    {
        long started = clock.GetTimestamp();
        var deadline = TimeSpan.FromSeconds(options.Value.StartSeconds);

        while (true)
        {
            var status = await LookAsync(project, raised, cancellationToken);
            bool isSettled = status.State is not EnvironmentState.Starting;

            if (isSettled || clock.GetElapsedTime(started) >= deadline)
                return status with { Seconds = Math.Round(clock.GetElapsedTime(started).TotalSeconds, 1) };

            await Task.Delay(Poll, clock, cancellationToken);
        }
    }

    /// <summary>Where the environment stands: its health is asked only of an app that runs, since an app on its way out answers it too.</summary>
    private async Task<EnvironmentStatus> LookAsync(Project project, RaiseMark since, CancellationToken cancellationToken)
    {
        var environment = project.Copy.Task;
        var run = await jobs.RunAsync(environment, since, cancellationToken);

        if (run is null)
            return new EnvironmentStatus(environment.Value, EnvironmentState.Stopped, null, []);

        string app = AppOf(project).ToString();

        if (FailedPart(run) is { } failed)
        {
            string stderr = await jobs.LogsAsync(environment, failed.Name, true, options.Value.DefaultLogBytes, cancellationToken);
            string stdout = await jobs.LogsAsync(environment, failed.Name, false, options.Value.DefaultLogBytes, cancellationToken);
            string output = failed.Name == Parts.App ? $"{stdout}{stderr}".Trim() : $"{failed.Name} failed. {stdout}{stderr}".Trim();

            return new EnvironmentStatus(environment.Value, EnvironmentState.Failed, app, run.Parts) { Output = output };
        }

        if (run.App is not { State: "running" })
            return new EnvironmentStatus(environment.Value, EnvironmentState.Starting, app, run.Parts);

        var health = new HttpCall("GET", project.Manifest.App.Health, null, "text/plain", []);
        var answer = await calls.SendAsync(AppOf(project), health, 0, cancellationToken);
        var state = answer is { Status: >= 200 and < 300 } ? EnvironmentState.Ready : EnvironmentState.Starting;

        return new EnvironmentStatus(environment.Value, state, app, run.Parts);
    }

    private async Task<Answer<Project>> RunningAsync(CancellationToken cancellationToken)
    {
        var place = session.Current();

        return place.TryGet(out var copy) ? await RunningAsync(copy, cancellationToken) : place.As<Project>();
    }

    private async Task<Answer<Project>> RunningAsync(WorkspacePlace<TaskEnvironment> copy, CancellationToken cancellationToken)
    {
        var read = await ProjectAsync(copy, cancellationToken);
        if (read.Value is not { } project)
            return read;

        if (await jobs.RunAsync(project.Copy.Task, RaiseMark.None, cancellationToken) is null)
            return Answer.Failed<Project>(Messages.NotRaised, Messages.NotRaisedHint);

        use.Touch(project.Copy.Task);

        return read;
    }

    private async Task<Answer<Project>> ProjectAsync(WorkspacePlace<TaskEnvironment> copy, CancellationToken cancellationToken)
    {
        var manifest = await manifests.ReadAsync(copy.Path, cancellationToken);

        return manifest.TryGet(out var read)
            ? Answer.Ok(new Project(copy, read))
            : manifest.As<Project>();
    }

    /// <summary>The answer of a call, or what Nomad's failure means for it: nothing happened, and why.</summary>
    private async Task<Answer<T>> GuardAsync<T>(Func<Task<Answer<T>>> call)
    {
        try
        {
            return await call();
        }
        catch (SchedulerException failure)
        {
            return Answer.Failed<T>(Messages.SchedulerDown(options.Value.NomadAddress, failure.Message), Messages.SchedulerDownHint);
        }
    }

    /// <summary>The part that ended the environment: the app once it died, or any other part Nomad gave up on.</summary>
    /// <remarks>Only the app was looked at once, so a service whose image could not be pulled left the environment starting until the wait ran out.</remarks>
    private static PartState? FailedPart(EnvironmentRun run) =>
        run.App is { HasFailed: true } or { State: "dead" }
            ? run.App
            : run.Parts.FirstOrDefault(part => part.HasFailed && part.Name != Parts.Keep);

    private static Uri AppOf(Project project) => new($"http://{project.Copy.Task.App}:{project.Manifest.App.Port}/");

    private static (string Text, bool IsCut) Tail(string text, int limit) =>
        text.Length <= limit ? (text, false) : (text[^limit..], true);

    /// <summary>The session's copy with the manifest it holds.</summary>
    private sealed record Project(WorkspacePlace<TaskEnvironment> Copy, EnvironmentManifest Manifest);
}
