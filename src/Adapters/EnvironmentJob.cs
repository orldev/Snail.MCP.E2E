using System.Text.Json.Nodes;
using Snail.MCP.E2E.Configuration;

namespace Snail.MCP.E2E.Adapters;

/// <summary>An environment and a test run as Nomad jobs: one group, the services and the app side by side in the environment's network.</summary>
/// <remarks>
/// <para>
/// The app and the tests run the project's own code, so they see nothing of other tasks. A first task of the group,
/// <see cref="Parts.Prepare"/>, is this server's script: it alone mounts the workspaces, and copies the task's copy into
/// the allocation's own folder, without its bin and obj — they carry paths of another machine, which a restore here trips
/// over. The app builds from there. Mounting every workspace into the app once let one task's app read every other
/// task's files.
/// </para>
/// <para>
/// The workspaces are mounted with host-to-task propagation: on a stand where they are a named Docker volume, the host
/// volume lies inside Docker's own data root, and Docker refuses a private bind mount from there — the first raise on
/// the stand failed on exactly that. On a cluster's plain host path the mode changes nothing.
/// </para>
/// <para>
/// Packages are the task's own too. They used to sit in one folder every app wrote to, so one task's build could change
/// a package another task's build then ran. Now the preparing task puts the task's packages into the allocation, and
/// <see cref="Parts.Keep"/>, once the app has stopped, writes them back to the task's folder of the package volume — by
/// copying into a fresh folder and renaming it, so a link the app left there points nowhere it can write. Only these two
/// scripts mount the package volume; a folder untouched for a week is dropped.
/// </para>
/// <para>
/// Every container sits on one internal network. Packages come through the restore proxy, which lets only NuGet's hosts
/// through. The app is started with <c>--no-launch-profile</c>: a launch profile would choose its own address and
/// environment over the ones given here.
/// </para>
/// <para>
/// Nomad interpolates <c>${…}</c> in every string of a task's config, so the shell's own <c>${…}</c> is written
/// <c>$${…}</c>; unescaped, the test run failed validation before it started. A service's image and arguments from the
/// manifest are escaped the same way. A task's environment has no such escape: on Nomad 1.10.5 <c>$${node.unique.id}</c>
/// in a setting arrived as <c>$</c> and the node's id. So no setting and no test filter holding <c>${</c> reaches a job —
/// <see cref="ManifestRules"/> refuses the one, the test run the other.
/// </para>
/// </remarks>
public static class EnvironmentJob
{
    /// <summary>Where the package volume is mounted inside the preparing and keeping tasks.</summary>
    public const string PackagesFolder = "/nuget";

    /// <summary>Where the task's copy and its packages are inside the allocation every task of the group shares.</summary>
    public const string AllocationFolder = "/alloc";

    /// <summary>The line a test run prints when the tests have ended, before it waits to be stopped with its output kept.</summary>
    public const string TestsEnded = "S3L_TESTS_ENDED";

    /// <summary>How long a test run that ended waits for the server to read it and stop it.</summary>
    /// <remarks>Long enough for a server that restarted to be up again; the sweep at its start stops the run.</remarks>
    public const int HoldSeconds = 600;

    /// <summary>Nanoseconds in one second, the unit Nomad's API counts durations in.</summary>
    private const long Second = 1_000_000_000;

    private const string Prepare =
        """
        set -e
        rm -rf /alloc/src /alloc/nuget
        mkdir -p /alloc/src /alloc/nuget /nuget/tasks
        cp -a "$S3L_COPY/." /alloc/src/
        find /alloc/src -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
        if [ -d "/nuget/tasks/$S3L_TASK" ]; then cp -a "/nuget/tasks/$S3L_TASK/." /alloc/nuget/; fi
        find /nuget/tasks -mindepth 1 -maxdepth 1 -type d -mtime +7 -exec rm -rf {} + || true
        """;

