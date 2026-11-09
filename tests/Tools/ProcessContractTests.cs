using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.JobClient;

namespace Snail.MCP.E2E.Tests.Tools;

/// <summary>
/// The program as the platform starts it — the build beside the tests, or the publication <c>SNAIL_E2E_SERVER</c>
/// names — asked over HTTP.
/// </summary>
/// <remarks>
/// The server takes a port, <c>SNAIL_MCP_E2E_PORT</c>, not a URL. Nomad's address names a port nothing listens on: no
/// environment is raised, and the tools that need the scheduler have to say so in an answer of their own.
/// </remarks>
public sealed class ProcessContractTests : IAsyncLifetime
{
    public const string ServerVariable = "SNAIL_E2E_SERVER";

    private const string PortVariable = "SNAIL_MCP_E2E_PORT";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly string _workspaces = TestFolder.Create();
    private HttpServerProgram _server = default!;

    public async Task InitializeAsync()
    {
        var environment = new Dictionary<string, string?>
        {
            ["SNAIL_MCP_E2E_NOMAD_ADDRESS"] = $"http://127.0.0.1:{Ports.Free()}",
            ["SNAIL_MCP_E2E_WORKSPACES_ROOT"] = _workspaces
        };

        _server = await HttpServerProgram.StartAsync("Snail.MCP.E2E", ServerVariable, environment, addressVariable: PortVariable);
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        Directory.Delete(_workspaces, recursive: true);
    }

    [Fact]
    public async Task ListTools_StartedProgram_OffersEveryTool()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await _server.SessionAsync(cancellationToken: cancellation.Token);

        var tools = await session.ListToolsAsync(cancellationToken: cancellation.Token);

        Assert.Equal(["e2e_diagnose", "e2e_down", "e2e_http", "e2e_logs", "e2e_status", "e2e_test", "e2e_up"], tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>The guard rules of every Snail server, on the list a model receives.</summary>
    [Fact]
    public async Task Tools_AsListed_KeepTheGuardRules()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await _server.SessionAsync(cancellationToken: cancellation.Token);

        var tools = await session.ListToolsAsync(cancellationToken: cancellation.Token);

        Assert.Empty(ToolGuards.Problems(tools.Select(tool => tool.ProtocolTool)));
    }

    [Fact]
    public async Task CallTool_DiagnoseWithoutNomad_NamesTheAddressThatDidNotAnswer()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await _server.SessionAsync(cancellationToken: cancellation.Token);

        var result = await session.CallToolAsync("e2e_diagnose", cancellationToken: cancellation.Token);

        Assert.True(result.IsError);
        Assert.Contains("did not answer", result.Error(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallTool_UpWithoutACopyInTheAddress_IsAToolErrorThatSaysWhereTheCopyComesFrom()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await _server.SessionAsync(cancellationToken: cancellation.Token);

        var result = await session.CallToolAsync("e2e_up", cancellationToken: cancellation.Token);

        Assert.True(result.IsError);
        Assert.Contains("?root=", result.Text(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A raise is a job: a client of Snail.Sdk.Mcp.JobClient awaits it to its end, here failed with Nomad's absence said in
    /// words — it used to end succeeded, the failure's envelope as its result.
    /// </summary>
    [Fact]
    public async Task CallJob_UpWithoutNomad_EndsFailedWithTheAddressThatDidNotAnswer()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await _server.SessionAsync($"root={await CopyAsync("0c2b9e1f", cancellation.Token)}", cancellationToken: cancellation.Token);

        var report = await session.CallJobAsync("e2e_up", cancellationToken: cancellation.Token);

        Assert.Equal(JobState.Failed, report.State);
        Assert.StartsWith("job://e2e/up/", report.Uri, StringComparison.Ordinal);
        Assert.Contains("did not answer", report.Message, StringComparison.Ordinal);
        Assert.NotNull(report.Hint);
    }

    /// <summary>A raise answered within the wait is its tool's failure — the error, the hint, flagged — with the job's report beside it.</summary>
    [Fact]
    public async Task CallTool_UpWithoutNomad_IsAFailureWithTheJobBesideIt()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await _server.SessionAsync($"root={await CopyAsync("0c2b9e1f", cancellation.Token)}", cancellationToken: cancellation.Token);

        var result = await session.CallToolAsync("e2e_up", new Dictionary<string, object?> { ["wait"] = 30 }, cancellationToken: cancellation.Token);

        Assert.True(result.IsError);
        Assert.False(result.IsOk());
        Assert.Contains("did not answer", result.Error(), StringComparison.Ordinal);
        Assert.Equal(JobState.Failed, result.Report()!.State);
    }

    /// <summary>
    /// Every client is given a session, as by every server of the fleet: on the MCP SDK's default transport, stateless
    /// since 2.2, a client that opens with initialize and subscribes to a raise or a test run is never told of its end,
    /// since the server keeps no stream to tell it on — Snail.MCP.Security's scan waited forever so.
    /// </summary>
    [Fact]
    public async Task Initialize_StartedProgram_GivesTheClientASession()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await _server.SessionAsync(cancellationToken: cancellation.Token);

        Assert.False(string.IsNullOrEmpty(session.SessionId));
    }

    /// <summary>A copy whose path climbs out with <c>..</c> is not a task's copy, whatever folder it seems to name.</summary>
    [Fact]
    public async Task CallTool_UpAtACopyThatClimbsOut_IsRefusedAsNoTaskCopy()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        string root = Uri.EscapeDataString($"{_workspaces}/0c2b9e1f/../../etc");
        await using var session = await _server.SessionAsync($"root={root}", cancellationToken: cancellation.Token);

        var result = await session.CallToolAsync("e2e_up", cancellationToken: cancellation.Token);

        Assert.True(result.IsError);
        Assert.StartsWith("The working copy", result.Error(), StringComparison.Ordinal);
    }

