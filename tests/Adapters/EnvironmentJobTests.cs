using System.Text.Json.Nodes;
using Snail.MCP.E2E.Adapters;
using Snail.MCP.E2E.Configuration;
using Snail.MCP.E2E.Tests.Application;

namespace Snail.MCP.E2E.Tests.Adapters;

public sealed class EnvironmentJobTests
{
    private static readonly TaskEnvironment Name = TaskEnvironment.Of("0c2b9e1f")!.Value;

    private static readonly E2EOptions Options = new() { RestoreProxy = "http://mcp-e2e:3128" };

    private static JsonObject Task(JsonObject job, string name) =>
        job["TaskGroups"]![0]!["Tasks"]!.AsArray().Single(task => (string?)task!["Name"] == name)!.AsObject();

    [Fact]
    public void Of_Shop_PutsTheAppAndItsServiceOnTheEnvironmentsNetworkUnderTheirHosts()
    {
        var job = EnvironmentJob.Of(new EnvironmentPlan(Name, Stage.Copy, Stage.Shop), Options);

        Assert.Equal("e2e-0c2b9e1f", (string?)job["ID"]);
        Assert.Equal("s3l-e2e", (string?)Task(job, "app")["Config"]!["network_mode"]);
        Assert.Equal("e2e-0c2b9e1f-app", (string?)Task(job, "app")["Config"]!["network_aliases"]![0]);
        Assert.Equal("e2e-0c2b9e1f-mongo", (string?)Task(job, "mongo")["Config"]!["network_aliases"]![0]);
        Assert.Equal("mongo:8.0", (string?)Task(job, "mongo")["Config"]!["image"]);
    }

    [Fact]
    public void Of_Shop_GivesTheAppItsCopyReadOnlyAndTheProxyWithItsOwnHostsKeptOff()
    {
        var job = EnvironmentJob.Of(new EnvironmentPlan(Name, Stage.Copy, Stage.Shop), Options);
        var app = Task(job, "app");

        Assert.Equal(Stage.Copy, (string?)app["Env"]!["S3L_COPY"]);
        Assert.Equal("8080", (string?)app["Env"]!["S3L_PORT"]);
        Assert.Equal("http://mcp-e2e:3128", (string?)app["Env"]!["HTTPS_PROXY"]);
        Assert.Contains("e2e-0c2b9e1f-mongo", (string?)app["Env"]!["NO_PROXY"], StringComparison.Ordinal);
        Assert.True((bool?)app["VolumeMounts"]![0]!["ReadOnly"]);
        Assert.Equal("host-to-task", (string?)app["VolumeMounts"]![0]!["PropagationMode"]);
        Assert.True((bool?)job["TaskGroups"]![0]!["Volumes"]!["workspaces"]!["ReadOnly"]);
    }

    [Fact]
    public void Of_SettingWithAServiceMark_ReachesTheAppFilled()
    {
        var manifest = Stage.Shop with { App = Stage.Shop.App with { Env = new Dictionary<string, string> { ["Mongo__Url"] = "mongodb://{mongo}:27017" } } };

        var job = EnvironmentJob.Of(new EnvironmentPlan(Name, Stage.Copy, manifest), Options);

        Assert.Equal("mongodb://e2e-0c2b9e1f-mongo:27017", (string?)Task(job, "app")["Env"]!["Mongo__Url"]);
    }

    [Fact]
    public void Of_NoProxyConfigured_LeavesTheAppWithoutOne()
    {
        var job = EnvironmentJob.Of(new EnvironmentPlan(Name, Stage.Copy, Stage.Shop), new E2EOptions());

        Assert.Null(Task(job, "app")["Env"]!["HTTPS_PROXY"]);
    }

    /// <summary>Nomad interpolates every <c>${…}</c> of a config string: the scripts escape the shell's own, or the run fails validation.</summary>
    [Fact]
    public void Scripts_ShellExpansions_AreEscapedFromNomad()
    {
        var plan = new TestPlan(Name, Stage.Copy, Stage.Shop, Stage.Shop.Tests!, null);
        JsonObject[] jobs = [EnvironmentJob.Of(new EnvironmentPlan(Name, Stage.Copy, Stage.Shop), Options), EnvironmentJob.TestsOf(plan, "r1", Options)];

        IEnumerable<string> scripts = jobs.SelectMany(job => job["TaskGroups"]![0]!["Tasks"]!.AsArray())
            .Select(task => task!["Config"]!["args"]?.AsArray().LastOrDefault()?.GetValue<string>() ?? string.Empty);

        Assert.All(scripts, script => Assert.DoesNotMatch(@"(?<!\$)\$\{", script));
    }

    [Fact]
    public void TestsOf_Run_IsABatchJobOnceWithTheAppsAddressAndTheFilter()
    {
        var plan = new TestPlan(Name, Stage.Copy, Stage.Shop, Stage.Shop.Tests!, "FullyQualifiedName~Orders");

        var job = EnvironmentJob.TestsOf(plan, "r1", Options);
        var tests = Task(job, "tests");

        Assert.Equal(("e2e-0c2b9e1f-tests", "batch", "r1"), ((string?)job["ID"], (string?)job["Type"], (string?)job["Meta"]!["s3l.e2e.run"]));
        Assert.Equal("http://e2e-0c2b9e1f-app:8080", (string?)tests["Env"]!["APP_URL"]);
        Assert.Equal("FullyQualifiedName~Orders", (string?)tests["Env"]!["S3L_FILTER"]);
        Assert.Equal(0, (int?)job["TaskGroups"]![0]!["RestartPolicy"]!["Attempts"]);
    }
}
