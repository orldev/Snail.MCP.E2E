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
        Assert.Equal("e2e-0c2b9e1f--app", (string?)Task(job, "app")["Config"]!["network_aliases"]![0]);
        Assert.Equal("e2e-0c2b9e1f--mongo", (string?)Task(job, "mongo")["Config"]!["network_aliases"]![0]);
        Assert.Equal("mongo:8.0", (string?)Task(job, "mongo")["Config"]!["image"]);
    }

    [Fact]
    public void Of_Shop_GivesTheAppTheProxyWithItsOwnHostsKeptOff()
    {
        var app = Task(EnvironmentJob.Of(new EnvironmentPlan(Name, Stage.Copy, Stage.Shop), Options), "app");

        Assert.Equal("8080", (string?)app["Env"]!["S3L_PORT"]);
        Assert.Equal("http://mcp-e2e:3128", (string?)app["Env"]!["HTTPS_PROXY"]);
        Assert.Contains("e2e-0c2b9e1f--mongo", (string?)app["Env"]!["NO_PROXY"], StringComparison.Ordinal);
    }

    /// <summary>The app once mounted every task's workspace and one package folder all apps wrote to.</summary>
    [Fact]
    public void Of_Shop_AppSeesNeitherTheWorkspacesNorThePackageVolume()
    {
        var app = Task(EnvironmentJob.Of(new EnvironmentPlan(Name, Stage.Copy, Stage.Shop), Options), "app");

        Assert.Null(app["VolumeMounts"]);
        Assert.Null(app["Config"]!["mounts"]);
        Assert.Null(app["Env"]!["S3L_COPY"]);
        Assert.Equal("/alloc/nuget", (string?)app["Env"]!["NUGET_PACKAGES"]);
        Assert.Contains("cp -a /alloc/src/. /tmp/src/", (string?)app["Config"]!["args"]![1], StringComparison.Ordinal);
    }

    [Fact]
    public void Of_Shop_PreparesTheCopyFirstFromTheWorkspacesReadOnlyAndOffTheNetwork()
    {
        var job = EnvironmentJob.Of(new EnvironmentPlan(Name, Stage.Copy, Stage.Shop), Options);
        var prepare = Task(job, Parts.Prepare);

        Assert.Equal("prestart", (string?)prepare["Lifecycle"]!["Hook"]);
        Assert.Equal(Stage.Copy, (string?)prepare["Env"]!["S3L_COPY"]);
        Assert.Equal("e2e-0c2b9e1f", (string?)prepare["Env"]!["S3L_TASK"]);
        Assert.Equal("none", (string?)prepare["Config"]!["network_mode"]);
        Assert.True((bool?)prepare["VolumeMounts"]![0]!["ReadOnly"]);
        Assert.Equal("host-to-task", (string?)prepare["VolumeMounts"]![0]!["PropagationMode"]);
        Assert.True((bool?)job["TaskGroups"]![0]!["Volumes"]!["workspaces"]!["ReadOnly"]);
    }

    /// <summary>A link the app leaves among its packages must not let the next copy write into another task's folder.</summary>
    [Fact]
    public void Of_Shop_KeepsThePackagesAfterwardsThroughAFreshFolderOfTheTask()
    {
        var keep = Task(EnvironmentJob.Of(new EnvironmentPlan(Name, Stage.Copy, Stage.Shop), Options), Parts.Keep);
        string script = (string)keep["Config"]!["args"]![1]!;

        Assert.Equal("poststop", (string?)keep["Lifecycle"]!["Hook"]);
        Assert.Null(keep["VolumeMounts"]);
        Assert.Contains("rm -rf \"/nuget/tasks/$S3L_TASK\"", script, StringComparison.Ordinal);
        Assert.Contains("mv -T \"$fresh\" \"/nuget/tasks/$S3L_TASK\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TestsOf_Run_IsPreparedAndKeptLikeTheEnvironment()
    {
        var plan = new TestPlan(Name, Stage.Copy, Stage.Shop, Stage.Shop.Tests!, null);

        var job = EnvironmentJob.TestsOf(plan, "r1", Options);

        Assert.Equal("prestart", (string?)Task(job, Parts.Prepare)["Lifecycle"]!["Hook"]);
        Assert.Equal("poststop", (string?)Task(job, Parts.Keep)["Lifecycle"]!["Hook"]);
        Assert.Null(Task(job, "tests")["VolumeMounts"]);
    }

    [Fact]
    public void Of_SettingWithAServiceMark_ReachesTheAppFilled()
    {
        var manifest = Stage.Shop with { App = Stage.Shop.App with { Env = new Dictionary<string, string> { ["Mongo__Url"] = "mongodb://{mongo}:27017" } } };

        var job = EnvironmentJob.Of(new EnvironmentPlan(Name, Stage.Copy, manifest), Options);

        Assert.Equal("mongodb://e2e-0c2b9e1f--mongo:27017", (string?)Task(job, "app")["Env"]!["Mongo__Url"]);
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

        Assert.Equal(("e2e-0c2b9e1f--tests", "batch", "r1"), ((string?)job["ID"], (string?)job["Type"], (string?)job["Meta"]!["s3l.e2e.run"]));
        Assert.Equal("http://e2e-0c2b9e1f--app:8080", (string?)tests["Env"]!["APP_URL"]);
        Assert.Equal("FullyQualifiedName~Orders", (string?)tests["Env"]!["S3L_FILTER"]);
        Assert.Equal(0, (int?)job["TaskGroups"]![0]!["RestartPolicy"]!["Attempts"]);
    }

    /// <summary>A run whose server restarted ran on with nobody waiting for it; it now ends on the limit the server waits for.</summary>
    [Fact]
    public void TestsOf_Run_EndsOnItsOwnTimeLimit()
    {
        var plan = new TestPlan(Name, Stage.Copy, Stage.Shop, Stage.Shop.Tests!, null);

        var tests = Task(EnvironmentJob.TestsOf(plan, "r1", Options), "tests");

        Assert.Equal($"{Options.TestSeconds}s", (string?)tests["Env"]!["S3L_TIMEOUT"]);
        Assert.Contains("timeout -k 10 \"$S3L_TIMEOUT\" dotnet test", (string?)tests["Config"]!["args"]![1], StringComparison.Ordinal);
    }

    /// <summary>Unescaped, a setting of the manifest read the node's attributes through Nomad's interpolation.</summary>
    [Fact]
    public void Of_ManifestValuesSpellingAnInterpolation_ReachNomadEscaped()
    {
        var mongo = new ServicePart("mongo:8.0", 27017, new Dictionary<string, string> { ["HOST"] = "${attr.unique.hostname}" }, ["--bind", "${NOMAD_IP_db}"]);
        var manifest = Stage.Shop with
        {
            App = Stage.Shop.App with { Env = new Dictionary<string, string> { ["Secret"] = "${node.unique.id}" } },
            Services = new Dictionary<string, ServicePart> { ["mongo"] = mongo }
        };

        var job = EnvironmentJob.Of(new EnvironmentPlan(Name, Stage.Copy, manifest), Options);

        Assert.Equal("$${node.unique.id}", (string?)Task(job, "app")["Env"]!["Secret"]);
        Assert.Equal("$${attr.unique.hostname}", (string?)Task(job, "mongo")["Env"]!["HOST"]);
        Assert.Equal("$${NOMAD_IP_db}", (string?)Task(job, "mongo")["Config"]!["args"]![1]);
    }

    [Fact]
    public void TestsOf_FilterSpellingAnInterpolation_ReachesNomadEscaped()
    {
        var plan = new TestPlan(Name, Stage.Copy, Stage.Shop, Stage.Shop.Tests!, "Name=${node.unique.id}");

        var job = EnvironmentJob.TestsOf(plan, "r1", Options);

        Assert.Equal("Name=$${node.unique.id}", (string?)Task(job, "tests")["Env"]!["S3L_FILTER"]);
    }
}
