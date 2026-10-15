using System.Text.RegularExpressions;

namespace Snail.MCP.E2E.Domain;

/// <summary>What <c>dotnet test</c> printed, read back as an outcome with counts.</summary>
/// <remarks>
/// The run happens in a container of its own and only its output comes back, so the report is read from the summary a
/// VSTest run ends with, in either of its shapes: the one line of minimal verbosity —
/// <c>Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3</c> — or the block of normal verbosity, which the server asks
/// for so the failed tests are named: <c>Total tests: 3</c> with <c>Passed: 3</c>, <c>Failed:</c> and <c>Skipped:</c>
/// lines under it, each present only when it is not zero. Each failed test prints a <c>Failed Name [time]</c> line. A
/// build that fails prints <c>error CS…</c> or <c>Build FAILED</c> and no summary.
/// </remarks>
public static partial class TestSummary
{
    /// <summary>Most failed tests named in one report.</summary>
    public const int MaxFailures = 20;

    /// <summary>The outcome and counts of a run that ended on its own.</summary>
    public static (TestOutcome Outcome, TestCounts? Counts) Of(string output)
    {
        var summaries = Summary().Matches(output);
        if (summaries.Count == 0)
            summaries = Block().Matches(output);

        if (summaries.Count == 0)
            return (Build().IsMatch(output) ? TestOutcome.BuildFailed : TestOutcome.NoTests, null);

        int failed = Count(summaries, "failed");
        int passed = Count(summaries, "passed");
        int skipped = Count(summaries, "skipped");
        int total = Count(summaries, "total");
        IReadOnlyList<string> failures = [.. FailedTest().Matches(output).Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).Take(MaxFailures)];
        var counts = new TestCounts(total, passed, failed, skipped, failures);

        TestOutcome outcome = failed > 0 ? TestOutcome.Failed : total == 0 ? TestOutcome.NoTests : TestOutcome.Passed;

        return (outcome, counts);
    }

    /// <summary>One count summed over every summary; a count a block leaves out is zero.</summary>
    private static int Count(MatchCollection summaries, string group) =>
        summaries.Sum(match => match.Groups[group].Success ? int.Parse(match.Groups[group].Value, System.Globalization.CultureInfo.InvariantCulture) : 0);

    [GeneratedRegex(@"Total tests:\s*(?<total>\d+)(?:\s*\n\s*Passed:\s*(?<passed>\d+))?(?:\s*\n\s*Failed:\s*(?<failed>\d+))?(?:\s*\n\s*Skipped:\s*(?<skipped>\d+))?")]
    private static partial Regex Block();

    [GeneratedRegex(@"(?:Passed|Failed)!\s+-\s+Failed:\s+(?<failed>\d+),\s+Passed:\s+(?<passed>\d+),\s+Skipped:\s+(?<skipped>\d+),\s+Total:\s+(?<total>\d+)")]
    private static partial Regex Summary();

    [GeneratedRegex(@"^[ \t]*Failed (?<name>.+?) \[[^\]\n]*\][ \t]*\r?$", RegexOptions.Multiline)]
    private static partial Regex FailedTest();

    [GeneratedRegex(@"error (CS|MSB|NU|FS)\d+|Build FAILED", RegexOptions.IgnoreCase)]
    private static partial Regex Build();
}
