namespace Snail.MCP.E2E.Application;

/// <summary>What a caller reads when something did not happen, with what to do about it.</summary>
public static class Messages
{
    /// <summary>The session names no working copy.</summary>
    public const string NoCopy = "This session names no working copy, so there is no project to raise.";

    /// <summary>Where the copy comes from.</summary>
    public const string NoCopyHint = "The platform names the step's copy in the server's address as ?root=; connect the server to a role through the platform.";

    /// <summary>The manifest names no tests.</summary>
    public const string NoTests = "The manifest names no tests: add a \"tests\" part with the test project.";

    /// <summary>No environment runs for the task.</summary>
    public const string NotRaised = "No environment runs for this task.";

    /// <summary>What raises one.</summary>
    public const string NotRaisedHint = "Call e2e_up first.";

    /// <summary>The app did not answer a request.</summary>
    public const string Unreachable = "The app did not answer.";

    /// <summary>A part of the environment stopped and will not start again.</summary>
    public const string StartFailed = "A part of the environment stopped and will not start again: the app's build or start, a service, or the copy of the project failed. The parts say which, and the output ends with the reason.";

    /// <summary>The app did not answer its health check in time.</summary>
    public const string StartSlow = "The app did not answer its health check in time. It may still be restoring or building: call e2e_status, or read e2e_logs.";

    /// <summary>Some tests failed.</summary>
    public const string TestsFailed = "Some tests failed: the failures name them; fix the app or the tests and run again.";

    /// <summary>The test project did not build.</summary>
    public const string BuildFailed = "The test project did not build: the output ends with the compiler's errors.";

    /// <summary>No test ran.</summary>
    public const string NoTestsRan = "No test ran: the project holds none, or the filter matched none.";

    /// <summary>A test run that printed nothing at all.</summary>
    public const string TestsSilent = "The test run left no output to read: it did not start, or its node dropped the output before it was read. e2e_status shows the environment; read e2e_logs for tests.";

    /// <summary>The tests ran out of time.</summary>
    public const string TestsTimedOut = "The tests did not end in time and were stopped.";

    /// <summary>A copy outside the workspaces root.</summary>
    public static string CopyOutside(string root, string workspaces) =>
        $"The working copy \"{root}\" is not a task's copy under {workspaces}.";

    /// <summary>The copy has no manifest.</summary>
    public static string NoManifest(string path) =>
        $"The project has no environment manifest at {path}.";

    /// <summary>A manifest to start from.</summary>
    public const string NoManifestHint = $"Write one: {ExampleManifest}";

    /// <summary>A manifest the rules accept: an API with Mongo and its end-to-end tests.</summary>
    public const string ExampleManifest =
        """{"app":{"project":"src/Api/Api.csproj","port":8080,"health":"/health","env":{"Mongo__Url":"mongodb://{mongo}:27017"}},"services":{"mongo":{"image":"mongo:8.0","port":27017}},"tests":{"project":"tests/Api.E2E/Api.E2E.csproj","env":{"APP_URL":"{app}"}}}""";

    /// <summary>The manifest is not valid JSON.</summary>
    public static string BadManifest(string path, string reason) =>
        $"The environment manifest at {path} cannot be read: {reason}";

    /// <summary>The manifest breaks its rules.</summary>
    public static string ManifestProblems(string path, IReadOnlyList<string> problems) =>
        $"The environment manifest at {path} cannot be raised: {string.Join("; ", problems)}.";

    /// <summary>A part the environment does not hold.</summary>
    public static string UnknownPart(string part, IEnumerable<string> parts) =>
        $"The environment has no part \"{part}\"; it has {string.Join(", ", parts)}.";

    /// <summary>A method the tool does not send.</summary>
    public static string BadMethod(string method) =>
        $"\"{method}\" is not a method this tool sends: GET, POST, PUT, PATCH, DELETE or HEAD.";

    /// <summary>A path that is not the app's.</summary>
    public static string BadPath(string path) =>
        $"\"{path}\" is not a path of the app: it starts with / and names no other host.";

    /// <summary>A test filter Nomad would change on its way to the run.</summary>
    public static string BadFilter(string filter) =>
        $"The filter \"{filter}\" holds ${{…}}, which Nomad fills with the node's own values before the run sees it: write it without ${{.";

    /// <summary>Nomad did not answer.</summary>
    public static string SchedulerDown(string address, string reason) =>
        $"Nomad at {address} did not answer: {reason}";

    /// <summary>Where Nomad comes from.</summary>
    public const string SchedulerDownHint = "TaskEnvironments run through Nomad: start the stand with SWARM_NOMAD=1, or point SNAIL_MCP_E2E_NOMAD_ADDRESS at it.";

    /// <summary>A raise or a test run waits for the one before it in the environment's line.</summary>
    public const string WaitingInLine = "waiting for the environment's previous raise or test run to end";

    /// <summary>A raise at work.</summary>
    public const string Raising = "raising the environment and waiting for the app's health check";

    /// <summary>A test run at work.</summary>
    public const string Testing = "running the end-to-end tests";

    /// <summary>A raise or a test run that e2e_down stopped.</summary>
    public const string StoppedByDown = "The environment was stopped with e2e_down before this ended.";

    /// <summary>A job the server cancelled, as it does when it stops.</summary>
    public const string JobCancelled = "The work was cancelled: the server stopped before it ended.";

    /// <summary>Where the result of a job still running will be.</summary>
    public static string JobRunning(string job) =>
        $"Still running. Its result will be in the resource {job}: read it when you need the result. Calling the same tool again while it runs waits on this job rather than starting another.";
}
