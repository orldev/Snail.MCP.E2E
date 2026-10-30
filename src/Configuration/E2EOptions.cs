namespace Snail.MCP.E2E.Configuration;

/// <summary>Where task environments run, what they may reach, and how long the server waits for them.</summary>
/// <remarks>
/// Every limit stands between an app a model just changed and the node every other task shares: an app that never
/// answers, a build that never ends, an environment nobody stops. The defaults are the ones the tool descriptions name,
/// and a guard test fails when the two drift apart.
/// </remarks>
public sealed class E2EOptions
{
    /// <summary>Port the MCP endpoint and the health check listen on, on every interface: the container's published port is the boundary.</summary>
    public int Port { get; set; } = 8828;

    /// <summary>Nomad's HTTP address, which environments are raised and stopped through.</summary>
    public string NomadAddress { get; set; } = "http://nomad:4646";

    /// <summary>Datacenter environment jobs are placed in.</summary>
    public string Datacenter { get; set; } = "dc1";

    /// <summary>Folder the platform's working copies lie under, as this server sees them, read-only.</summary>
    public string WorkspacesRoot { get; set; } = "/workspaces";

    /// <summary>Nomad host volume holding the same working copies, mounted read-only into every environment.</summary>
    public string WorkspacesVolume { get; set; } = "workspaces";

    /// <summary>Docker network environments run in: internal, so an app reaches its own services and the restore proxy only.</summary>
    public string Network { get; set; } = "s3l-e2e";

    /// <summary>Image an app is built and run in, and its tests: Microsoft's own .NET SDK image.</summary>
    public string SdkImage { get; set; } = "mcr.microsoft.com/dotnet/sdk:10.0";

    /// <summary>Docker volume packages are kept in between environments, so a second raise restores from disk.</summary>
    public string PackagesVolume { get; set; } = "s3l-e2e-nuget";

    /// <summary>The restore proxy's address as an environment reaches it, such as http://mcp-e2e:3128; null leaves environments without one.</summary>
    public string? RestoreProxy { get; set; }

    /// <summary>Port the restore proxy listens on inside the server; 0 keeps it off.</summary>
    public int RestoreProxyPort { get; set; }

    /// <summary>Hosts the restore proxy lets through; every other host is refused.</summary>
    /// <remarks>An environment's network is closed but for packages: NuGet's API and the CDN it serves packages from.</remarks>
    public string[] RestoreHosts { get; set; } = ["api.nuget.org", "globalcdn.nuget.org", "www.nuget.org"];

    /// <summary>Path of the environment manifest inside a working copy.</summary>
    public string ManifestPath { get; set; } = ".snail/environment.json";

    /// <summary>Seconds an app gets to restore, build, start and answer its health check.</summary>
    public int StartSeconds { get; set; } = 300;

    /// <summary>Seconds a test run gets, restore and build included.</summary>
    public int TestSeconds { get; set; } = 900;

    /// <summary>Minutes an environment lives after its last call before it is stopped.</summary>
    public int IdleMinutes { get; set; } = 60;

    /// <summary>Bytes of a response body kept for the answer.</summary>
    public int ResponseBytes { get; set; } = 16384;

    /// <summary>Most bytes of one log stream a call may ask for.</summary>
    public int MaxLogBytes { get; set; } = 65536;

    /// <summary>Bytes of one log stream returned when the caller names none.</summary>
    public int DefaultLogBytes { get; set; } = 8192;

    /// <summary>Bytes of a test run's output kept for the answer.</summary>
    public int TestOutputBytes { get; set; } = 16384;

    /// <summary>Memory, in megabytes, the app's task may use while it builds and runs.</summary>
    public int AppMemoryMegabytes { get; set; } = 1536;

    /// <summary>Memory, in megabytes, one service of an environment may use.</summary>
    public int ServiceMemoryMegabytes { get; set; } = 512;

    /// <summary>CPU, in MHz as Nomad counts it, the app's task and a test run may use.</summary>
    public int AppCpu { get; set; } = 1000;

    /// <summary>CPU, in MHz as Nomad counts it, one service may use.</summary>
    public int ServiceCpu { get; set; } = 500;
}
