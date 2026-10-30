using System.Collections;
using System.Globalization;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.E2E.Configuration;

/// <summary>The settings <see cref="E2EOptions"/> is bound from: the server's settings file, then its <c>SNAIL_MCP_E2E_</c> variables, which win.</summary>
/// <remarks>
/// Built apart from the web host's own configuration on purpose: an <c>appsettings.json</c> or a variable of whoever
/// started the server that happens to share a name is not a setting of this server and must not become one by accident.
/// </remarks>
public static class E2ESettings
{
    /// <summary>The server's short name, which names its settings file and the prefix of its variables.</summary>
    public const string Name = "e2e";

    /// <summary>The port the server listens on, on every interface: Port (<c>SNAIL_MCP_E2E_PORT</c>), read before the host exists.</summary>
    public static int Port() =>
        int.TryParse(Read()[nameof(E2EOptions.Port)], NumberStyles.None, CultureInfo.InvariantCulture, out int port) ? port : new E2EOptions().Port;

    /// <summary>The settings as they stand: the settings file found, then the process's variables.</summary>
    public static IConfiguration Read() => Read(ServerSettings.FileOf(Name), Environment.GetEnvironmentVariables());

    /// <summary>The settings of <paramref name="file"/>, when there is one, under the server's variables among <paramref name="variables"/>.</summary>
    public static IConfiguration Read(string? file, IDictionary variables) =>
        new ConfigurationBuilder()
            .AddServerSettings(Name, file, variables)
            .Build();
}
