using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.E2E.Application;

namespace Snail.MCP.E2E.Tools;

/// <summary>The task's environment: raise, look, call, read, test and stop.</summary>
[McpServerToolType]
public sealed class EnvironmentTools(TaskEnvironments environments, EnvironmentWork work)
{
    /// <summary>Raises the environment as a job and waits for the app within the wait.</summary>
    [McpServerTool(Name = "e2e_up", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Up)]
    public Task<CallToolResult> UpAsync(
        [Description(ToolDescriptions.Wait)] int wait = 60,
        CancellationToken cancellationToken = default) =>
        work.RaiseAsync(wait, cancellationToken);

    /// <summary>Says where the environment stands.</summary>
    [McpServerTool(Name = "e2e_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Status)]
    public async Task<CallToolResult> StatusAsync(CancellationToken cancellationToken = default) =>
        ToolAnswer.From(await environments.StatusAsync(cancellationToken), AnswerFields.Of);

    /// <summary>Stops the environment.</summary>
    [McpServerTool(Name = "e2e_down", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Down)]
    public async Task<CallToolResult> DownAsync(CancellationToken cancellationToken = default) =>
        ToolAnswer.From(await environments.DownAsync(cancellationToken), AnswerFields.Stopped);

    /// <summary>Sends one request to the app.</summary>
    [McpServerTool(Name = "e2e_http", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Http)]
    public async Task<CallToolResult> HttpAsync(
        [Description(ToolDescriptions.HttpMethod)] string method,
        [Description(ToolDescriptions.HttpPath)] string path,
        [Description(ToolDescriptions.HttpBody)] string? body = null,
        [Description(ToolDescriptions.HttpContentType)] string? contentType = null,
        [Description(ToolDescriptions.HttpHeaders)] string[]? headers = null,
        CancellationToken cancellationToken = default)
    {
        var call = Calls.Of(method, path, body, contentType, headers);
        if (!call.TryGet(out var asked))
            return ToolAnswer.Failed(call);

        return ToolAnswer.From(await environments.CallAsync(asked, cancellationToken), AnswerFields.Of);
    }

    /// <summary>Reads the end of a part's output.</summary>
    [McpServerTool(Name = "e2e_logs", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Logs)]
    public async Task<CallToolResult> LogsAsync(
        [Description(ToolDescriptions.LogsPart)] string part,
        [Description(ToolDescriptions.LogsBytes)] int? bytes = null,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(await environments.LogsAsync(part, bytes, cancellationToken), AnswerFields.Of);

    /// <summary>Runs the end-to-end tests as a job and answers within the wait.</summary>
    [McpServerTool(Name = "e2e_test", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Test)]
    public Task<CallToolResult> TestAsync(
        [Description(ToolDescriptions.TestFilter)] string? filter = null,
        [Description(ToolDescriptions.Wait)] int wait = 60,
        CancellationToken cancellationToken = default) =>
        work.TestAsync(filter, wait, cancellationToken);
}
