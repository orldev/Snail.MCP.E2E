using Snail.MCP.E2E.Application;

namespace Snail.MCP.E2E.Tests.Application;

public sealed class TaskEnvironmentsTests
{
    [Fact]
    public async Task UpAsync_AppAnswersItsHealth_IsReadyAndRaisedFromTheSessionsCopy()
    {
        var stage = new Stage();

        var answer = await stage.Environments().UpAsync(CancellationToken.None);

        Assert.Equal(EnvironmentState.Ready, answer.Value!.State);
        Assert.Equal("http://e2e-0c2b9e1f-app:8080/", answer.Value.AppUrl);
        Assert.Equal(Stage.Copy, Assert.Single(stage.Jobs.Raised).Copy);
        Assert.Equal("/health", stage.Calls.Sent.Last().Call.Path);
    }

    [Fact]
    public async Task UpAsync_AppTaskDied_IsFailedWithTheEndOfItsOutput()
    {
        var stage = new Stage();
        stage.Jobs.Run = new EnvironmentRun("a1", [new PartState("app", "dead", 2, true)]);
        stage.Jobs.Logs["app:err"] = "error CS1002: ; expected";

        var answer = await stage.Environments().UpAsync(CancellationToken.None);

        Assert.Equal(EnvironmentState.Failed, answer.Value!.State);
        Assert.Contains("CS1002", answer.Value.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpAsync_AppSilentPastTheWait_IsStillStarting()
    {
        var stage = new Stage();
        stage.Calls.Answer = null;

        var answer = await stage.Environments().UpAsync(CancellationToken.None);

        Assert.Equal(EnvironmentState.Starting, answer.Value!.State);
    }

    [Fact]
    public async Task UpAsync_SessionWithoutACopy_IsRefusedWithWhereTheCopyComesFrom()
    {
        var answer = await new Stage().Environments(root: null).UpAsync(CancellationToken.None);

        Assert.Equal(Messages.NoCopy, answer.Error);
        Assert.Equal(Messages.NoCopyHint, answer.Hint);
    }

    [Theory]
    [InlineData("/etc")]
    [InlineData("/workspaces/../etc")]
    [InlineData("/workspaces")]
    [InlineData("/workspaces/Task_1/step-1")]
    public async Task UpAsync_CopyOutsideTheWorkspaces_IsRefusedAndRaisesNothing(string root)
    {
        var stage = new Stage();

        var answer = await stage.Environments(root).UpAsync(CancellationToken.None);

        Assert.StartsWith("The working copy", answer.Error, StringComparison.Ordinal);
        Assert.Empty(stage.Jobs.Raised);
    }

    [Fact]
    public async Task UpAsync_CopyWithoutAManifest_SaysHowToWriteOne()
    {
        var stage = new Stage { Manifest = null };

        var answer = await stage.Environments().UpAsync(CancellationToken.None);

        Assert.Equal(Messages.NoManifestHint, answer.Hint);
        Assert.Empty(stage.Jobs.Raised);
    }

    [Fact]
    public async Task CallAsync_NothingRaised_SaysToRaiseFirst()
    {
        var answer = await new Stage().Environments().CallAsync(new HttpCall("GET", "/orders", null, "application/json", []), CancellationToken.None);

        Assert.Equal(Messages.NotRaised, answer.Error);
    }

    [Theory]
    [InlineData("TRACE", "/orders")]
    [InlineData("GET", "orders")]
    [InlineData("GET", "//evil.example/x")]
    public async Task CallAsync_MethodOrPathNotTheApps_IsRefusedBeforeAnyRequest(string method, string path)
    {
        var stage = new Stage();
        await stage.Environments().UpAsync(CancellationToken.None);
        int before = stage.Calls.Sent.Count;

        var answer = await stage.Environments().CallAsync(new HttpCall(method, path, null, "application/json", []), CancellationToken.None);

        Assert.NotNull(answer.Error);
        Assert.Equal(before, stage.Calls.Sent.Count);
    }

    [Fact]
    public async Task CallAsync_RaisedApp_GoesToItsAddressWithTheMethodInCapitals()
    {
        var stage = new Stage();
        await stage.Environments().UpAsync(CancellationToken.None);

        var answer = await stage.Environments().CallAsync(new HttpCall("post", "/orders", "{}", "application/json", []), CancellationToken.None);

        Assert.Equal(200, answer.Value!.Status);
        Assert.Equal(("http://e2e-0c2b9e1f-app:8080/", "POST"), (stage.Calls.Sent.Last().App.ToString(), stage.Calls.Sent.Last().Call.Method));
    }

    [Fact]
    public async Task LogsAsync_PartTheEnvironmentLacks_NamesTheOnesItHas()
    {
        var stage = new Stage();
        await stage.Environments().UpAsync(CancellationToken.None);

        var answer = await stage.Environments().LogsAsync("redis", null, CancellationToken.None);

        Assert.Equal(Messages.UnknownPart("redis", ["app", "mongo", "tests"]), answer.Error);
    }

    [Fact]
    public async Task TestAsync_RunEndedWithFailures_ReportsThemWithTheFilterPassedOn()
    {
        var stage = new Stage();
        await stage.Environments().UpAsync(CancellationToken.None);
        stage.Jobs.Tests = new TestRun("  Failed Shop.E2E.OrdersTests.Create [1 ms]\nFailed!  - Failed:     1, Passed:     4, Skipped:     0, Total:     5", false);

        var answer = await stage.Environments().TestAsync(" FullyQualifiedName~Orders ", CancellationToken.None);

        Assert.Equal(TestOutcome.Failed, answer.Value!.Outcome);
        Assert.Equal(["Shop.E2E.OrdersTests.Create"], answer.Value.Counts!.Failures);
        Assert.Equal("FullyQualifiedName~Orders", Assert.Single(stage.Jobs.Tested).Filter);
    }

    [Fact]
    public async Task TestAsync_RunOutOfTime_IsTimedOutWhateverItPrinted()
    {
        var stage = new Stage();
        await stage.Environments().UpAsync(CancellationToken.None);
        stage.Jobs.Tests = new TestRun("Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1", true);

        var answer = await stage.Environments().TestAsync(null, CancellationToken.None);

        Assert.Equal(TestOutcome.TimedOut, answer.Value!.Outcome);
    }

    [Fact]
    public async Task TestAsync_ManifestWithoutTests_SaysToAddThem()
    {
        var stage = new Stage { Manifest = Stage.Shop with { Tests = null } };
        await stage.Environments().UpAsync(CancellationToken.None);

        var answer = await stage.Environments().TestAsync(null, CancellationToken.None);

        Assert.Equal(Messages.NoTests, answer.Error);
    }

    [Fact]
    public async Task DownAsync_RaisedEnvironment_IsStoppedAndAStatusAfterSaysStopped()
    {
        var stage = new Stage();
        await stage.Environments().UpAsync(CancellationToken.None);

        var down = await stage.Environments().DownAsync(CancellationToken.None);
        var status = await stage.Environments().StatusAsync(CancellationToken.None);

        Assert.True(down.Value);
        Assert.Equal(EnvironmentState.Stopped, status.Value!.State);
    }
}
