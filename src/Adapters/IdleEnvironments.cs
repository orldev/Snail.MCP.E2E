using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Application.Ports;
using Snail.MCP.E2E.Configuration;

namespace Snail.MCP.E2E.Adapters;

/// <summary>Stops every environment nobody called for longer than the idle span.</summary>
/// <remarks>
/// The platform tells an MCP server nothing when a task ends, so an environment a task forgot to stop would run until
/// the node is rebooted. This is the one place that ends it.
/// </remarks>
public sealed class IdleEnvironments(IEnvironmentJobs jobs, EnvironmentUse use, IOptions<E2EOptions> options, TimeProvider clock, ILogger<IdleEnvironments> logger) : BackgroundService
{
    /// <summary>How often the environments are looked at.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period, clock);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (SchedulerException exception)
            {
                Log.SweepFailed(logger, exception);
            }
        }
    }

    /// <summary>Stops the idle environments once.</summary>
    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        var idle = TimeSpan.FromMinutes(options.Value.IdleMinutes);

        foreach (var name in await jobs.ListAsync(cancellationToken))
        {
            if (!use.IsIdle(name, idle))
                continue;

            await jobs.HasStoppedAsync(name, cancellationToken);
            use.Forget(name);
            Log.IdleStopped(logger, name.Value, options.Value.IdleMinutes);
        }
    }
}
