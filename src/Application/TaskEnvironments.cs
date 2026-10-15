using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Application.Ports;
using Snail.MCP.E2E.Configuration;

namespace Snail.MCP.E2E.Application;

/// <summary>The task's environment: raised from the working copy's manifest, read, called, tested and stopped.</summary>
/// <remarks>
/// Each call reads the session's copy and its manifest again: a step changes both, and the next raise has to run what
/// the step left, not what the first one saw.
/// </remarks>
public sealed class TaskEnvironments(
    SessionCopy session,
    IManifests manifests,
    IEnvironmentJobs jobs,
    IAppCalls calls,
    EnvironmentUse use,
    IOptions<E2EOptions> options,
    TimeProvider clock)
{
    /// <summary>How often a raise looks at the app while it waits for it.</summary>
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);

    private static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD"];

    /// <summary>Raises the environment from the copy, or replaces the one running, and waits until the app answers its health check.</summary>
    public async Task<Answer<EnvironmentStatus>> UpAsync(CancellationToken cancellationToken)
    {
        var read = await ProjectAsync(cancellationToken);
        if (read.Value is not { } project)
            return Answer.Failed<EnvironmentStatus>(read.Error!, read.Hint);

        use.Touch(project.Copy.Environment);
        await jobs.RaiseAsync(new EnvironmentPlan(project.Copy.Environment, project.Copy.Path, project.Manifest), cancellationToken);

        return Answer.Ok(await WaitAsync(project, cancellationToken));
    }

    /// <summary>Where the environment stands now, without waiting.</summary>
    public async Task<Answer<EnvironmentStatus>> StatusAsync(CancellationToken cancellationToken)
    {
        var read = await ProjectAsync(cancellationToken);
        if (read.Value is not { } project)
            return Answer.Failed<EnvironmentStatus>(read.Error!, read.Hint);

        return Answer.Ok(await LookAsync(project, cancellationToken));
    }

    /// <summary>Stops the environment and its test runs.</summary>
    public async Task<Answer<bool>> DownAsync(CancellationToken cancellationToken)
    {
        var place = session.Current();
        if (place.Value is not { } copy)
            return Answer.Failed<bool>(place.Error!, place.Hint);

        bool hadRun = await jobs.HasStoppedAsync(copy.Environment, cancellationToken);
        use.Forget(copy.Environment);

        return Answer.Ok(hadRun);
    }

    /// <summary>Sends one request to the app and answers with what it said.</summary>
    public async Task<Answer<HttpExchange>> CallAsync(HttpCall call, CancellationToken cancellationToken)
    {
        if (!Methods.Contains(call.Method, StringComparer.OrdinalIgnoreCase))
            return Answer.Failed<HttpExchange>(Messages.BadMethod(call.Method));

        if (!call.Path.StartsWith('/') || call.Path.StartsWith("//", StringComparison.Ordinal))
            return Answer.Failed<HttpExchange>(Messages.BadPath(call.Path));

        var read = await RunningAsync(cancellationToken);
        if (read.Value is not { } project)
            return Answer.Failed<HttpExchange>(read.Error!, read.Hint);

        var exchange = await calls.SendAsync(AppOf(project), call with { Method = call.Method.ToUpperInvariant() }, options.Value.ResponseBytes, cancellationToken);

        return exchange is null ? Answer.Failed<HttpExchange>(Messages.Unreachable, Messages.NotRaisedHint) : Answer.Ok(exchange);
    }

    /// <summary>The end of one part's output: the app, a service, or the last test run.</summary>
    public async Task<Answer<LogTail>> LogsAsync(string part, int? bytes, CancellationToken cancellationToken)
    {
        var read = await RunningAsync(cancellationToken);
        if (read.Value is not { } project)
            return Answer.Failed<LogTail>(read.Error!, read.Hint);

        IReadOnlyList<string> parts = [Parts.App, .. project.Manifest.Services.Keys, Parts.Tests];
        if (!parts.Contains(part, StringComparer.Ordinal))
            return Answer.Failed<LogTail>(Messages.UnknownPart(part, parts));

        int wanted = Math.Clamp(bytes ?? options.Value.DefaultLogBytes, 1, options.Value.MaxLogBytes);
        var environment = project.Copy.Environment;
        string stdout = await jobs.LogsAsync(environment, part, false, wanted, cancellationToken);
        string stderr = await jobs.LogsAsync(environment, part, true, wanted, cancellationToken);

        return Answer.Ok(new LogTail(part, stdout, stderr));
    }

    /// <summary>Runs the project's end-to-end tests against the running app and reads back how they went.</summary>
    public async Task<Answer<TestReport>> TestAsync(string? filter, CancellationToken cancellationToken)
    {
        var read = await RunningAsync(cancellationToken);
        if (read.Value is not { } project)
            return Answer.Failed<TestReport>(read.Error!, read.Hint);

        if (project.Manifest.Tests is not { } tests)
            return Answer.Failed<TestReport>(Messages.NoTests, Messages.NoManifestHint);

        long started = clock.GetTimestamp();
        var plan = new TestPlan(project.Copy.Environment, project.Copy.Path, project.Manifest, tests, string.IsNullOrWhiteSpace(filter) ? null : filter.Trim());
        var run = await jobs.TestAsync(plan, TimeSpan.FromSeconds(options.Value.TestSeconds), cancellationToken);
        var (outcome, counts) = run.HasTimedOut ? (TestOutcome.TimedOut, null) : TestSummary.Of(run.Output);
        var (output, isCut) = Tail(run.Output.Length > 0 ? run.Output : Messages.TestsSilent, options.Value.TestOutputBytes);

        return Answer.Ok(new TestReport(outcome, counts, output, isCut, clock.GetElapsedTime(started).TotalSeconds));
    }

    private async Task<EnvironmentStatus> WaitAsync(Project project, CancellationToken cancellationToken)
    {
        long started = clock.GetTimestamp();
        var deadline = TimeSpan.FromSeconds(options.Value.StartSeconds);

        while (true)
        {
            var status = await LookAsync(project, cancellationToken);
            bool isSettled = status.State is not EnvironmentState.Starting;

            if (isSettled || clock.GetElapsedTime(started) >= deadline)
                return status with { Seconds = Math.Round(clock.GetElapsedTime(started).TotalSeconds, 1) };

            await Task.Delay(Poll, clock, cancellationToken);
        }
    }

    private async Task<EnvironmentStatus> LookAsync(Project project, CancellationToken cancellationToken)
    {
        var environment = project.Copy.Environment;
        var run = await jobs.RunAsync(environment, cancellationToken);

        if (run is null)
            return new EnvironmentStatus(environment.Value, EnvironmentState.Stopped, null, []);

        string app = AppOf(project).ToString();

        if (run.App is { HasFailed: true } or { State: "dead" })
        {
            string stderr = await jobs.LogsAsync(environment, Parts.App, true, options.Value.DefaultLogBytes, cancellationToken);
            string stdout = await jobs.LogsAsync(environment, Parts.App, false, options.Value.DefaultLogBytes, cancellationToken);

            return new EnvironmentStatus(environment.Value, EnvironmentState.Failed, app, run.Parts) { Output = $"{stdout}{stderr}".Trim() };
        }

        var health = new HttpCall("GET", project.Manifest.App.Health, null, "text/plain", []);
        var answer = await calls.SendAsync(AppOf(project), health, 0, cancellationToken);
        var state = answer is { Status: >= 200 and < 300 } ? EnvironmentState.Ready : EnvironmentState.Starting;

        return new EnvironmentStatus(environment.Value, state, app, run.Parts);
    }

    private async Task<Answer<Project>> RunningAsync(CancellationToken cancellationToken)
    {
        var read = await ProjectAsync(cancellationToken);
        if (read.Value is not { } project)
            return read;

        if (await jobs.RunAsync(project.Copy.Environment, cancellationToken) is null)
            return Answer.Failed<Project>(Messages.NotRaised, Messages.NotRaisedHint);

        use.Touch(project.Copy.Environment);

        return read;
    }

    private async Task<Answer<Project>> ProjectAsync(CancellationToken cancellationToken)
    {
        var place = session.Current();
        if (place.Value is not { } copy)
            return Answer.Failed<Project>(place.Error!, place.Hint);

        var manifest = await manifests.ReadAsync(copy.Path, cancellationToken);

        return manifest.Value is { } read
            ? Answer.Ok(new Project(copy, read))
            : Answer.Failed<Project>(manifest.Error!, manifest.Hint);
    }

    private static Uri AppOf(Project project) => new($"http://{project.Copy.Environment.App}:{project.Manifest.App.Port}/");

    private static (string Text, bool IsCut) Tail(string text, int limit) =>
        text.Length <= limit ? (text, false) : (text[^limit..], true);

    /// <summary>The session's copy with the manifest it holds.</summary>
    private sealed record Project(CopyPlace Copy, EnvironmentManifest Manifest);
}
