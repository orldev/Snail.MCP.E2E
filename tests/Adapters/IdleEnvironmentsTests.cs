using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Adapters;
using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Configuration;
using Snail.MCP.E2E.Tests.Application;

namespace Snail.MCP.E2E.Tests.Adapters;

public sealed class IdleEnvironmentsTests
{
    private static readonly TaskEnvironment Shop = TaskEnvironment.Of("aa")!.Value;
    private static readonly TaskEnvironment Blog = TaskEnvironment.Of("bb")!.Value;

    private readonly FakeJobs _jobs = new() { Listed = [Shop, Blog] };
    private readonly Clock _clock = new();
    private readonly EnvironmentLeases _leases = new();
    private readonly EnvironmentUse _use;

    public IdleEnvironmentsTests() => _use = new EnvironmentUse(_clock);

    private IdleEnvironments Sweeper() =>
        new(_jobs, _use, _leases, Options.Create(new E2EOptions { IdleMinutes = 60 }), _clock, NullLogger<IdleEnvironments>.Instance);

    private void Unused(TimeSpan span)
    {
        _use.Touch(Shop);
        _use.Touch(Blog);
        _clock.Now += span;
    }

    [Fact]
    public async Task SweepAsync_UnusedPastTheIdleSpan_IsStopped()
    {
        Unused(TimeSpan.FromMinutes(61));

        await Sweeper().SweepAsync(CancellationToken.None);

        Assert.Equal(["e2e-aa", "e2e-bb"], _jobs.Stopped);
    }

    [Fact]
    public async Task SweepAsync_UsedWithinTheIdleSpan_IsLeftRunning()
    {
        Unused(TimeSpan.FromMinutes(59));

        await Sweeper().SweepAsync(CancellationToken.None);

        Assert.Empty(_jobs.Stopped);
    }

    /// <summary>A raise that came between the look and the stop used to be stopped under the caller.</summary>
    [Fact]
    public async Task SweepAsync_IdleButSomebodyInLine_IsLeftRunning()
    {
        Unused(TimeSpan.FromMinutes(61));
        var raise = _leases.Join(Shop, LeaseKinds.Up);

        await Sweeper().SweepAsync(CancellationToken.None);

        Assert.Equal(["e2e-bb"], _jobs.Stopped);
        _leases.Release(raise);
    }

    /// <summary>One environment Nomad failed to stop used to end the sweep for the rest.</summary>
    [Fact]
    public async Task SweepAsync_StopOfOneFails_StillStopsTheOthers()
    {
        Unused(TimeSpan.FromMinutes(61));
        _jobs.FailingStops.Add("e2e-aa");

        await Sweeper().SweepAsync(CancellationToken.None);

        Assert.Equal(["e2e-bb"], _jobs.Stopped);
        Assert.False(_leases.IsBusy(Shop));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
