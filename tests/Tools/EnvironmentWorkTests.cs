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

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static JobReport Report(CallToolResult result) => JobReport.FromJson(result.StructuredContent!.Value.GetRawText())!;

    private async Task<JobReport> EndOfAsync(CallToolResult result) => await _jobs.Find(Report(result).Uri)!.Completion.WaitAsync(Patience);

    [Fact]
    public async Task RaiseAsync_AppAnswersWithinTheWait_AnswersAsTheToolAlwaysDid()
    {
        var result = await Work().RaiseAsync(60, CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        Assert.StartsWith("""{"ok":true,"data":{"environment":"e2e-0c2b9e1f","state":"ready""", Text(result), StringComparison.Ordinal);
        Assert.Equal(JobState.Succeeded, Report(result).State);
    }

    [Fact]
    public async Task RaiseAsync_NoWait_AnswersWithTheJobAndWhereItsResultWillBe()
    {
        _stage.Jobs.Hold = _release.Task;

        var result = await Work().RaiseAsync(0, CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        Assert.StartsWith("job://e2e/up/", Report(result).Uri, StringComparison.Ordinal);
        Assert.Contains(Report(result).Uri, Text(result), StringComparison.Ordinal);
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

        Assert.Equal(Report(first).Uri, Report(second).Uri);
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
    public async Task DownAsync_WhileARaiseRuns_CancelsItAndItsJobSaysSo()
    {
        _stage.Jobs.Hold = _release.Task;
        var raise = await Work().RaiseAsync(0, CancellationToken.None);

        var down = await _stage.Environments().DownAsync(CancellationToken.None);
        var ended = await EndOfAsync(raise);

        Assert.True(down.Error is null);
        Assert.Contains(Messages.StoppedByDown, ended.Result!.Value.GetRawText(), StringComparison.Ordinal);
        Assert.False(_stage.Leases.IsBusy(TaskEnvironment.Of("0c2b9e1f")!.Value));
    }

    [Fact]
    public async Task RaiseAsync_SessionWithoutACopy_IsRefusedWithoutAJob()
    {
        var result = await Work(root: null).RaiseAsync(60, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("?root=", Text(result), StringComparison.Ordinal);
        Assert.False(result.StructuredContent?.TryGetProperty("uri", out _) ?? false);
    }
}
