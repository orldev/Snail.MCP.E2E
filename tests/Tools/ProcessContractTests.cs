using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ModelContextProtocol.Client;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.JobClient;

namespace Snail.MCP.E2E.Tests.Tools;

/// <summary>
/// The program as the platform starts it — the build beside the tests, or the publication <c>SNAIL_E2E_SERVER</c>
/// names — asked over HTTP.
/// </summary>
/// <remarks>Nomad's address names a port nothing listens on: no environment is raised, and the tools that need the scheduler have to say so in an answer of their own.</remarks>
public sealed class ProcessContractTests : IAsyncLifetime
{
    public const string ServerVariable = "SNAIL_E2E_SERVER";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly Uri _address = new($"http://127.0.0.1:{FreePort()}");
    private readonly string _workspaces = TestFolder.Create();
    private Process _server = default!;

    public async Task InitializeAsync()
    {
        var (command, arguments) = ServerProgram.Command("Snail.MCP.E2E", ServerVariable);
        var start = new ProcessStartInfo(command, arguments)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        start.Environment["ASPNETCORE_URLS"] = _address.ToString();
        start.Environment["SNAIL_MCP_E2E_NOMAD_ADDRESS"] = $"http://127.0.0.1:{FreePort()}";
        start.Environment["SNAIL_MCP_E2E_WORKSPACES_ROOT"] = _workspaces;
        start.Environment[ServerSettings.FileVariableOf("e2e")] = ServerProgram.NoSettingsFile;

        _server = Process.Start(start)!;
        _server.OutputDataReceived += (_, _) => { };
        _server.ErrorDataReceived += (_, _) => { };
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();

        using var waiting = new CancellationTokenSource(Timeout);
        using var http = new HttpClient { BaseAddress = _address };

        while (true)
        {
            try
            {
                using var answer = await http.GetAsync(new Uri("/healthz", UriKind.Relative), waiting.Token);

                if (answer.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException) when (!_server.HasExited)
            {
            }

            await Task.Delay(100, waiting.Token);
        }
    }

    public Task DisposeAsync()
    {
        _server.Kill(entireProcessTree: true);
        _server.WaitForExit();
        _server.Dispose();
        Directory.Delete(_workspaces, recursive: true);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ListTools_StartedProgram_OffersEveryTool()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await SessionAsync(null, cancellation.Token);

        var tools = await session.ListToolsAsync(cancellationToken: cancellation.Token);

        Assert.Equal(["e2e_diagnose", "e2e_down", "e2e_http", "e2e_logs", "e2e_status", "e2e_test", "e2e_up"], tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>The guard rules of every Snail server, on the list a model receives.</summary>
    [Fact]
    public async Task Tools_AsListed_KeepTheGuardRules()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await SessionAsync(null, cancellation.Token);

        var tools = await session.ListToolsAsync(cancellationToken: cancellation.Token);

        Assert.Empty(ToolGuards.Problems(tools.Select(tool => tool.ProtocolTool)));
    }

    [Fact]
    public async Task CallTool_DiagnoseWithoutNomad_NamesTheAddressThatDidNotAnswer()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await SessionAsync(null, cancellation.Token);

        var result = await session.CallToolAsync("e2e_diagnose", cancellationToken: cancellation.Token);

        Assert.True(result.IsError);
        Assert.Contains("did not answer", result.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallTool_UpWithoutACopyInTheAddress_IsAToolErrorThatSaysWhereTheCopyComesFrom()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await SessionAsync(null, cancellation.Token);

        var result = await session.CallToolAsync("e2e_up", cancellationToken: cancellation.Token);

        Assert.True(result.IsError);
        Assert.Contains("?root=", result.Text(), StringComparison.Ordinal);
    }

    /// <summary>A raise is a job: a client of Snail.Sdk.Mcp.JobClient awaits it to its end, here Nomad's absence said in words.</summary>
    [Fact]
    public async Task CallJob_UpWithoutNomad_EndsWithTheAddressThatDidNotAnswer()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await SessionAsync($"root={await CopyAsync("0c2b9e1f", cancellation.Token)}", cancellation.Token);

        var report = await session.CallJobAsync("e2e_up", cancellationToken: cancellation.Token);

        Assert.Equal(JobState.Succeeded, report.State);
        Assert.StartsWith("job://e2e/up/", report.Uri, StringComparison.Ordinal);
        Assert.Contains("did not answer", report.Result!.Value.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>One server serves every task of the platform: a session of another task does not read a task's job, whatever URI it holds.</summary>
    [Fact]
    public async Task ReadJob_OfAnotherTask_IsUnknownToItsSessions()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var owner = await SessionAsync($"root={await CopyAsync("0c2b9e1f", cancellation.Token)}", cancellation.Token);
        await using var other = await SessionAsync($"root={await CopyAsync("4f1c", cancellation.Token)}", cancellation.Token);

        var report = await owner.CallJobAsync("e2e_up", cancellationToken: cancellation.Token);
        var read = await owner.ReadJobAsync(report.Uri, cancellation.Token);

        Assert.Equal(report.Uri, read.Uri);
        await Assert.ThrowsAnyAsync<Exception>(() => other.ReadJobAsync(report.Uri, cancellation.Token));
    }

    [Fact]
    public async Task CallTool_ToolNotGrantedToTheSession_IsRefusedByName()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var session = await SessionAsync("tool=e2e_status", cancellation.Token);

        var result = await session.CallToolAsync("e2e_down", cancellationToken: cancellation.Token);

        Assert.True(result.IsError);
        Assert.Contains("'e2e_down' was not granted", result.Text(), StringComparison.Ordinal);
    }

    /// <summary>A step's copy of a task under the workspaces, with a manifest, as the address names it.</summary>
    private async Task<string> CopyAsync(string task, CancellationToken cancellationToken)
    {
        string copy = Path.Combine(_workspaces, task, "step-1");
        Directory.CreateDirectory(Path.Combine(copy, ".snail"));
        await File.WriteAllTextAsync(Path.Combine(copy, ".snail", "environment.json"), """{ "app": { "project": "src/Api/Api.csproj" } }""", cancellationToken);

        return Uri.EscapeDataString(copy);
    }

    private Task<McpClient> SessionAsync(string? query, CancellationToken cancellationToken) =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(_address, query is null ? "/mcp" : $"/mcp?{query}") }), cancellationToken: cancellationToken);

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
