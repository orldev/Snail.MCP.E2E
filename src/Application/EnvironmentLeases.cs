namespace Snail.MCP.E2E.Application;

/// <summary>Who works on each environment, one at a time: a raise, a test run, a stop or a sweep, in the order they came.</summary>
/// <remarks>
/// Two calls on one environment used to step on each other: a second test run purged the first one's job and the first
/// read the second's output, and a raise during a test run restarted the app under the tests. Each environment now has a
/// line: the first lease in it works, the others wait their turn. A stop cancels everyone in the line and goes last, so
/// nothing raised before it outlives it; a sweep only takes an environment nobody is in line for.
/// </remarks>
public sealed class EnvironmentLeases
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<Lease>> _lines = new(StringComparer.Ordinal);

    /// <summary>A lease at the end of the environment's line; its turn comes once everyone before it has let go.</summary>
    public Lease Join(TaskEnvironment name, string kind)
    {
        lock (_gate)
            return Enqueue(name, kind);
    }

    /// <summary>A lease that works at once, or null when somebody works on the environment or waits to.</summary>
    public Lease? TryTake(TaskEnvironment name, string kind)
    {
        lock (_gate)
            return _lines.ContainsKey(name.Value) ? null : Enqueue(name, kind);
    }

    /// <summary>The newest lease in the environment's line, or null when the line is empty.</summary>
    public Lease? Last(TaskEnvironment name)
    {
        lock (_gate)
            return _lines.TryGetValue(name.Value, out var line) ? line[^1] : null;
    }

    /// <summary>Whether anybody works on the environment or waits to.</summary>
    public bool IsBusy(TaskEnvironment name)
    {
        lock (_gate)
            return _lines.ContainsKey(name.Value);
    }

    /// <summary>Tells everyone in the environment's line to stop.</summary>
    public void CancelAll(TaskEnvironment name)
    {
        List<Lease> line;

        lock (_gate)
            line = _lines.TryGetValue(name.Value, out var held) ? [.. held] : [];

        foreach (var lease in line)
            lease.Cancel();
    }

    /// <summary>Takes the lease out of its line, wherever it stood, and gives the turn to the next one.</summary>
    public void Release(Lease lease)
    {
        lock (_gate)
        {
            if (_lines.TryGetValue(lease.Name.Value, out var line) && line.Remove(lease))
            {
                if (line.Count == 0)
                    _lines.Remove(lease.Name.Value);
                else
                    line[0].Begin();
            }
        }

        lease.Dispose();
    }

    private Lease Enqueue(TaskEnvironment name, string kind)
    {
        var lease = new Lease(name, kind);

        if (!_lines.TryGetValue(name.Value, out var line))
            _lines[name.Value] = line = [];

        line.Add(lease);
        if (line.Count == 1)
            lease.Begin();

        return lease;
    }
}

/// <summary>One place in an environment's line: what it is for, the job it is read at, when its turn comes and how it is told to stop.</summary>
public sealed class Lease : IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _cancelling = new();
    private readonly TaskCompletionSource _turn = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isEnded;

    internal Lease(TaskEnvironment name, string kind)
    {
        Name = name;
        Kind = kind;
    }

    /// <summary>The environment the lease is on.</summary>
    public TaskEnvironment Name { get; }

    /// <summary>What the lease is for: one of <see cref="LeaseKinds"/>.</summary>
    public string Kind { get; }

    /// <summary>The job the work is read at, once it runs as one.</summary>
    public string? Job { get; set; }

    /// <summary>Cancelled when somebody stops the environment.</summary>
    public CancellationToken Stopping => _cancelling.Token;

    /// <summary>Waits until everyone before this lease has let go, or until it is told to stop.</summary>
    public async Task TurnAsync(CancellationToken cancellationToken)
    {
        using var either = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Stopping);

        await _turn.Task.WaitAsync(either.Token);
    }

    /// <summary>Tells the work under this lease to stop; a lease already let go is left as it was.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (!_isEnded)
                _cancelling.Cancel();
        }
    }

    /// <summary>Ends the lease; <see cref="EnvironmentLeases.Release"/> does this after taking it out of its line.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_isEnded)
                return;

            _isEnded = true;
            _cancelling.Dispose();
        }
    }

    internal void Begin() => _turn.TrySetResult();
}

/// <summary>What a lease on an environment is for.</summary>
public static class LeaseKinds
{
    /// <summary>A raise: e2e_up.</summary>
    public const string Up = "up";

    /// <summary>A test run: e2e_test.</summary>
    public const string Test = "test";

    /// <summary>A stop: e2e_down.</summary>
    public const string Down = "down";

    /// <summary>A stop of an environment nobody used for the idle span.</summary>
    public const string Sweep = "sweep";
}
