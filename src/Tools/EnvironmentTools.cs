using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.E2E.Application;

namespace Snail.MCP.E2E.Tools;

/// <summary>The task's environment: raise, look, call, read, test and stop.</summary>
[McpServerToolType]
public sealed class EnvironmentTools(TaskEnvironments environments)
{
    /// <summary>Raises the environment and waits for the app.</summary>
    [McpServerTool(Name = "e2e_up", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Up)]
    public async Task<CallToolResult> UpAsync(CancellationToken cancellationToken = default) =>
        ToolResponse.From(await environments.UpAsync(cancellationToken));

    /// <summary>Says where the environment stands.</summary>
    [McpServerTool(Name = "e2e_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Status)]
    public async Task<CallToolResult> StatusAsync(CancellationToken cancellationToken = default) =>
        ToolResponse.From(await environments.StatusAsync(cancellationToken));

    /// <summary>Stops the environment.</summary>
    [McpServerTool(Name = "e2e_down", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Down)]
    public async Task<CallToolResult> DownAsync(CancellationToken cancellationToken = default) =>
        ToolResponse.From(await environments.DownAsync(cancellationToken));

    /// <summary>Sends one request to the app.</summary>
    [McpServerTool(Name = "e2e_http", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Http)]
    public async Task<CallToolResult> HttpAsync(
        [Description(ToolDescriptions.HttpMethod)] string method,
        [Description(ToolDescriptions.HttpPath)] string path,
        [Description(ToolDescriptions.HttpBody)] string? body = null,
        [Description(ToolDescriptions.HttpContentType)] string? contentType = null,
        [Description(ToolDescriptions.HttpHeaders)] string[]? headers = null,
        CancellationToken cancellationToken = default) =>
        ToolResponse.From(await environments.CallAsync(Calls.Of(method, path, body, contentType, headers), cancellationToken));

    /// <summary>Reads the end of a part's output.</summary>
    [McpServerTool(Name = "e2e_logs", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Logs)]
    public async Task<CallToolResult> LogsAsync(
        [Description(ToolDescriptions.LogsPart)] string part,
        [Description(ToolDescriptions.LogsBytes)] int? bytes = null,
        CancellationToken cancellationToken = default) =>
        ToolResponse.From(await environments.LogsAsync(part, bytes, cancellationToken));

    /// <summary>Runs the end-to-end tests.</summary>
    [McpServerTool(Name = "e2e_test", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Test)]
    public async Task<CallToolResult> TestAsync(
        [Description(ToolDescriptions.TestFilter)] string? filter = null,
        CancellationToken cancellationToken = default) =>
        ToolResponse.From(await environments.TestAsync(filter, cancellationToken));
}
