using System.Net;
using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Adapters;
using Snail.MCP.E2E.Application.Ports;
using Snail.MCP.E2E.Configuration;
using Snail.MCP.E2E.Tests.Application;
using Snail.Toolkit.Nomad.Models;

namespace Snail.MCP.E2E.Tests.Adapters;

public sealed class NomadEnvironmentJobsTests
{
    private const string Job = "e2e-0c2b9e1f";

    private const string Tests = "e2e-0c2b9e1f--tests";

    private const string Allocations = """[{"ID":"a2","Name":"x","JobID":"e2e-0c2b9e1f","TaskGroup":"environment","NodeID":"n","ClientStatus":"running","DesiredStatus":"run","CreateTime":2,"TaskStates":{"mongo":{"State":"running"},"app":{"State":"running","Restarts":1}}},{"ID":"a1","Name":"x","JobID":"e2e-0c2b9e1f","TaskGroup":"environment","NodeID":"n","ClientStatus":"complete","DesiredStatus":"stop","CreateTime":1}]""";

    private readonly FakeNomad _nomad = new();

    private static TaskEnvironment Environment => TaskEnvironment.Of("0c2b9e1f")!.Value;

    private NomadEnvironmentJobs Jobs() => new(_nomad.Client(), Options.Create(new E2EOptions()), TimeProvider.System);

    private FakeNomad Registering() => _nomad.Json("POST", "/v1/jobs", """{"EvalID":"e1"}""");

    private static Allocation Of(string id, string desired, long created) =>
        new(id, "e2e-shop.app[0]", "e2e-shop", "app", "node", DesiredStatus: desired, CreateTime: created);

    [Fact]
    public void Latest_OldestListedFirst_TakesTheNewestMeantToRun()
    {
        NomadPage<Allocation> page = new([Of("old", "run", 1), Of("stopped", "stop", 3), Of("new", "run", 2)]);

        Assert.Equal("new", NomadEnvironmentJobs.Latest(page)?.Id);
    }

    [Fact]
    public void Latest_NoneMeantToRun_TakesTheNewestOfAll()
    {
        NomadPage<Allocation> page = new([Of("old", "stop", 1), Of("new", "stop", 2)]);

        Assert.Equal("new", NomadEnvironmentJobs.Latest(page)?.Id);
    }

    [Fact]
    public void Latest_NoAllocations_IsNull()
    {
        Assert.Null(NomadEnvironmentJobs.Latest(new NomadPage<Allocation>([])));
    }

    [Fact]
    public async Task RunAsync_RunningJob_NamesEachPartOfTheNewestAllocationInOrder()
    {
        _nomad.Json("GET", $"/v1/job/{Job}", """{"ID":"e2e-0c2b9e1f","Name":"e2e-0c2b9e1f","Namespace":"default","Type":"service","Status":"running","StatusDescription":"","Version":1,"Stop":false,"SubmitTime":1,"TaskGroups":[]}""")
            .Json("GET", $"/v1/job/{Job}/allocations", Allocations);

        var run = await Jobs().RunAsync(Environment, CancellationToken.None);

        Assert.Equal("a2", run!.Allocation);
        Assert.Equal(["app", "mongo"], run.Parts.Select(part => part.Name));
        Assert.Equal(1, run.App!.Restarts);
    }