    private const string Keep =
        """
        set -e
        [ -d /alloc/nuget ] || exit 0
        fresh="/nuget/tasks/.$S3L_TASK.$NOMAD_ALLOC_ID"
        rm -rf "$fresh" && mkdir -p "$fresh"
        cp -a /alloc/nuget/. "$fresh/"
        rm -rf "/nuget/tasks/$S3L_TASK"
        mv -T "$fresh" "/nuget/tasks/$S3L_TASK" || rm -rf "$fresh"
        """;

    private const string Build =
        """
        set -e
        rm -rf /tmp/src && mkdir -p /tmp/src && cp -a /alloc/src/. /tmp/src/
        cd /tmp/src
        """;

    private const string Run = $"""
        {Build}
        exec dotnet run --project "$S3L_PROJECT" -c Release --no-launch-profile --urls "http://0.0.0.0:$S3L_PORT"
        """;

    private const string Test = $$"""
        {{Build}}
        set +e
        timeout -k 10 "$S3L_TIMEOUT" dotnet test "$S3L_PROJECT" --nologo --logger "console;verbosity=normal" $${S3L_FILTER:+--filter "$S3L_FILTER"}
        echo "{{TestsEnded}} $?"
        exec sleep "$S3L_HOLD"
        """;

    /// <summary>The environment's job: a service job of one group, which Nomad restarts an app in twice before it gives up.</summary>
    /// <remarks>
    /// Each raise carries a mark of its own in the preparing task, which makes it a change Nomad replaces the allocation
    /// for. Without it a raise of the same copy registered the same job, Nomad kept the allocation running, and the app
    /// went on serving the code it was built from before the caller changed it: the raise answered ready in no time.
    /// </remarks>
    public static JsonObject Of(EnvironmentPlan plan, string raise, E2EOptions options)
    {
        var manifest = plan.Manifest;
        var services = manifest.Services.Keys.ToList();
        JsonArray tasks = [.. manifest.Services.Select(service => ServiceTask(plan.Name, service.Key, service.Value, manifest.App, services, options))];

        tasks.Add(BuildTask(Parts.App, plan.Name, Run, AppEnv(plan, options), options));
        tasks.Add(PrepareTask(plan.Name, plan.Copy, raise, options));
        tasks.Add(KeepTask(plan.Name, options));

        var job = new JsonObject
        {
            ["ID"] = plan.Name.Value,
            ["Name"] = plan.Name.Value,
            ["Type"] = "service",
            ["Datacenters"] = new JsonArray(options.Datacenter),
            ["Meta"] = new JsonObject { ["s3l.e2e.copy"] = plan.Copy, ["s3l.e2e.raise"] = raise },
            ["TaskGroups"] = new JsonArray(Group("environment", tasks, options, attempts: 2))
        };

        return job;
    }

