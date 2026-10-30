namespace Snail.MCP.E2E.Domain;

/// <summary>Where an environment stands.</summary>
public enum EnvironmentState
{
    /// <summary>The app answers its health check.</summary>
    Ready,

    /// <summary>The environment runs, but the app does not answer yet: it restores, builds or starts.</summary>
    Starting,

    /// <summary>A part stopped and will not start again on its own: the app's build or start, a service, or the copy of the project failed.</summary>
    Failed,

    /// <summary>No environment runs for the task.</summary>
    Stopped
}

/// <summary>An environment as a caller sees it: its state, the app's address inside its network, and each part's state.</summary>
public sealed record EnvironmentStatus(string Name, EnvironmentState State, string? AppUrl, IReadOnlyList<PartState> Parts)
{
    /// <summary>Seconds the call waited for the app.</summary>
    public double Seconds { get; init; }

    /// <summary>The end of the app's output when it failed, so the reason is in the same answer.</summary>
    public string? Output { get; init; }
}

/// <summary>One part of an environment as Nomad runs it: the app or a service, its task state, and how often it restarted.</summary>
public sealed record PartState(string Name, string State, int Restarts, bool HasFailed);
