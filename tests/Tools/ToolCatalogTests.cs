using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Snail.MCP.E2E.Configuration;
using Snail.MCP.E2E.Tools;

namespace Snail.MCP.E2E.Tests.Tools;

/// <summary>The descriptions a model reads, held to the code they describe.</summary>
public sealed partial class ToolCatalogTests
{
    private static readonly E2EOptions Defaults = new();

    private static IEnumerable<string> Tools()
    {
        var methods = typeof(EnvironmentTools).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods());

        return methods.Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name).OfType<string>();
    }

    [Fact]
    public void Tools_EveryOne_IsNamedForThisServer()
    {
        Assert.Equal(["e2e_diagnose", "e2e_down", "e2e_http", "e2e_logs", "e2e_status", "e2e_test", "e2e_up"], Tools().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void States_EveryEnvironmentState_IsNamed()
    {
        foreach (var state in Enum.GetValues<EnvironmentState>())
            Assert.Contains(ToolAnswer.Word(state, AnswerContent.Casing), Words(ToolDescriptions.States));
    }

    [Fact]
    public void Outcomes_EveryTestOutcome_IsNamed()
    {
        foreach (var outcome in Enum.GetValues<TestOutcome>())
            Assert.Contains(ToolAnswer.Word(outcome, AnswerContent.Casing), Words(ToolDescriptions.Outcomes));
    }

    /// <summary>Every number a description names is the default it describes: a limit changed in code fails here until the text says it.</summary>
    [Theory]
    [InlineData(ToolDescriptions.Up, nameof(E2EOptions.StartSeconds))]
    [InlineData(ToolDescriptions.Down, nameof(E2EOptions.IdleMinutes))]
    [InlineData(ToolDescriptions.Http, nameof(E2EOptions.ResponseBytes))]
    [InlineData(ToolDescriptions.Logs, nameof(E2EOptions.DefaultLogBytes))]
    [InlineData(ToolDescriptions.Logs, nameof(E2EOptions.MaxLogBytes))]
    [InlineData(ToolDescriptions.LogsBytes, nameof(E2EOptions.DefaultLogBytes))]
    [InlineData(ToolDescriptions.LogsBytes, nameof(E2EOptions.MaxLogBytes))]
    [InlineData(ToolDescriptions.Test, nameof(E2EOptions.TestSeconds))]
    [InlineData(ToolDescriptions.Test, nameof(E2EOptions.TestOutputBytes))]
    public void Description_NumberItNames_IsTheDefault(string description, string option)
    {
        object? value = typeof(E2EOptions).GetProperty(option)!.GetValue(Defaults);

        Assert.Contains($"{value}", Number().Matches(description).Select(match => match.Value));
    }

    [Fact]
    public void Wait_LongestItNames_IsTheLongestAJobIsWaitedFor()
    {
        Assert.Contains($"0 to {EnvironmentWork.MaxWaitSeconds}", ToolDescriptions.Wait, StringComparison.Ordinal);
    }

    [Fact]
    public void Test_FailuresItNames_AreAsManyAsTheReportKeeps()
    {
        Assert.Contains($"up to {TestSummary.MaxFailures} that", ToolDescriptions.Test.ReplaceLineEndings(" "), StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Words(string text) => [.. text.Split([' ', ',', '.'], StringSplitOptions.RemoveEmptyEntries)];

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();
}
