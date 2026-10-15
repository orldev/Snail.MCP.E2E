using System.Collections.Concurrent;

namespace Snail.MCP.E2E.Application;

/// <summary>When each environment was last called, so one nobody uses is stopped rather than kept running.</summary>
/// <remarks>Kept in memory: after a restart every running environment counts as used at the server's start, and lives one more idle span.</remarks>
public sealed class EnvironmentUse(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _last = new(StringComparer.Ordinal);

    /// <summary>Notes a call to the environment now.</summary>
    public void Touch(TaskEnvironment name) => _last[name.Value] = clock.GetUtcNow();

    /// <summary>Forgets a stopped environment.</summary>
    public void Forget(TaskEnvironment name) => _last.TryRemove(name.Value, out _);

    /// <summary>Whether the environment went unused for longer than <paramref name="idle"/>; one never seen counts as used now.</summary>
    public bool IsIdle(TaskEnvironment name, TimeSpan idle)
    {
        DateTimeOffset last = _last.GetOrAdd(name.Value, _ => clock.GetUtcNow());

        return clock.GetUtcNow() - last > idle;
    }
}
