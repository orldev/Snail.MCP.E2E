using Snail.MCP.E2E.Adapters;
using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Application.Ports;
using Snail.MCP.E2E.Configuration;
using Snail.Toolkit.Nomad.Extensions;

namespace Snail.MCP.E2E.Extensions;

/// <summary>The environments feature: the session's copy, the manifest, Nomad, the app's address, the proxy and the sweep.</summary>
public static class EnvironmentRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Everything a tool reaches through <see cref="TaskEnvironments"/>, with Nomad at the address the settings name.</summary>
        public IServiceCollection AddEnvironments()
        {
            services.AddSingleton(TimeProvider.System);
            services.AddHttpContextAccessor();
            services.AddSingleton<SessionCopy>();
            services.AddSingleton<SessionGrant>();
            services.AddSingleton<EnvironmentUse>();
            services.AddSingleton<IManifests, FileManifests>();
            services.AddSingleton<IEnvironmentJobs, NomadEnvironmentJobs>();
            services.AddSingleton<IAppCalls, HttpAppCalls>();
            services.AddSingleton<TaskEnvironments>();

            services.AddNomad(nomad => nomad.Address = Snail.MCP.E2E.Configuration.E2ESettings.Read()[nameof(E2EOptions.NomadAddress)] ?? new E2EOptions().NomadAddress);
            services
                .AddHttpClient(HttpAppCalls.Client, client => client.Timeout = HttpAppCalls.Timeout)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });

            services.AddHostedService<RestoreProxy>();
            services.AddHostedService<IdleEnvironments>();

            return services;
        }
    }
}