    [Fact]
    public async Task RunAsync_NomadNotReachable_IsASchedulerFailure()
    {
        _nomad.Down = new HttpRequestException("Connection refused");

        await Assert.ThrowsAsync<SchedulerException>(() => Jobs().RunAsync(Environment, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_NomadAnswersAnError_IsASchedulerFailure()
    {
        _nomad.Json("GET", $"/v1/job/{Job}", "no leader", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<SchedulerException>(() => Jobs().RunAsync(Environment, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_CallerCancelled_StaysACancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Jobs().RunAsync(Environment, cancelled.Token));
    }

    [Fact]
    public async Task ReachAsync_NomadNotReachable_SaysWhereItWasLookedFor()
    {
        _nomad.Down = new HttpRequestException("Connection refused");

        var answer = await Jobs().ReachAsync(CancellationToken.None);

        Assert.Contains("http://nomad:4646", answer.Error, StringComparison.Ordinal);
    }

    /// <summary>A client node drops a log stream now and then; the result of a test run is only in that stream.</summary>
    [Fact]
    public async Task LogsAsync_StreamDroppedOnce_IsReadAgain()
    {
        _nomad.Json("GET", $"/v1/job/{Job}/allocations", Allocations)
            .Answer("GET", "/v1/client/fs/logs/a2", Dropped)
            .Text("GET", "/v1/client/fs/logs/a2", "Passed!");

        string logs = await Jobs().LogsAsync(Environment, "app", false, 100, CancellationToken.None);

        Assert.Equal("Passed!", logs);
    }

    [Fact]
    public async Task LogsAsync_StreamDroppedEveryTime_IsASchedulerFailure()
    {
        _nomad.Json("GET", $"/v1/job/{Job}/allocations", Allocations)
            .Answer("GET", "/v1/client/fs/logs/a2", Dropped);

        await Assert.ThrowsAsync<SchedulerException>(() => Jobs().LogsAsync(Environment, "app", false, 100, CancellationToken.None));
    }

    [Fact]
    public async Task LogsAsync_PartWithoutAnAllocation_IsEmpty()
    {
        string logs = await Jobs().LogsAsync(Environment, "tests", false, 100, CancellationToken.None);

        Assert.Empty(logs);
    }

    [Fact]
    public async Task ListAsync_JobsOfThePrefix_KeepsRunningEnvironmentsOnly()
    {
        _nomad.Json("GET", "/v1/jobs", """[{"ID":"e2e-aa","Name":"e2e-aa","Namespace":"default","Type":"service","Status":"running"},{"ID":"e2e-bb","Name":"e2e-bb","Namespace":"default","Type":"service","Status":"dead","Stop":true},{"ID":"e2e-aa--tests","Name":"e2e-aa--tests","Namespace":"default","Type":"batch","Status":"running"}]""");

        var environments = await Jobs().ListAsync(CancellationToken.None);

        Assert.Equal(["e2e-aa"], environments.Select(environment => environment.Value));
    }

    /// <summary>A run cancelled while it went on was left on the node with nobody waiting for it.</summary>
    [Fact]
    public async Task TestAsync_CallerGoneWhileTheRunGoesOn_StopsTheRunAndKeepsItsLogs()
    {
        Registering();
        using var gone = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Jobs().TestAsync(Plan(), TimeSpan.FromMinutes(15), gone.Token));

        Assert.Equal(("DELETE", $"/v1/job/{Tests}"), (_nomad.Sent[^1].Method, _nomad.Sent[^1].Path));
    }

    [Fact]
    public async Task TestAsync_OutOfTime_StopsTheRunAndSaysSo()
    {
        Registering();

        var run = await Jobs().TestAsync(Plan(), TimeSpan.Zero, CancellationToken.None);

        Assert.True(run.HasTimedOut);
        Assert.Contains(_nomad.Sent, request => request is { Method: "DELETE" } && request.Path == $"/v1/job/{Tests}");
    }

    [Fact]
    public async Task StopTestRunsAsync_RunsAnEarlierStartLeft_StopsTheRunningOnesOnly()
    {
        _nomad.Json("GET", "/v1/jobs", """[{"ID":"e2e-aa","Name":"e2e-aa","Namespace":"default","Type":"service","Status":"running"},{"ID":"e2e-aa--tests","Name":"e2e-aa--tests","Namespace":"default","Type":"batch","Status":"running"},{"ID":"e2e-bb--tests","Name":"e2e-bb--tests","Namespace":"default","Type":"batch","Status":"dead"}]""");

        int stopped = await Jobs().StopTestRunsAsync(CancellationToken.None);

        Assert.Equal(1, stopped);
        Assert.Equal(["/v1/job/e2e-aa--tests"], _nomad.Sent.Where(request => request.Method == "DELETE").Select(request => request.Path));
    }

    /// <summary>Joined the other way, a run that wrote to standard error had its summary pushed out of the end the answer keeps.</summary>
    [Fact]
    public void Joined_BothStreams_EndsWithTheStandardOneAndItsSummary()
    {
        string output = NomadEnvironmentJobs.Joined("warning: obsolete\n", "Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3");

        Assert.EndsWith("Total: 3", output, StringComparison.Ordinal);
        Assert.StartsWith("warning: obsolete", output, StringComparison.Ordinal);
    }

    private static TestPlan Plan() => new(Environment, Stage.Copy, Stage.Shop, Stage.Shop.Tests!, null);

    private static HttpResponseMessage Dropped() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new DroppedStream()) };

    /// <summary>A body whose connection ends in the middle, as a client node's does.</summary>
    private sealed class DroppedStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Connection reset by peer");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("Connection reset by peer");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new IOException("Connection reset by peer");
    }
}
