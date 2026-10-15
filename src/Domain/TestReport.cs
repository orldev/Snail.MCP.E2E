namespace Snail.MCP.E2E.Domain;

/// <summary>How a test run ended.</summary>
public enum TestOutcome
{
    /// <summary>Every test ran and none failed.</summary>
    Passed,

    /// <summary>At least one test failed.</summary>
    Failed,

    /// <summary>The test project did not build, so no test ran.</summary>
    BuildFailed,

    /// <summary>The project built but holds no test that ran.</summary>
    NoTests,

    /// <summary>The run did not end within its time and was stopped.</summary>
    TimedOut
}

/// <summary>A test run as the caller reads it: the outcome, the counts, the failed tests by name, and the end of the output.</summary>
public sealed record TestReport(TestOutcome Outcome, TestCounts? Counts, string Output, bool IsOutputCut, double Seconds);

/// <summary>The counts a test run printed, and the names of the tests that failed.</summary>
public sealed record TestCounts(int Total, int Passed, int Failed, int Skipped, IReadOnlyList<string> Failures);
