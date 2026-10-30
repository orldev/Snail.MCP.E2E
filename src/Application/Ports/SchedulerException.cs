namespace Snail.MCP.E2E.Application.Ports;

/// <summary>The scheduler environments run on did not answer, or refused what it was sent.</summary>
/// <remarks>
/// The one failure a port of <see cref="IEnvironmentJobs"/> throws: whatever the scheduler's client throws — a refused
/// connection, a timeout, an error status — arrives as this, so the tools answer it as data rather than letting the
/// MCP SDK turn it into an error with no words a caller can act on.
/// </remarks>
public sealed class SchedulerException : Exception
{
    /// <summary>A failure with no reason given.</summary>
    public SchedulerException()
    {
    }

    /// <summary>A failure, said in words.</summary>
    public SchedulerException(string message)
        : base(message)
    {
    }

    /// <summary>A failure, said in words, with the client's own failure under it.</summary>
    public SchedulerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
