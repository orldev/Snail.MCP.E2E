using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Application.Ports;

namespace Snail.MCP.E2E.Tests.Application;

public sealed class TaskEnvironmentsTests
{
    [Fact]
    public async Task UpAsync_AppAnswersItsHealth_IsReadyAndRaisedFromTheSessionsCopy()
    {
        var stage = new Stage();

        var answer = await stage.UpAsync();

        Assert.Equal(EnvironmentState.Ready, answer.Value!.State);
        Assert.Equal("http://e2e-0c2b9e1f--app:8080/", answer.Value.AppUrl);
        Assert.Equal(Stage.Copy, Assert.Single(stage.Jobs.Raised).Copy);
        Assert.Equal("/health", stage.Calls.Sent.Last().Call.Path);
    }

    [Fact]
    public async Task UpAsync_AppTaskDied_IsFailedWithTheEndOfItsOutput()
    {
        var stage = new Stage();
        stage.Jobs.Run = new EnvironmentRun("a1", [new PartState("app", "dead", 2, true)]);
        stage.Jobs.Logs["app:err"] = "error CS1002: ; expected";

        var answer = await stage.UpAsync();

        Assert.Equal(EnvironmentState.Failed, answer.Value!.State);
        Assert.Contains("CS1002", answer.Value.Output, StringComparison.Ordinal);
    }

    /// <summary>Only the app was looked at once: a service whose image could not be pulled left the environment starting until the wait ran out.</summary>
    [Fact]
    public async Task UpAsync_ServiceNomadGaveUpOn_IsFailedNamingIt()
    {
        var stage = new Stage();
        stage.Jobs.Run = new EnvironmentRun("a1", [new PartState("app", "pending", 0, false), new PartState("mongo", "dead", 0, true)]);
        stage.Jobs.Logs["mongo:err"] = "pull access denied";

        var answer = await stage.UpAsync();

        Assert.Equal(EnvironmentState.Failed, answer.Value!.State);
        Assert.Equal("mongo failed. pull access denied", answer.Value.Output);
    }

    /// <summary>An app on its way out answers its health check too; only an app whose task runs is asked.</summary>
    [Fact]
    public async Task UpAsync_AppTaskNotRunningYet_IsStartingWithoutAskingItsHealth()
    {
        var stage = new Stage();
        stage.Jobs.Run = new EnvironmentRun(null, []);

        var answer = await stage.UpAsync();

        Assert.Equal(EnvironmentState.Starting, answer.Value!.State);
        Assert.Empty(stage.Calls.Sent);
    }

