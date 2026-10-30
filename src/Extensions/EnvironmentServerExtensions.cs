using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Configuration;
using Snail.MCP.E2E.Tools;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.Http;

namespace Snail.MCP.E2E.Extensions;

/// <summary>Composition root: which features the server has, and where it answers.</summary>
public static class EnvironmentServerExtensions
{
    extension(WebApplicationBuilder builder)
    {
        /// <summary>The tools, the environments and the settings, read from the settings file and the <c>SNAIL_MCP_E2E_</c> variables only.</summary>
        /// <remarks>The host is Snail.Sdk.Mcp.Http's container host: log lines on stderr, where the container's log driver collects them.</remarks>
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
                .RefuseWhatWasNotGranted()
                .WithHttpTransport()
                .WithTools<EnvironmentTools>()
                .WithTools<DiagnosticsTools>()
                .WithJobs("e2e", OwnerOf);

            return builder;
        }
    }

    /// <summary>The owner of a request's jobs: the environment of the task whose copy the session's address names.</summary>
    /// <remarks>One server serves every task of the platform; a session of one task must not read another's test output.</remarks>
    private static string? OwnerOf(IServiceProvider services) =>
        services.GetService<SessionCopy>()?.Current().Value?.Environment.Value;

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
            app.MapGet("/healthz", () => Results.Ok());

            return app;
        }
    }
}
