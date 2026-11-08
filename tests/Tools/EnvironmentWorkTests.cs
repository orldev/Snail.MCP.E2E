using ModelContextProtocol.Protocol;
using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Tests.Application;
using Snail.MCP.E2E.Tools;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.MCP.E2E.Tests.Tools;

public sealed class EnvironmentWorkTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Stage _stage = new();
    private readonly Jobs _jobs = new("e2e");
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private EnvironmentWork Work(string? root = Stage.Copy) => new(_stage.Environments(root), _stage.Leases, _jobs);

    private async Task<JobReport> EndOfAsync(CallToolResult result) => await _jobs.Find(result.Report()!.Uri)!.Completion.WaitAsync(Patience);

    [Fact]
    public async Task RaiseAsync_AppAnswersWithinTheWait_AnswersAsTheToolAlwaysDid()
    {
        var result = await Work().RaiseAsync(60, CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        Assert.StartsWith("""{"ok":true,"data":{"environment":"e2e-0c2b9e1f","state":"ready""", result.Text(), StringComparison.Ordinal);
        Assert.Equal(JobState.Succeeded, result.Report()!.State);
    }

    [Fact]
    public async Task RaiseAsync_NoWait_AnswersWithTheJobAndWhereItsResultWillBe()
    {
        _stage.Jobs.Hold = _release.Task;

        var result = await Work().RaiseAsync(0, CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        Assert.StartsWith("job://e2e/up/", result.Report()!.Uri, StringComparison.Ordinal);
        Assert.Contains(result.Report()!.Uri, result.Text(), StringComparison.Ordinal);
        _release.SetResult();
    }

    /// <summary>A second raise used to register the job again under the first, which restarted the app it was waiting for.</summary>
    [Fact]
    public async Task RaiseAsync_CalledAgainWhileOneRuns_WaitsOnTheSameJob()
    {
        _stage.Jobs.Hold = _release.Task;

        var first = await Work().RaiseAsync(0, CancellationToken.None);
        var second = await Work().RaiseAsync(0, CancellationToken.None);
        _release.SetResult();
        await EndOfAsync(first);

        Assert.Equal(first.Report()!.Uri, second.Report()!.Uri);
        Assert.Single(_stage.Jobs.Raised);
    }

    /// <summary>A test run during a raise used to run against the app being replaced under it.</summary>
    [Fact]
    public async Task TestAsync_WhileARaiseRuns_WaitsItsTurnAndThenRuns()
    {
        _stage.Jobs.Hold = _release.Task;
        await Work().RaiseAsync(0, CancellationToken.None);

        var test = await Work().TestAsync(null, 0, CancellationToken.None);
        bool hasWaited = _stage.Jobs.Tested.Count == 0;
        _release.SetResult();
        var ended = await EndOfAsync(test);

        Assert.True(hasWaited);
        Assert.Equal(JobState.Succeeded, ended.State);
        Assert.Single(_stage.Jobs.Tested);
    }

    [Fact]
    public async Task TestAsync_SameFilterWhileOneRuns_WaitsOnTheSameJob()
    {
        await Work().RaiseAsync(60, CancellationToken.None);
        _stage.Jobs.Hold = _release.Task;

        var first = await Work().TestAsync("FullyQualifiedName~Orders", 0, CancellationToken.None);
        var second = await Work().TestAsync("FullyQualifiedName~Orders", 0, CancellationToken.None);
        _release.SetResult();
        await EndOfAsync(first);

        Assert.Equal(first.Report()!.Uri, second.Report()!.Uri);
        Assert.Single(_stage.Jobs.Tested);
    }

    /// <summary>
    /// A test run called while one ran used to join it whatever its filter: the model asked for the payment tests and was
    /// answered with the order tests, as if it had got what it asked for.
    /// </summary>
    [Fact]
    public async Task TestAsync_OtherFilterWhileOneRuns_RunsTheTestsItAskedForAfterIt()
    {
        await Work().RaiseAsync(60, CancellationToken.None);
        _stage.Jobs.Hold = _release.Task;

        var first = await Work().TestAsync("FullyQualifiedName~Orders", 0, CancellationToken.None);
        var second = await Work().TestAsync("FullyQualifiedName~Payments", 0, CancellationToken.None);
        _release.SetResult();
        await EndOfAsync(second);

        Assert.NotEqual(first.Report()!.Uri, second.Report()!.Uri);
        Assert.Equal(["FullyQualifiedName~Orders", "FullyQualifiedName~Payments"], _stage.Jobs.Tested.Select(plan => plan.Filter));
    }

    [Fact]
    public async Task DownAsync_WhileARaiseRuns_CancelsItAndItsJobSaysSo()
    {
        _stage.Jobs.Hold = _release.Task;
        var raise = await Work().RaiseAsync(0, CancellationToken.None);

        var down = await _stage.Environments().DownAsync(CancellationToken.None);
        var ended = await EndOfAsync(raise);

        Assert.True(down.Error is null);
        Assert.Equal(JobState.Failed, ended.State);
        Assert.Equal(Messages.StoppedByDown, ended.Message);
        Assert.Equal(Messages.NotRaisedHint, ended.Hint);
        Assert.False(_stage.Leases.IsBusy(TaskEnvironment.Of("0c2b9e1f")!.Value));
    }

    [Fact]
    public async Task RaiseAsync_SessionWithoutACopy_IsRefusedWithoutAJob()
    {
        var result = await Work(root: null).RaiseAsync(60, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("?root=", result.Text(), StringComparison.Ordinal);
        Assert.False(result.StructuredContent?.TryGetProperty("uri", out _) ?? false);
    }

    /// <summary>
    /// A raise that ended within the wait without an environment answers as its tool fails — error, hint, flagged — and its
    /// job ended failed with the same words: it used to end succeeded with the failure's envelope as its result, read as
    /// work done by a client that reads the job alone.
    /// </summary>
    [Fact]
    public async Task RaiseAsync_CopyWithoutAManifest_AnswersTheFailureTheJobEndedWith()
    {
        _stage.Manifest = null;

        var result = await Work().RaiseAsync(60, CancellationToken.None);
        var report = result.Report()!;

        Assert.True(result.IsError);
        Assert.False(result.IsOk());
        Assert.Equal(Messages.NoManifest(".snail/environment.json"), result.Error());
        Assert.Equal(Messages.NoManifestHint, result.Hint());
        Assert.Equal(JobState.Failed, report.State);
        Assert.Equal(Messages.NoManifest(".snail/environment.json"), report.Message);
        Assert.Equal(Messages.NoManifestHint, report.Hint);
    }

    /// <summary>A job belongs to the task the copy's first folder names, the owner <c>JobOwners.TaskOfCopy</c> gives every session of that task.</summary>
    [Fact]
    public async Task RaiseAsync_SessionAtATaskCopy_StartsAJobOwnedByTheTask()
    {
        var result = await Work().RaiseAsync(60, CancellationToken.None);

        Assert.Equal("0c2b9e1f", _jobs.Find(result.Report()!.Uri)!.Owner);
    }

    /// <summary>A job cancelled within the wait — the server stopping — answers as a failure that says what raises the environment again.</summary>
    [Fact]
    public async Task RaiseAsync_JobCancelledWithinTheWait_IsAFailureWithTheHintToRaiseAgain()
    {
        _stage.Jobs.Hold = _release.Task;
        var first = await Work().RaiseAsync(0, CancellationToken.None);
        var waiting = Work().RaiseAsync(60, CancellationToken.None);

        _jobs.Find(first.Report()!.Uri)!.Cancel();
        var result = await waiting.WaitAsync(Patience);

        Assert.True(result.IsError);
        Assert.Equal("cancelled", result.Type());
        Assert.Equal(Messages.NotRaisedHint, result.Hint());
        Assert.Equal(JobState.Cancelled, result.Report()!.State);
    }
}
