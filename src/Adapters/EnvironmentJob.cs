using System.Text.Json.Nodes;
using Snail.MCP.E2E.Configuration;

namespace Snail.MCP.E2E.Adapters;

/// <summary>An environment and a test run as Nomad jobs: one group, the services and the app side by side in the environment's network.</summary>
/// <remarks>
/// <para>
/// The app is built where it runs, from a fresh copy of the working copy inside its own container: the copy is mounted
/// read-only from the same volume the workers write, and a build must not leave its bin and obj in the task's files.
/// The copy's own bin and obj are dropped first: they carry paths of another machine, which a restore here trips over.
/// </para>
/// <para>
/// The copy is mounted with host-to-task propagation: on a stand where the workspaces are a named Docker volume, the host
/// volume lies inside Docker's own data root, and Docker refuses a private bind mount from there — the first raise on
/// the stand failed on exactly that. On a cluster's plain host path the mode changes nothing.
/// </para>
/// <para>
/// Every container sits on one internal network. Packages come through the restore proxy, which lets only NuGet's hosts
/// through, and are kept in a shared volume so the next raise restores from disk. The app is started with
/// <c>--no-launch-profile</c>: a launch profile would choose its own address and environment over the ones given here.
/// </para>
/// <para>
/// Nomad interpolates <c>${…}</c> in every string of a task's config and environment, so the shell's own <c>${…}</c> is
/// written <c>$${…}</c>; unescaped, the test run failed validation before it started. Every value that comes from the
/// manifest or the caller is escaped the same way: unescaped, <c>${attr.unique.hostname}</c> in a setting read the
/// node's attributes, and a value that meant <c>${…}</c> literally arrived changed.
/// </para>
/// </remarks>
public static class EnvironmentJob
{
    /// <summary>Where the package volume is mounted inside a container that builds.</summary>
    public const string PackagesFolder = "/nuget";

    /// <summary>Nanoseconds in one second, the unit Nomad's API counts durations in.</summary>
    private const long Second = 1_000_000_000;

    private const string Build =
        """
        set -e
        rm -rf /tmp/src && mkdir -p /tmp/src && cp -a "$S3L_COPY/." /tmp/src/
        find /tmp/src -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
        cd /tmp/src
        """;

    private const string Run = $"""
        {Build}
        exec dotnet run --project "$S3L_PROJECT" -c Release --no-launch-profile --urls "http://0.0.0.0:$S3L_PORT"
        """;

    private const string Test = $$"""
        {{Build}}
        dotnet test "$S3L_PROJECT" --nologo --logger "console;verbosity=normal" $${S3L_FILTER:+--filter "$S3L_FILTER"}
        """;

    /// <summary>The environment's job: a service job of one group, which Nomad restarts an app in twice before it gives up.</summary>
    public static JsonObject Of(EnvironmentPlan plan, E2EOptions options)
    {
        var manifest = plan.Manifest;
        var services = manifest.Services.Keys.ToList();
        JsonArray tasks = [.. manifest.Services.Select(service => ServiceTask(plan.Name, service.Key, service.Value, manifest.App, services, options))];

        tasks.Add(BuildTask(Parts.App, plan.Name, plan.Copy, Run, AppEnv(plan, options), options));

        var job = new JsonObject
        {
            ["ID"] = plan.Name.Value,
            ["Name"] = plan.Name.Value,
            ["Type"] = "service",
            ["Datacenters"] = new JsonArray(options.Datacenter),
            ["Meta"] = new JsonObject { ["s3l.e2e.copy"] = plan.Copy },
            ["TaskGroups"] = new JsonArray(Group("environment", tasks, options, attempts: 2))
        };

        return job;
    }

    /// <summary>The test run's job: a batch job that runs once, never restarted, in the environment's network.</summary>
    /// <remarks>Each run carries a mark of its own: Nomad keeps a batch job that registers unchanged, and the tests would not run again.</remarks>
    public static JsonObject TestsOf(TestPlan plan, string run, E2EOptions options)
    {
        var manifest = plan.Manifest;
        var services = manifest.Services.Keys.ToList();
        var env = Common(plan.Name, plan.Copy, plan.Tests.Project, manifest, options);

        foreach (var setting in Placeholders.Fill(plan.Tests.Env, plan.Name, manifest.App, services))
            env[setting.Key] = setting.Value;

        if (plan.Filter is { } filter)
            env["S3L_FILTER"] = filter;

        var job = new JsonObject
        {
            ["ID"] = plan.Name.Tests,
            ["Name"] = plan.Name.Tests,
            ["Type"] = "batch",
            ["Datacenters"] = new JsonArray(options.Datacenter),
            ["Meta"] = new JsonObject { ["s3l.e2e.copy"] = plan.Copy, ["s3l.e2e.run"] = run },
            ["TaskGroups"] = new JsonArray(Group(Parts.Tests, [BuildTask(Parts.Tests, plan.Name, plan.Copy, Test, env, options)], options, attempts: 0))
        };

        return job;
    }

