namespace Snail.MCP.E2E.Tools;

/// <summary>What the model is told about this server when it connects.</summary>
public static class ServerGuidance
{
    /// <summary>The instructions a client shows the model before any tool.</summary>
    public const string Instructions =
        """
        This server runs the task's .NET app with the services it needs — a database, a broker — in an environment of its
        own, so you can check that the app works end to end, not only that it compiles.

        Start with e2e_up: it builds the working copy and waits until the app answers. Then call the API with e2e_http,
        read what the app or a service printed with e2e_logs, and run the project's end-to-end tests with e2e_test.
        After you change the code, call e2e_up again: the environment is replaced by one running the current files.
        e2e_status says where it stands without waiting; e2e_down stops it when you are done.

        The project describes its environment in .snail/environment.json. Without one, e2e_up says how to write it.
        Every answer is JSON with an ok flag, and a failed call is also flagged isError. When ok is false, read error and
        hint and fix the call instead of retrying it verbatim; type names the kind of failure. Tests that failed and an app
        that answered 500 are successes, told in the data. A raise or a test run whose job ended failed answers ok false
        too: the job had started, and what it did still stands.
        """;
}
