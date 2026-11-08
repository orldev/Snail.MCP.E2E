using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.E2E.Application.Ports;
using Snail.MCP.E2E.Configuration;

namespace Snail.MCP.E2E.Tools;

/// <summary>Whether the server can raise environments: the scheduler, the network and the proxy.</summary>
[McpServerToolType]
public sealed class DiagnosticsTools(IEnvironmentJobs jobs, IOptions<E2EOptions> options)
{
    /// <summary>Says whether environments can be raised.</summary>
    [McpServerTool(Name = "e2e_diagnose", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Diagnose)]
    public async Task<CallToolResult> DiagnoseAsync(CancellationToken cancellationToken = default)
    {
        var reached = await jobs.ReachAsync(cancellationToken);
        if (!reached.IsSuccess)
            return ToolAnswer.Failed(reached.Error!, reached.Hint, reached.Type);

        var state = new JsonObject
        {
            ["server"] = typeof(DiagnosticsTools).Assembly.GetName().Version?.ToString(3),
            ["nomad"] = reached.Value,
            ["network"] = options.Value.Network,
            ["workspaces"] = options.Value.WorkspacesRoot,
            ["restoreProxy"] = options.Value.RestoreProxy,
            ["restoreHosts"] = new JsonArray([.. options.Value.RestoreHosts.Select(host => JsonValue.Create(host))])
        };

        return ToolAnswer.Ok(state);
    }
}