    private static JsonObject Group(string name, JsonArray tasks, E2EOptions options, int attempts) => new()
    {
        ["Name"] = name,
        ["Count"] = 1,
        ["RestartPolicy"] = new JsonObject { ["Attempts"] = attempts, ["Interval"] = 300 * Second, ["Delay"] = 5 * Second, ["Mode"] = "fail" },
        ["ReschedulePolicy"] = new JsonObject { ["Attempts"] = 0, ["Unlimited"] = false },
        ["Volumes"] = new JsonObject
        {
            ["workspaces"] = new JsonObject { ["Name"] = "workspaces", ["Type"] = "host", ["Source"] = options.WorkspacesVolume, ["ReadOnly"] = true }
        },
        ["Tasks"] = tasks
    };

    private static JsonObject ServiceTask(TaskEnvironment name, string service, ServicePart part, AppPart app, IReadOnlyCollection<string> services, E2EOptions options)
    {
        var config = new JsonObject
        {
            ["image"] = Literal(part.Image),
            ["network_mode"] = options.Network,
            ["network_aliases"] = new JsonArray(name.HostOf(service))
        };

        if (part.Args.Count > 0)
            config["args"] = new JsonArray([.. part.Args.Select(argument => (JsonNode)Literal(argument))]);

        return new JsonObject
        {
            ["Name"] = service,
            ["Driver"] = "docker",
            ["Config"] = config,
            ["Env"] = Env(Placeholders.Fill(part.Env, name, app, services)),
            ["Resources"] = new JsonObject { ["CPU"] = options.ServiceCpu, ["MemoryMB"] = options.ServiceMemoryMegabytes }
        };
    }

    private static JsonObject BuildTask(string task, TaskEnvironment name, string copy, string script, Dictionary<string, string> env, E2EOptions options) => new()
    {
        ["Name"] = task,
        ["Driver"] = "docker",
        ["Config"] = new JsonObject
        {
            ["image"] = options.SdkImage,
            ["network_mode"] = options.Network,
            ["network_aliases"] = new JsonArray(name.HostOf(task)),
            ["command"] = "sh",
            ["args"] = new JsonArray("-c", script),
            ["mounts"] = new JsonArray(new JsonObject { ["type"] = "volume", ["source"] = options.PackagesVolume, ["target"] = PackagesFolder, ["readonly"] = false })
        },
        ["VolumeMounts"] = new JsonArray(new JsonObject
        {
            ["Volume"] = "workspaces",
            ["Destination"] = options.WorkspacesRoot,
            ["ReadOnly"] = true,
            ["PropagationMode"] = "host-to-task"
        }),
        ["Env"] = Env(env),
        ["Resources"] = new JsonObject { ["CPU"] = options.AppCpu, ["MemoryMB"] = options.AppMemoryMegabytes }
    };

    private static Dictionary<string, string> AppEnv(EnvironmentPlan plan, E2EOptions options)
    {
        var manifest = plan.Manifest;
        var env = Common(plan.Name, plan.Copy, manifest.App.Project, manifest, options);

        env["S3L_PORT"] = manifest.App.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        env["ASPNETCORE_ENVIRONMENT"] = "Development";

        foreach (var setting in Placeholders.Fill(manifest.App.Env, plan.Name, manifest.App, [.. manifest.Services.Keys]))
            env[setting.Key] = setting.Value;

        return env;
    }

    /// <summary>What every container that builds is given: the copy, the project, the packages, and the proxy with the environment's own hosts kept off it.</summary>
    private static Dictionary<string, string> Common(TaskEnvironment name, string copy, string project, EnvironmentManifest manifest, E2EOptions options)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["S3L_COPY"] = copy,
            ["S3L_PROJECT"] = project,
            ["NUGET_PACKAGES"] = $"{PackagesFolder}/packages",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
        };

        if (options.RestoreProxy is not { Length: > 0 } proxy)
            return env;

        IReadOnlyList<string> local = ["localhost", "127.0.0.1", name.App, .. manifest.Services.Keys.Select(name.HostOf)];

        env["HTTP_PROXY"] = proxy;
        env["HTTPS_PROXY"] = proxy;
        env["NO_PROXY"] = string.Join(',', local);

        return env;
    }

    private static JsonObject Env(IReadOnlyDictionary<string, string> settings) =>
        new([.. settings.Select(setting => KeyValuePair.Create(setting.Key, (JsonNode?)Literal(setting.Value)))]);

    /// <summary>The value as Nomad is to pass it on, with every <c>${</c> kept from its interpolation.</summary>
    private static string Literal(string value) => value.Replace("${", "$${", StringComparison.Ordinal);
}
