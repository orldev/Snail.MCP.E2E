namespace Snail.MCP.E2E.Tests.Domain;

public sealed class PlaceholdersTests
{
    private static readonly TaskEnvironment Environment = TaskEnvironment.Of("4f1c")!.Value;

    private static readonly AppPart App = new("src/Api/Api.csproj", 8080, "/health", new Dictionary<string, string>());

    [Fact]
    public void Fill_ServiceAndApp_BecomeTheirHostsInTheEnvironment()
    {
        string filled = Placeholders.Fill("mongodb://{mongo}:27017 and {app}", Environment, App, ["mongo"]);

        Assert.Equal("mongodb://e2e-4f1c-mongo:27017 and http://e2e-4f1c-app:8080", filled);
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
        Assert.Equal("e2e-0c2b9e1f-tests", TaskEnvironment.Of("0c2b9e1f")!.Value.Tests);
    }

    [Fact]
    public void IsEnvironment_TestRunJob_IsNotAnEnvironment()
    {
        Assert.True(TaskEnvironment.IsEnvironment("e2e-4f1c"));
        Assert.False(TaskEnvironment.IsEnvironment("e2e-4f1c-tests"));
        Assert.False(TaskEnvironment.IsEnvironment("mcp-git"));
    }
}