    [Fact]
    public async Task UpAsync_AppSilentPastTheWait_IsStillStarting()
    {
        var stage = new Stage();
        stage.Calls.Answer = null;

        var answer = await stage.UpAsync();

        Assert.Equal(EnvironmentState.Starting, answer.Value!.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Session_AddressWithoutACopy_IsRefusedWithWhereTheCopyComesFrom(string? root)
    {
        var answer = new Stage().Environments(root).Session();

        Assert.Equal(Messages.NoCopy, answer.Error);
        Assert.Equal(Messages.NoCopyHint, answer.Hint);
        Assert.Equal("noRoot", answer.Type);
    }

    /// <summary>One sentence for every root that is no task's copy, typed by the reason so a program can tell them apart.</summary>
    [Theory]
    [InlineData("/etc", "outside")]
    [InlineData("/workspaces/../etc", "malformed")]
    [InlineData("/workspaces", "outside")]
    [InlineData("/workspaces/Task_1/step-1", "notATask")]
    [InlineData("/workspaces/0c2b9e1f/../../etc", "malformed")]
    [InlineData("/workspaces-old/0c2b9e1f/step-1", "outside")]
    public void Session_CopyOutsideTheWorkspaces_IsRefused(string root, string type)
    {
        var stage = new Stage();

        var answer = stage.Environments(root).Session();

        Assert.StartsWith("The working copy", answer.Error, StringComparison.Ordinal);
        Assert.Equal(type, answer.Type);
    }

    /// <summary>
    /// The calls that read the session's copy themselves refuse a root as e2e_up does, typed by the reason: they copied
    /// the refusal's error and hint by hand and lost its type, so up said <c>noRoot</c> and status, down, http and logs
    /// said nothing a program could branch on.
    /// </summary>
    [Theory]
    [InlineData("status", null, "noRoot")]
    [InlineData("down", null, "noRoot")]
    [InlineData("http", null, "noRoot")]
    [InlineData("logs", null, "noRoot")]
    [InlineData("status", "/etc", "outside")]
    [InlineData("down", "/workspaces/../etc", "malformed")]
    [InlineData("http", "/workspaces/Task_1/step-1", "notATask")]
    [InlineData("logs", "/etc", "outside")]
    public async Task CallReadingTheSession_RootThatIsNoCopy_IsRefusedTypedAsUpIs(string tool, string? root, string type)
    {
        var environments = new Stage().Environments(root);

        string? said = tool switch
        {
            "status" => (await environments.StatusAsync(CancellationToken.None)).Type,
            "down" => (await environments.DownAsync(CancellationToken.None)).Type,
            "http" => (await environments.CallAsync(new HttpCall("GET", "/orders", null, "application/json", []), CancellationToken.None)).Type,
            _ => (await environments.LogsAsync("app", null, CancellationToken.None)).Type
        };

        Assert.Equal(environments.Session().Type, said);
        Assert.Equal(type, said);
    }

    [Fact]
    public async Task UpAsync_CopyWithoutAManifest_SaysHowToWriteOne()
    {
        var stage = new Stage { Manifest = null };

        var answer = await stage.UpAsync();

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
    [InlineData("POST", "//nomad:4646/v1/jobs")]
    public async Task CallAsync_MethodOrPathNotTheApps_IsRefusedBeforeAnyRequest(string method, string path)
    {
        var stage = new Stage();
        await stage.UpAsync();
        int before = stage.Calls.Sent.Count;

        var answer = await stage.Environments().CallAsync(new HttpCall(method, path, null, "application/json", []), CancellationToken.None);

        Assert.NotNull(answer.Error);
        Assert.Equal(before, stage.Calls.Sent.Count);
    }

    [Fact]
    public async Task CallAsync_RaisedApp_GoesToItsAddressWithTheMethodInCapitals()
    {
        var stage = new Stage();
        await stage.UpAsync();

        var answer = await stage.Environments().CallAsync(new HttpCall("post", "/orders", "{}", "application/json", []), CancellationToken.None);

        Assert.Equal(200, answer.Value!.Status);
        Assert.Equal(("http://e2e-0c2b9e1f--app:8080/", "POST"), (stage.Calls.Sent.Last().App.ToString(), stage.Calls.Sent.Last().Call.Method));
    }

    [Fact]
    public async Task LogsAsync_PartTheEnvironmentLacks_NamesTheOnesItHas()
    {
        var stage = new Stage();
        await stage.UpAsync();

        var answer = await stage.Environments().LogsAsync("redis", null, CancellationToken.None);

        Assert.Equal(Messages.UnknownPart("redis", ["app", "mongo", "tests"]), answer.Error);
    }

    [Fact]
    public async Task TestAsync_RunEndedWithFailures_ReportsThemWithTheFilterPassedOn()
    {
        var stage = new Stage();
        await stage.UpAsync();
        stage.Jobs.Tests = new TestRun("  Failed Shop.E2E.OrdersTests.Create [1 ms]\nFailed!  - Failed:     1, Passed:     4, Skipped:     0, Total:     5", false);

        var answer = await stage.TestAsync(" FullyQualifiedName~Orders ");

        Assert.Equal(TestOutcome.Failed, answer.Value!.Outcome);
        Assert.Equal(["Shop.E2E.OrdersTests.Create"], answer.Value.Counts!.Failures);
        Assert.Equal("FullyQualifiedName~Orders", Assert.Single(stage.Jobs.Tested).Filter);
    }

    /// <summary>On its way to the run Nomad turned ${node.unique.id} in a filter into the node's id.</summary>
    [Fact]
    public async Task TestAsync_FilterThatSpellsAnInterpolation_IsRefusedBeforeAnyRun()
    {
        var stage = new Stage();
        await stage.UpAsync();

        var answer = await stage.TestAsync("FullyQualifiedName~${node.unique.id}");

        Assert.Equal(Messages.BadFilter("FullyQualifiedName~${node.unique.id}"), answer.Error);
        Assert.Empty(stage.Jobs.Tested);
    }

    [Fact]
    public async Task TestAsync_RunOutOfTime_IsTimedOutWhateverItPrinted()
    {
        var stage = new Stage();
        await stage.UpAsync();
        stage.Jobs.Tests = new TestRun("Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1", true);

        var answer = await stage.TestAsync();

        Assert.Equal(TestOutcome.TimedOut, answer.Value!.Outcome);
    }

    [Fact]
    public async Task TestAsync_ManifestWithoutTests_SaysToAddThem()
    {
        var stage = new Stage { Manifest = Stage.Shop with { Tests = null } };
        await stage.UpAsync();

        var answer = await stage.TestAsync();

        Assert.Equal(Messages.NoTests, answer.Error);
    }

    [Fact]
    public async Task DownAsync_RaisedEnvironment_IsStoppedAndAStatusAfterSaysStopped()
    {
        var stage = new Stage();
        await stage.UpAsync();

        var down = await stage.Environments().DownAsync(CancellationToken.None);
        var status = await stage.Environments().StatusAsync(CancellationToken.None);

        Assert.True(down.Value);
        Assert.Equal(EnvironmentState.Stopped, status.Value!.State);
    }

    /// <summary>Each of these once ended in an error the MCP SDK said nothing about: Nomad's failure flew past the answer.</summary>
    [Theory]
    [InlineData("up")]
    [InlineData("status")]
    [InlineData("down")]
    [InlineData("http")]
    [InlineData("logs")]
    [InlineData("test")]
    public async Task AnyCall_NomadGone_AnswersWhereNomadWasLookedForAndWhatToDo(string tool)
    {
        var stage = new Stage();
        await stage.UpAsync();
        stage.Jobs.Failure = new SchedulerException("Connection refused");
        var environments = stage.Environments();

        (string? Error, string? Hint) answer = tool switch
        {
            "up" => Said(await environments.UpAsync(environments.Session().Value!, CancellationToken.None)),
            "status" => Said(await environments.StatusAsync(CancellationToken.None)),
            "down" => Said(await environments.DownAsync(CancellationToken.None)),
            "http" => Said(await environments.CallAsync(new HttpCall("GET", "/orders", null, "application/json", []), CancellationToken.None)),
            "logs" => Said(await environments.LogsAsync("app", null, CancellationToken.None)),
            _ => Said(await environments.TestAsync(environments.Session().Value!, null, CancellationToken.None))
        };

        Assert.Equal(Messages.SchedulerDown(stage.Options.NomadAddress, "Connection refused"), answer.Error);
        Assert.Equal(Messages.SchedulerDownHint, answer.Hint);
    }

    private static (string? Error, string? Hint) Said<T>(Answer<T> answer) => (answer.Error, answer.Hint);
}
