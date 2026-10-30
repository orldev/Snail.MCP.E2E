namespace Snail.MCP.E2E.Tests.Domain;

public sealed class PlaceholdersTests
{
    private static readonly TaskEnvironment Environment = TaskEnvironment.Of("4f1c")!.Value;

    private static readonly AppPart App = new("src/Api/Api.csproj", 8080, "/health", new Dictionary<string, string>());

    [Fact]
    public void Fill_ServiceAndApp_BecomeTheirHostsInTheEnvironment()
    {
        string filled = Placeholders.Fill("mongodb://{mongo}:27017 and {app}", Environment, App, ["mongo"]);

        Assert.Equal("mongodb://e2e-4f1c--mongo:27017 and http://e2e-4f1c--app:8080", filled);
    }

    [Fact]
    public void Fill_MarkOfNothingTheEnvironmentHolds_StaysAsItWas()
    {
        Assert.Equal("{redis}:6379", Placeholders.Fill("{redis}:6379", Environment, App, ["mongo"]));
    }

    [Fact]
    public void Of_TaskIdThatCannotNameAHost_IsNull()
    {
        Assert.Null(TaskEnvironment.Of("Task_1"));
        Assert.Null(TaskEnvironment.Of(new string('a', 70)));
        Assert.Equal("e2e-0c2b9e1f--tests", TaskEnvironment.Of("0c2b9e1f")!.Value.Tests);
    }

    [Fact]
    public void IsEnvironment_TestRunJob_IsNotAnEnvironment()
    {
        Assert.True(TaskEnvironment.IsEnvironment("e2e-4f1c"));
        Assert.False(TaskEnvironment.IsEnvironment("e2e-4f1c--tests"));
        Assert.False(TaskEnvironment.IsEnvironment("mcp-git"));
    }

    [Theory]
    [InlineData("foo--bar")]
    [InlineData("foo-")]
    [InlineData("-foo")]
    [InlineData("")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Of_IdThatWouldShareAHostOrOutgrowALabel_IsNull(string taskId)
    {
        Assert.Null(TaskEnvironment.Of(taskId));
    }

    /// <summary>The platform names a task by a GUID without dashes; its longest host must still be one DNS label.</summary>
    [Fact]
    public void Of_PlatformTaskId_GivesHostsThatFitALabel()
    {
        var environment = TaskEnvironment.Of(Guid.NewGuid().ToString("n"))!.Value;

        Assert.True(environment.HostOf(new string('s', ManifestRules.MaxServiceName)).Length <= 63);
    }

    /// <summary>With one dash, the task foo-tests was the test run of the task foo, and the next run of foo purged it.</summary>
    [Fact]
    public void IsEnvironment_TaskNamedLikeATestRun_IsAnEnvironmentOfItsOwn()
    {
        var foo = TaskEnvironment.Of("foo")!.Value;
        var fooTests = TaskEnvironment.Of("foo-tests")!.Value;

        Assert.NotEqual(foo.Tests, fooTests.Value);
        Assert.True(TaskEnvironment.IsEnvironment(fooTests.Value));
        Assert.True(TaskEnvironment.IsTestRun(foo.Tests));
        Assert.False(TaskEnvironment.IsTestRun(fooTests.Value));
    }

    /// <summary>With one dash, the service b-app of the task a answered at the app's host of the task a-b.</summary>
    [Fact]
    public void HostOf_ServiceEndingLikeAPartOfAnotherTask_IsAHostOfItsOwn()
    {
        Assert.NotEqual(TaskEnvironment.Of("a")!.Value.HostOf("b-app"), TaskEnvironment.Of("a-b")!.Value.App);
    }
}
