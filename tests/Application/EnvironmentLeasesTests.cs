using Snail.MCP.E2E.Application;

namespace Snail.MCP.E2E.Tests.Application;

public sealed class EnvironmentLeasesTests
{
    private static readonly TaskEnvironment Shop = TaskEnvironment.Of("0c2b9e1f")!.Value;

    private readonly EnvironmentLeases _leases = new();

    [Fact]
    public async Task Join_SecondOnOneEnvironment_WaitsUntilTheFirstLetsGo()
    {
        var first = _leases.Join(Shop, LeaseKinds.Up);
        var second = _leases.Join(Shop, LeaseKinds.Test);
        var secondTurn = second.TurnAsync(CancellationToken.None);

        await first.TurnAsync(CancellationToken.None);
        bool hadToWait = !secondTurn.IsCompleted;
        _leases.Release(first);
        await secondTurn.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(hadToWait);
    }

    [Fact]
    public async Task Join_TwoEnvironments_WorkAtOnce()
    {
        _leases.Join(Shop, LeaseKinds.Up);
        var other = _leases.Join(TaskEnvironment.Of("4f1c")!.Value, LeaseKinds.Up);

        await other.TurnAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TryTake_EnvironmentSomebodyWaitsFor_IsNull()
    {
        _leases.Join(Shop, LeaseKinds.Test);

        Assert.Null(_leases.TryTake(Shop, LeaseKinds.Sweep));
    }

    [Fact]
    public void CancelAll_WorkingAndWaiting_AreBothToldToStop()
    {
        var working = _leases.Join(Shop, LeaseKinds.Up);
        var waiting = _leases.Join(Shop, LeaseKinds.Test);

        _leases.CancelAll(Shop);

        Assert.True(working.Stopping.IsCancellationRequested);
        Assert.True(waiting.Stopping.IsCancellationRequested);
    }

    [Fact]
    public async Task Release_WaiterThatLeftBeforeItsTurn_PassesTheTurnOnToTheNext()
    {
        var first = _leases.Join(Shop, LeaseKinds.Up);
        var leaving = _leases.Join(Shop, LeaseKinds.Test);
        var last = _leases.Join(Shop, LeaseKinds.Down);

        _leases.Release(leaving);
        _leases.Release(first);

        await last.TurnAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(last, _leases.Last(Shop));
    }

    [Fact]
    public void Release_LastOneOut_LeavesTheEnvironmentFree()
    {
        var only = _leases.Join(Shop, LeaseKinds.Up);

        _leases.Release(only);

        Assert.False(_leases.IsBusy(Shop));
    }

    [Fact]
    public void Cancel_LeaseAlreadyLetGo_ChangesNothing()
    {
        var only = _leases.Join(Shop, LeaseKinds.Up);
        _leases.Release(only);

        only.Cancel();
    }
}
