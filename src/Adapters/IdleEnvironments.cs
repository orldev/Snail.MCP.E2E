using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Application.Ports;
using Snail.MCP.E2E.Configuration;

namespace Snail.MCP.E2E.Adapters;

/// <summary>Stops every environment nobody called for longer than the idle span.</summary>
/// <remarks>
/// <para>
/// The platform tells an MCP server nothing when a task ends, so an environment a task forgot to stop would run until
/// the node is rebooted. This is the one place that ends it.
/// </para>
/// <para>
/// An environment somebody is in line for is left alone, and one taken is looked at again under its lease: a raise that
/// came between the look and the stop was stopped under the caller. One environment Nomad fails to stop no longer ends
/// the sweep for the rest.
/// </para>
/// </remarks>
public sealed class IdleEnvironments(IEnvironmentJobs jobs, EnvironmentUse use, EnvironmentLeases leases, IOptions<E2EOptions> options, TimeProvider clock, ILogger<IdleEnvironments> logger) : BackgroundService
{
    /// <summary>How often the environments are looked at.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await StopLeftTestRunsAsync(stoppingToken);
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

    /// <summary>Stops the test runs an earlier start of the server left going: nobody waits for them now.</summary>
    public async Task StopLeftTestRunsAsync(CancellationToken cancellationToken)
    {
        try
        {
            int stopped = await jobs.StopTestRunsAsync(cancellationToken);
            if (stopped > 0)
                Log.LeftTestRunsStopped(logger, stopped);
        }
        catch (SchedulerException exception)
        {
            Log.SweepFailed(logger, exception);
        }
    }

    /// <summary>Stops the idle environments once.</summary>
    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        var idle = TimeSpan.FromMinutes(options.Value.IdleMinutes);

        foreach (var name in await jobs.ListAsync(cancellationToken))
        {
            if (use.IsIdle(name, idle))
                await StopIdleAsync(name, idle, cancellationToken);
        }
    }

    private async Task StopIdleAsync(TaskEnvironment name, TimeSpan idle, CancellationToken cancellationToken)
    {
        if (leases.TryTake(name, LeaseKinds.Sweep) is not { } lease)
            return;

        try
        {
            if (!use.IsIdle(name, idle))
                return;

            await jobs.HasStoppedAsync(name, cancellationToken);
            use.Forget(name);
            Log.IdleStopped(logger, name.Value, options.Value.IdleMinutes);
        }
        catch (SchedulerException exception)
        {
            Log.IdleStopFailed(logger, name.Value, exception);
        }
        finally
        {
            leases.Release(lease);
        }
    }
}
