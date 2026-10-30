using Snail.MCP.E2E.Configuration;
using Snail.MCP.E2E.Tools;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.MCP.E2E.Extensions;

/// <summary>Composition root: which features the server has, and where it answers.</summary>
public static class EnvironmentServerExtensions
{
    extension(WebApplicationBuilder builder)
    {
        /// <summary>The tools, the environments and the settings, read from the settings file and the <c>SNAIL_MCP_E2E_</c> variables only.</summary>
        /// <remarks>Logging stays the web host's own, on standard output, where the container's log driver collects it.</remarks>
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
                .WithJobs("e2e");

            return builder;
        }
    }

    extension(WebApplication app)
    {
        /// <summary>The MCP endpoint and the health check Nomad polls.</summary>
        public WebApplication UseEnvironmentEndpoints()
        {
            app.MapMcp("/mcp");
            app.MapGet("/healthz", () => Results.Ok());

            return app;
        }
    }
}
