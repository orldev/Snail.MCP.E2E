using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Snail.MCP.E2E.Application;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.MCP.E2E.Tools;

/// <summary>A raise and a test run as jobs of this server, in each environment's line.</summary>
/// <remarks>
/// <para>
/// A raise takes minutes and a test run up to fifteen: a call that waited for them held the client past its own timeout,
/// and a client that gave up cancelled the work with it. Each now runs as a job: the tool answers within the wait the
/// caller allows — the result, or the job's <c>job://e2e/…</c> resource to read when the result is wanted — and the work
/// goes on whatever happens to the call.
/// </para>
/// <para>
/// One environment works on one thing at a time (<see cref="EnvironmentLeases"/>). A raise called while a raise is the
/// newest thing in the line is that raise, and so is a test run: the caller waits on the job already there. Anything
/// else takes its place in the line, and its job reports that it waits. A stop cancels them all.
/// </para>
/// </remarks>
public sealed class EnvironmentWork(TaskEnvironments environments, EnvironmentLeases leases, Jobs jobs)
{
    /// <summary>The longest a call waits for its job before answering with the job in flight.</summary>
    public const int MaxWaitSeconds = 900;

    private readonly Lock _gate = new();

    /// <summary>Raises the environment as a job and answers within the wait.</summary>
    public Task<CallToolResult> RaiseAsync(int wait, CancellationToken cancellationToken) =>
        RunAsync(LeaseKinds.Up, async (copy, token) => ToolResponse.Envelope(await environments.UpAsync(copy, token)), wait, cancellationToken);

    /// <summary>Runs the end-to-end tests as a job and answers within the wait.</summary>
    public Task<CallToolResult> TestAsync(string? filter, int wait, CancellationToken cancellationToken) =>
        RunAsync(LeaseKinds.Test, async (copy, token) => ToolResponse.Envelope(await environments.TestAsync(copy, filter, token)), wait, cancellationToken);

    private async Task<CallToolResult> RunAsync(string kind, Func<CopyPlace, CancellationToken, Task<JsonObject>> work, int wait, CancellationToken cancellationToken)
    {
        var place = environments.Session();
        if (place.Value is not { } copy)
            return ToolResponse.Failure(place.Error!, place.Hint);

        var job = Start(copy, kind, work);
        var report = await job.ForAsync(TimeSpan.FromSeconds(Math.Clamp(wait, 0, MaxWaitSeconds)), cancellationToken);

        return ToolResponse.From(report);
    }

    private Job Start(CopyPlace copy, string kind, Func<CopyPlace, CancellationToken, Task<JsonObject>> work)
    {
        lock (_gate)
        {
            if (leases.Last(copy.Environment) is { Job: { } uri } last && last.Kind == kind && jobs.Find(uri) is { Report.IsRunning: true } running)
                return running;

            var lease = leases.Join(copy.Environment, kind);
            var job = jobs.Start(kind, (progress, token) => WorkAsync(lease, copy, work, progress, token), E2EJson.Default.JsonObject);
            lease.Job = job.Uri;

            return job;
        }
    }

    private async Task<JsonObject> WorkAsync(Lease lease, CopyPlace copy, Func<CopyPlace, CancellationToken, Task<JsonObject>> work, IProgress<JobProgress> progress, CancellationToken cancellationToken)
    {
        using var either = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Stopping);

        try
        {
            progress.Report(new JobProgress(0, null, Messages.WaitingInLine));
            await lease.TurnAsync(either.Token);
            progress.Report(new JobProgress(0, null, lease.Kind == LeaseKinds.Up ? Messages.Raising : Messages.Testing));

            return await work(copy, either.Token);
        }
        catch (OperationCanceledException) when (lease.Stopping.IsCancellationRequested)
        {
            return ToolResponse.Envelope(Answer.Failed<bool>(Messages.StoppedByDown, Messages.NotRaisedHint));
        }
        finally
        {
            leases.Release(lease);
        }
    }
}
