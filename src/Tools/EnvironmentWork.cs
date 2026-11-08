using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Snail.MCP.E2E.Application;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.Hosting.Sessions;

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
/// A job belongs to its task, the id of the copy's first folder: a session of another task does not read or watch it,
/// whatever URI it is given (<c>WithJobs</c> in <c>EnvironmentServerExtensions</c> names the owner of a request with
/// <c>JobOwners.TaskOfCopy</c>, by the copy its address names).
/// </para>
/// <para>
/// The work answers as the tools do, an <c>Answer</c>: a raise without a manifest or a scheduler ends its job failed, with
/// the error, hint and type the tool answers (<c>JobAnswer.Result</c>), and an environment raised ends it with the data.
/// </para>
/// <para>
/// One environment works on one thing at a time (<see cref="EnvironmentLeases"/>). A raise called while a raise is the
/// newest thing in the line is that raise, and so is a test run with the same filter: the caller waits on the job
/// already there. Anything else — a test run with another filter among them — takes its place in the line, and its
/// job reports that it waits. A stop cancels them all.
/// </para>
/// </remarks>
public sealed class EnvironmentWork(TaskEnvironments environments, EnvironmentLeases leases, Jobs jobs)
{
    /// <summary>The longest a call waits for its job before answering with the job in flight.</summary>
    public const int MaxWaitSeconds = 900;

    private readonly Lock _gate = new();

    /// <summary>Raises the environment as a job and answers within the wait.</summary>
    public Task<CallToolResult> RaiseAsync(int wait, CancellationToken cancellationToken) =>
        RunAsync(LeaseKinds.Up, null, async (copy, token) => (await environments.UpAsync(copy, token)).Map(status => AnswerContent.Of(status)), wait, cancellationToken);

    /// <summary>Runs the end-to-end tests as a job and answers within the wait.</summary>
    public Task<CallToolResult> TestAsync(string? filter, int wait, CancellationToken cancellationToken) =>
        RunAsync(LeaseKinds.Test, filter, async (copy, token) => (await environments.TestAsync(copy, filter, token)).Map(report => AnswerContent.Of(report)), wait, cancellationToken);

    /// <summary>
    /// What the model reads of a job: the tool's own answer once the job ended — the data its work answered, or the
    /// failure — or the job in flight with the resource it is read at. The job's report is the structured content
    /// throughout, which a client of <c>Snail.Sdk.Mcp.JobClient</c> awaits.
    /// </summary>
    private async Task<CallToolResult> RunAsync(string kind, string? asked, Func<WorkspacePlace<TaskEnvironment>, CancellationToken, Task<Answer<JsonObject>>> work, int wait, CancellationToken cancellationToken)
    {
        var place = environments.Session();
        if (!place.TryGet(out var copy))
            return ToolAnswer.Failed(place);

        var job = Start(copy, kind, asked, work);
        var report = await job.ForAsync(TimeSpan.FromSeconds(Math.Clamp(wait, 0, MaxWaitSeconds)), cancellationToken);
        var running = new JsonObject { ["note"] = Messages.JobRunning(report.Uri) };

        return JobAnswer.Result(report, running, Messages.NotRaisedHint);
    }

    /// <summary>The job of the work asked, or the one already doing it.</summary>
    /// <remarks>The job belongs to the copy's task, the owner <c>JobOwners.TaskOfCopy</c> names for the sessions of that task.</remarks>
    private Job Start(WorkspacePlace<TaskEnvironment> copy, string kind, string? asked, Func<WorkspacePlace<TaskEnvironment>, CancellationToken, Task<Answer<JsonObject>>> work)
    {
        lock (_gate)
        {
            if (leases.Last(copy.Task) is { Job: { } uri } last && IsSame(last, kind, asked) && jobs.Find(uri) is { Report.IsRunning: true } running)
                return running;

            var lease = leases.Join(copy.Task, kind);
            lease.Asked = asked;
            var job = jobs.Start(kind, copy.Task.TaskId, (progress, token) => WorkAsync(lease, copy, work, progress, token), ToolAnswer.ObjectInfo);
            lease.Job = job.Uri;

            return job;
        }
    }

    private static bool IsSame(Lease lease, string kind, string? asked) =>
        lease.Kind == kind && string.Equals(lease.Asked ?? string.Empty, asked ?? string.Empty, StringComparison.Ordinal);

    private async Task<Answer<JsonObject>> WorkAsync(Lease lease, WorkspacePlace<TaskEnvironment> copy, Func<WorkspacePlace<TaskEnvironment>, CancellationToken, Task<Answer<JsonObject>>> work, IProgress<JobProgress> progress, CancellationToken cancellationToken)
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
            return Answer.Failed(Messages.StoppedByDown, Messages.NotRaisedHint);
        }
        finally
        {
            leases.Release(lease);
        }
    }
}
