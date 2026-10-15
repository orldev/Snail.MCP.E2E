namespace Snail.MCP.E2E.Domain;

/// <summary>What an environment holds, as the project describes it in its manifest: the app, the services it needs, and its end-to-end tests.</summary>
public sealed record EnvironmentManifest(AppPart App, IReadOnlyDictionary<string, ServicePart> Services, TestPart? Tests);

/// <summary>The project's app: which project file runs, the port it listens on, the path answering its health, and its settings.</summary>
/// <remarks>A setting's value may name <c>{service}</c> for that service's host and <c>{app}</c> for the app's own address.</remarks>
public sealed record AppPart(string Project, int Port, string Health, IReadOnlyDictionary<string, string> Env);

/// <summary>One service the app needs, run from its image: a database, a broker, a cache.</summary>
public sealed record ServicePart(string Image, int? Port, IReadOnlyDictionary<string, string> Env, IReadOnlyList<string> Args);

/// <summary>The project's end-to-end tests: the test project, and the settings that point it at the running app.</summary>
public sealed record TestPart(string Project, IReadOnlyDictionary<string, string> Env);
