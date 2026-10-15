using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Snail.MCP.E2E.Adapters;
using Snail.MCP.E2E.Configuration;
using Snail.MCP.E2E.Domain;

namespace Snail.MCP.E2E.Benchmarks;

/// <summary>The work a raise and a test run do on the server itself, measured; the shared perf workflow compares its allocations with the baseline.</summary>
[MemoryDiagnoser]
public class EnvironmentBenchmarks
{
    private static readonly EnvironmentManifest Shop = new(
        new AppPart("src/Shop.Api/Shop.Api.csproj", 8080, "/health", new Dictionary<string, string> { ["Mongo__Url"] = "mongodb://{mongo}:27017", ["Rabbit__Host"] = "{rabbit}" }),
        new Dictionary<string, ServicePart>
        {
            ["mongo"] = new("mongo:8.0", 27017, new Dictionary<string, string>(), []),
            ["rabbit"] = new("rabbitmq:4-alpine", 5672, new Dictionary<string, string>(), [])
        },
        new TestPart("tests/Shop.E2E/Shop.E2E.csproj", new Dictionary<string, string> { ["APP_URL"] = "{app}" }));

    private static readonly EnvironmentPlan Plan = new(TaskEnvironment.Of("0c2b9e1f")!.Value, "/workspaces/0c2b9e1f/step-2", Shop);

    private static readonly E2EOptions Options = new() { RestoreProxy = "http://mcp-e2e:3128" };

    private static readonly string Output = string.Join('\n', Enumerable.Range(0, 200).Select(index => $"  Passed Shop.E2E.OrdersTests.Case{index} [1 ms]"))
        + "\nFailed!  - Failed:     1, Passed:   199, Skipped:     0, Total:   200";

    /// <summary>The job of one environment, from its manifest.</summary>
    [Benchmark]
    public JsonObject Job() => EnvironmentJob.Of(Plan, Options);

    /// <summary>The rules a manifest is judged by.</summary>
    [Benchmark]
    public IReadOnlyList<string> Rules() => ManifestRules.Problems(Shop);

    /// <summary>The report of a run of two hundred tests.</summary>
    [Benchmark]
    public (TestOutcome, TestCounts?) Summary() => TestSummary.Of(Output);
}