    /// <summary>One server serves every task of the platform: a session of another task does not read a task's job, whatever URI it holds.</summary>
    [Fact]
    public async Task ReadJob_OfAnotherTask_IsUnknownToItsSessions()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var owner = await _server.SessionAsync($"root={await CopyAsync("0c2b9e1f", cancellation.Token)}", cancellationToken: cancellation.Token);
        await using var other = await _server.SessionAsync($"root={await CopyAsync("4f1c", cancellation.Token)}", cancellationToken: cancellation.Token);

        var report = await owner.CallJobAsync("e2e_up", cancellationToken: cancellation.Token);
        var read = await owner.ReadJobAsync(report.Uri, cancellation.Token);

        Assert.Equal(report.Uri, read.Uri);
        await Assert.ThrowsAnyAsync<Exception>(() => other.ReadJobAsync(report.Uri, cancellation.Token));
    }

    /// <summary>A tool the address did not grant is refused by name, in the envelope every answer is.</summary>
    [Fact]
    public async Task CallTool_ToolNotGrantedToTheSession_IsRefusedByName()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await _server.SessionAsync("tool=e2e_status", cancellationToken: cancellation.Token);

        var result = await session.CallToolAsync("e2e_down", cancellationToken: cancellation.Token);

        Assert.True(result.IsError);
        Assert.False(result.IsOk());
        Assert.Contains("'e2e_down' was not granted", result.Error(), StringComparison.Ordinal);
    }

    /// <summary>A role given some of the tools is listed those alone: a model offered a tool it may not call spends a turn learning so.</summary>
    [Fact]
    public async Task ListTools_SessionGrantedOne_ListsThatOneAlone()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await _server.SessionAsync("tool=e2e_status", cancellationToken: cancellation.Token);

        var tools = await session.ListToolsAsync(cancellationToken: cancellation.Token);

        Assert.Equal(["e2e_status"], tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// A page in a browser on the stand's machine that reaches the port sends <c>Origin</c>; the server used to build a
    /// host of its own and answered it 200, so the page could call every tool.
    /// </summary>
    [Fact]
    public async Task Mcp_RequestFromABrowserPage_IsRefused()
    {
        Assert.Empty(await HttpGuards.ProblemsAsync(_server.Endpoint));
    }

    /// <summary>A step's copy of a task under the workspaces, with a manifest, as the address names it.</summary>
    private async Task<string> CopyAsync(string task, CancellationToken cancellationToken)
    {
        string copy = Path.Combine(_workspaces, task, "step-1");
        Directory.CreateDirectory(Path.Combine(copy, ".snail"));
        await File.WriteAllTextAsync(Path.Combine(copy, ".snail", "environment.json"), """{ "app": { "project": "src/Api/Api.csproj" } }""", cancellationToken);

        return Uri.EscapeDataString(copy);
    }
}
