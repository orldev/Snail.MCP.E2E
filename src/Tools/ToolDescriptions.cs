namespace Snail.MCP.E2E.Tools;

/// <summary>What the model reads before calling: the tool contract, guarded by the tests in <c>ToolCatalogTests</c>.</summary>
/// <remarks>
/// Every number here is a default of <c>E2EOptions</c>, and a guard test fails when the two drift apart. A deployment
/// that changes a limit makes this text wrong for that deployment, so limits are changed by changing the defaults.
/// </remarks>
public static class ToolDescriptions
{
    /// <summary>The states an environment answers with, as the answer spells them; a guard test compares them with <c>EnvironmentState</c>.</summary>
    public const string States = "ready, starting, failed or stopped";

    /// <summary>The outcomes a test run answers with; a guard test compares them with <c>TestOutcome</c>.</summary>
    public const string Outcomes = "passed, failed, buildFailed, noTests or timedOut";

    /// <summary>The contract of e2e_up.</summary>
    public const string Up =
        $"""
        Raise the task's environment from the working copy and wait until the app answers its health check.
        The project describes it in .snail/environment.json: the app's project, port and health path, the services it
        needs (a database, a broker) by image, and its end-to-end tests. The app is built and run with .NET in a network
        of its own, beside its services; only NuGet is reachable from it. The first raise restores packages and builds,
        which takes minutes; the app is given up to 300 seconds to answer. Raise again after changing the code: the
        environment is replaced by one running the current files. The state is one of {States}; when it is failed, the
        answer carries the end of the app's output with the reason. {Job}
        """;

    /// <summary>The contract of e2e_status.</summary>
    public const string Status =
        $"""
        Say where the task's environment stands now without waiting: {States}, each part's task state and restarts.
        """;

    /// <summary>The contract of e2e_down.</summary>
    public const string Down =
        """
        Stop the task's environment and its test runs, cancelling a raise or test run still at work. An environment
        unused for 60 minutes is stopped on its own.
        """;

    /// <summary>The contract of e2e_http.</summary>
    public const string Http =
        """
        Send one HTTP request to the running app, as a client inside its network would, and get back the status, the
        headers and the body. The body is cut at 16384 bytes. Raise the environment first with e2e_up.
        """;

    /// <summary>The method parameter of e2e_http.</summary>
    public const string HttpMethod = "GET, POST, PUT, PATCH, DELETE or HEAD.";

    /// <summary>The path parameter of e2e_http.</summary>
    public const string HttpPath = "The path and query of the app, starting with /, such as /orders?status=new.";

    /// <summary>The body parameter of e2e_http.</summary>
    public const string HttpBody = "The request body as text, such as a JSON document; empty sends none.";

    /// <summary>The content type parameter of e2e_http.</summary>
    public const string HttpContentType = "The media type of the body; application/json when not given.";

    /// <summary>The headers parameter of e2e_http.</summary>
    public const string HttpHeaders = "Extra request headers, each as \"Name: value\", such as \"Authorization: Bearer abc\".";

    /// <summary>The contract of e2e_logs.</summary>
    public const string Logs =
        """
        Read the end of what one part of the environment printed: the app, one of its services by name, or tests for the
        last test run. Both standard output and standard error come back, each up to 8192 bytes unless asked for more,
        at most 65536.
        """;

    /// <summary>The part parameter of e2e_logs.</summary>
    public const string LogsPart = "app, a service name from the manifest, or tests.";

    /// <summary>The bytes parameter of e2e_logs.</summary>
    public const string LogsBytes = "How many bytes of the end of each stream to read; 8192 when not given, at most 65536.";

    /// <summary>The contract of e2e_test.</summary>
    public const string Test =
        $"""
        Run the project's end-to-end tests against the running app: dotnet test of the test project the manifest names,
        in the environment's network, with the settings that point it at the app. The run gets up to 900 seconds,
        restore and build included. The outcome is one of {Outcomes}; the answer counts the tests, names up to 20 that
        failed, and carries the end of the output, up to 16384 bytes. {Job}
        """;

    /// <summary>How a raise and a test run answer: within the wait, or with the job they go on as.</summary>
    public const string Job =
        "It runs as a job: the answer comes within the wait with the result, or with the job's resource, whose text holds the result once it ends, or why it failed. One environment does one raise or test run at a time; the next waits its turn, and e2e_down cancels them.";

    /// <summary>The wait parameter of e2e_up and e2e_test.</summary>
    public const string Wait = "Seconds to wait for the result before answering with the job in flight, 0 to 900; 0 answers at once.";

    /// <summary>The filter parameter of e2e_test.</summary>
    public const string TestFilter = "A dotnet test filter, such as FullyQualifiedName~Orders; empty runs every test.";

    /// <summary>The contract of e2e_diagnose.</summary>
    public const string Diagnose =
        """
        Say whether this server can raise environments at all: Nomad's version, the network environments run in, and
        whether the restore proxy is on. Changes nothing.
        """;
}