    /// <summary>The test run's job: a batch job that runs once, never restarted, in the environment's network.</summary>
    /// <remarks>
    /// Each run carries a mark of its own: Nomad keeps a batch job that registers unchanged, and the tests would not run
    /// again. The tests end on their own time limit, the one the server waits for: a run whose server restarted or whose
    /// stop failed would otherwise hold the node with nobody waiting for it.
    /// </remarks>
    public static JsonObject TestsOf(TestPlan plan, string run, E2EOptions options)
    {
        var manifest = plan.Manifest;
        var services = manifest.Services.Keys.ToList();
        var env = Common(plan.Name, plan.Tests.Project, manifest, options);

        foreach (var setting in Placeholders.Fill(plan.Tests.Env, plan.Name, manifest.App, services))
            env[setting.Key] = setting.Value;

        if (plan.Filter is { } filter)
            env["S3L_FILTER"] = filter;

        env["S3L_TIMEOUT"] = $"{options.TestSeconds}s";
        env["S3L_HOLD"] = $"{HoldSeconds}";

        var job = new JsonObject
        {
            ["ID"] = plan.Name.Tests,
            ["Name"] = plan.Name.Tests,
            ["Type"] = "batch",
            ["Datacenters"] = new JsonArray(options.Datacenter),
            ["Meta"] = new JsonObject { ["s3l.e2e.copy"] = plan.Copy, ["s3l.e2e.run"] = run },
            ["TaskGroups"] = new JsonArray(Group(Parts.Tests, [BuildTask(Parts.Tests, plan.Name, Test, env, options), PrepareTask(plan.Name, plan.Copy, run, options), KeepTask(plan.Name, options)], options, attempts: 0))
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

    private static JsonObject BuildTask(string task, TaskEnvironment name, string script, Dictionary<string, string> env, E2EOptions options) => new()
    {
        ["Name"] = task,
        ["Driver"] = "docker",
        ["Config"] = new JsonObject
        {
            ["image"] = options.SdkImage,
            ["network_mode"] = options.Network,
            ["network_aliases"] = new JsonArray(name.HostOf(task)),
            ["command"] = "sh",
            ["args"] = new JsonArray("-c", script)
        },
        ["Env"] = Env(env),
        ["Resources"] = new JsonObject { ["CPU"] = options.AppCpu, ["MemoryMB"] = options.AppMemoryMegabytes }
    };

    /// <summary>The server's own script before every other task: the task's copy and packages into the allocation.</summary>
    private static JsonObject PrepareTask(TaskEnvironment name, string copy, string mark, E2EOptions options)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal) { ["S3L_COPY"] = copy, ["S3L_TASK"] = name.Value, ["S3L_MARK"] = mark };
        var task = ScriptTask(Parts.Prepare, "prestart", Prepare, env, options);

        task["VolumeMounts"] = new JsonArray(new JsonObject
        {
            ["Volume"] = "workspaces",
            ["Destination"] = options.WorkspacesRoot,
            ["ReadOnly"] = true,
            ["PropagationMode"] = "host-to-task"
        });

        return task;
    }

    /// <summary>The server's own script after every other task: the task's packages back to its folder of the package volume.</summary>
    private static JsonObject KeepTask(TaskEnvironment name, E2EOptions options) =>
        ScriptTask(Parts.Keep, "poststop", Keep, new Dictionary<string, string>(StringComparer.Ordinal) { ["S3L_TASK"] = name.Value }, options);

    private static JsonObject ScriptTask(string task, string hook, string script, Dictionary<string, string> env, E2EOptions options) => new()
    {
        ["Name"] = task,
        ["Driver"] = "docker",
        ["Lifecycle"] = new JsonObject { ["Hook"] = hook, ["Sidecar"] = false },
        ["Config"] = new JsonObject
        {
            ["image"] = options.SdkImage,
            ["network_mode"] = "none",
            ["command"] = "sh",
            ["args"] = new JsonArray("-c", script),
            ["mounts"] = new JsonArray(new JsonObject { ["type"] = "volume", ["source"] = options.PackagesVolume, ["target"] = PackagesFolder, ["readonly"] = false })
        },
        ["Env"] = Env(env),
        ["Resources"] = new JsonObject { ["CPU"] = options.ServiceCpu, ["MemoryMB"] = options.ServiceMemoryMegabytes }
    };

    private static Dictionary<string, string> AppEnv(EnvironmentPlan plan, E2EOptions options)
    {
        var manifest = plan.Manifest;
        var env = Common(plan.Name, manifest.App.Project, manifest, options);

        env["S3L_PORT"] = manifest.App.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        env["ASPNETCORE_ENVIRONMENT"] = "Development";

        foreach (var setting in Placeholders.Fill(manifest.App.Env, plan.Name, manifest.App, [.. manifest.Services.Keys]))
            env[setting.Key] = setting.Value;

        return env;
    }

    /// <summary>What every container that builds is given: the project, the task's packages, and the proxy with the environment's own hosts kept off it.</summary>
    private static Dictionary<string, string> Common(TaskEnvironment name, string project, EnvironmentManifest manifest, E2EOptions options)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["S3L_PROJECT"] = project,
            ["NUGET_PACKAGES"] = $"{AllocationFolder}/nuget",
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
        new([.. settings.Select(setting => KeyValuePair.Create(setting.Key, (JsonNode?)setting.Value))]);

    /// <summary>A string of a task's config as Nomad is to pass it on, with every <c>${</c> kept from its interpolation.</summary>
    private static string Literal(string value) => value.Replace("${", "$${", StringComparison.Ordinal);
}
