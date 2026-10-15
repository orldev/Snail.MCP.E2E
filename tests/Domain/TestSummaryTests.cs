namespace Snail.MCP.E2E.Tests.Domain;

public sealed class TestSummaryTests
{
    [Fact]
    public void Of_FailedRun_CountsAndNamesTheFailures()
    {
        const string output = """
              Failed Shop.E2E.OrdersTests.Create_ValidOrder_IsStored [212 ms]
              Error Message:
               Assert.Equal() Failure
              Passed Shop.E2E.HealthTests.Health_Running_IsOk [12 ms]

            Failed!  - Failed:     1, Passed:     2, Skipped:     0, Total:     3, Duration: 1 s - Shop.E2E.dll (net10.0)
            """;

        var (outcome, counts) = TestSummary.Of(output);

        Assert.Equal(TestOutcome.Failed, outcome);
        Assert.Equal((3, 2, 1, 0), (counts!.Total, counts.Passed, counts.Failed, counts.Skipped));
        Assert.Equal(["Shop.E2E.OrdersTests.Create_ValidOrder_IsStored"], counts.Failures);
    }

    [Fact]
    public void Of_PassedRun_IsPassed()
    {
        var (outcome, counts) = TestSummary.Of("Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 1 s - Shop.E2E.dll (net10.0)");

        Assert.Equal(TestOutcome.Passed, outcome);
        Assert.Equal(3, counts!.Passed);
    }

    /// <summary>The block normal verbosity ends with, read off a real run of the sample shop.</summary>
    [Fact]
    public void Of_NormalVerbosityBlock_IsReadLikeTheOneLineSummary()
    {
        const string output = """
              Passed Shop.E2E.OrdersTests.Health_AppWithItsServices_IsOk [1 ms]
              Failed Shop.E2E.OrdersTests.CreateOrder_QuantityNotPositive_IsRejected(quantity: 0) [3 ms]

            Test Run Failed.
            Total tests: 6
                 Passed: 4
                 Failed: 2
             Total time: 0,9047 Seconds
            """;

        var (outcome, counts) = TestSummary.Of(output);

        Assert.Equal(TestOutcome.Failed, outcome);
        Assert.Equal((6, 4, 2, 0), (counts!.Total, counts.Passed, counts.Failed, counts.Skipped));
        Assert.Equal(["Shop.E2E.OrdersTests.CreateOrder_QuantityNotPositive_IsRejected(quantity: 0)"], counts.Failures);
    }

    [Fact]
    public void Of_NormalVerbosityAllPassed_IsPassed()
    {
        var (outcome, counts) = TestSummary.Of("Test Run Successful.\nTotal tests: 6\n     Passed: 6\n Total time: 0.9 Seconds");

        Assert.Equal((TestOutcome.Passed, 6), (outcome, counts!.Passed));
    }

    [Fact]
    public void Of_CompilerErrorsAndNoSummary_IsABuildFailure()
    {
        var (outcome, counts) = TestSummary.Of("/tmp/src/tests/OrdersTests.cs(12,5): error CS0103: The name 'x' does not exist\nBuild FAILED.");

        Assert.Equal(TestOutcome.BuildFailed, outcome);
        Assert.Null(counts);
    }

    [Fact]
    public void Of_NoSummaryAndNoErrors_IsNoTests()
    {
        Assert.Equal(TestOutcome.NoTests, TestSummary.Of("No test is available in /tmp/src/tests/bin/Shop.E2E.dll.").Outcome);
    }
}
