using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Configuration;
using Snail.MCP.E2E.Tools;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.Hosting.Sessions;
using Snail.Sdk.Mcp.Http;

namespace Snail.MCP.E2E.Extensions;

/// <summary>Composition root: which features the server has, and where it answers.</summary>
public static class EnvironmentServerExtensions
{
    extension(WebApplicationBuilder builder)
    {
        /// <summary>The tools, the environments and the settings, read from the settings file and the <c>SNAIL_MCP_E2E_</c> variables only.</summary>
        /// <remarks>
        /// <para>The host is Snail.Sdk.Mcp.Http's, at the endpoint the settings name: log lines on stderr, where the container's log driver or launchd collects them.</para>
        /// <para>
        /// A session for every client (<c>WithLocalHttpTransport</c>), as on every server of the fleet. The MCP SDK's own
        /// <c>WithHttpTransport()</c>, which the server was written with, is stateless by default since 2.2: a client that
        /// opens with initialize and subscribes to a raise or a test run is never told of its end, since a stateless
        /// server keeps no stream to tell it on. The copy, the grant and the owner of a job are read from each request's
        /// address either way.
        /// </para>
        /// </remarks>
        public WebApplicationBuilder ConfigureEnvironmentServer()
        {
            builder.Services
                .AddOptions<E2EOptions>()
                .Bind(E2ESettings.Read())
                .ValidateOnStart();

            builder.Services.AddEnvironments();
            builder.Services.AddSingleton<EnvironmentWork>();

            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = Snail.Sdk.Mcp.Hosting.ServerIdentity.Of(typeof(EnvironmentServerExtensions).Assembly);
                    options.ServerInstructions = ServerGuidance.Instructions;
                })
                .ExplainFailures()
                .WithLocalHttpTransport()
                .WithSessionGrant()
                .WithTools<EnvironmentTools>()
                .WithTools<DiagnosticsTools>()
                .WithJobs("e2e", JobOwners.TaskOfCopy(WorkspacesOf, TaskEnvironment.Of, environment => environment.TaskId));

            return builder;
        }
    }

    /// <summary>The folder the task's copies lie under, which names the owner of a request's jobs: the task whose copy the session's address names.</summary>
    /// <remarks>One server serves every task of the platform; a session of one task must not read another's test output.</remarks>
    private static string WorkspacesOf(IServiceProvider services) =>
        services.GetRequiredService<IOptions<E2EOptions>>().Value.WorkspacesRoot;

    extension(WebApplication app)
    {
        /// <summary>The MCP endpoint behind the refusal of a browser's request, and the health check Nomad polls.</summary>
        /// <remarks>
        /// A page in a browser on a machine that reaches the port always sends <c>Origin</c>, and an agent's client never
        /// does: <c>MapLocalMcp</c> answers such a request 403 before any tool runs.
        /// </remarks>
        public WebApplication UseEnvironmentEndpoints()
        {
            app.MapLocalMcp();
            app.MapHealth();

            return app;
        }
    }
}
