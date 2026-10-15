namespace Snail.MCP.E2E;

/// <summary>What the server writes to its log, as source-generated messages.</summary>
internal static partial class Log
{
    [LoggerMessage(1, LogLevel.Information, "The restore proxy listens on port {Port}")]
    public static partial void ProxyListening(ILogger logger, int port);

    [LoggerMessage(2, LogLevel.Information, "The restore proxy refused {Host}:{Port}")]
    public static partial void ProxyRefused(ILogger logger, string host, int port);

    [LoggerMessage(3, LogLevel.Debug, "A restore proxy connection ended")]
    public static partial void ProxyConnectionEnded(ILogger logger, Exception exception);

    [LoggerMessage(4, LogLevel.Information, "Stopped {Environment}: unused for {Minutes} minutes")]
    public static partial void IdleStopped(ILogger logger, string environment, int minutes);

    [LoggerMessage(5, LogLevel.Warning, "The sweep of idle environments failed")]
    public static partial void SweepFailed(ILogger logger, Exception exception);
}
