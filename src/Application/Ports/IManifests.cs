namespace Snail.MCP.E2E.Application.Ports;

/// <summary>The environment manifest of a working copy.</summary>
public interface IManifests
{
    /// <summary>The manifest of the copy, or why there is none that can be raised.</summary>
    Task<Answer<EnvironmentManifest>> ReadAsync(string copy, CancellationToken cancellationToken);
}
