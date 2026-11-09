using System.Text.Json.Nodes;
using Snail.MCP.E2E.Application;

namespace Snail.MCP.E2E.Tools;

/// <summary>What each answer of this server carries as its data, inside the envelope <see cref="ToolAnswer"/> writes.</summary>
/// <remarks>An app that answered 500 or tests that failed are successes of the tool: the failure is the app's, told in the data. Only a call that could not happen is a failure.</remarks>
public static class AnswerFields
{
    /// <summary>How an answer spells an enum value: camel case, the way the descriptions name it (<c>buildFailed</c>, <c>noTests</c>).</summary>
    public const WordCase Casing = WordCase.Camel;

    /// <summary>Where the environment stands, with a note when it is not ready.</summary>
    public static JsonObject Of(EnvironmentStatus status) => new()
    {
        ["environment"] = status.Name,
        ["state"] = ToolAnswer.Word(status.State, Casing),
        ["app"] = status.AppUrl,
        ["parts"] = new JsonArray([.. status.Parts.Select(part => new JsonObject { ["name"] = part.Name, ["state"] = part.State, ["restarts"] = part.Restarts, ["failed"] = part.HasFailed })]),
        ["seconds"] = status.Seconds,
        ["output"] = status.Output,
        ["note"] = status.State switch
        {
            EnvironmentState.Failed => Messages.StartFailed,
            EnvironmentState.Starting => Messages.StartSlow,
            EnvironmentState.Stopped => Messages.NotRaisedHint,
            _ => null
        }
    };

    /// <summary>How a test run went, with a note when it did not pass.</summary>
    public static JsonObject Of(TestReport report) => new()
    {
        ["outcome"] = ToolAnswer.Word(report.Outcome, Casing),
        ["total"] = report.Counts?.Total,
        ["passed"] = report.Counts?.Passed,
        ["failed"] = report.Counts?.Failed,
        ["skipped"] = report.Counts?.Skipped,
        ["failures"] = new JsonArray([.. (report.Counts?.Failures ?? []).Select(name => JsonValue.Create(name))]),
        ["seconds"] = Math.Round(report.Seconds, 1),
        ["output"] = report.Output,
        ["outputCut"] = report.IsOutputCut,
        ["note"] = report.Outcome switch
        {
            TestOutcome.Failed => Messages.TestsFailed,
            TestOutcome.BuildFailed => Messages.BuildFailed,
            TestOutcome.NoTests => Messages.NoTestsRan,
            TestOutcome.TimedOut => Messages.TestsTimedOut,
            _ => null
        }
    };

    /// <summary>What the app answered to one request.</summary>
    public static JsonObject Of(HttpExchange exchange) => new()
    {
        ["status"] = exchange.Status,
        ["headers"] = new JsonObject([.. exchange.Headers.Select(header => KeyValuePair.Create(header.Key, (JsonNode?)header.Value))]),
        ["body"] = exchange.Body,
        ["bodyCut"] = exchange.IsBodyCut,
        ["milliseconds"] = exchange.Milliseconds
    };

    /// <summary>The end of one part's output.</summary>
    public static JsonObject Of(LogTail tail) => new() { ["part"] = tail.Part, ["stdout"] = tail.Stdout, ["stderr"] = tail.Stderr };

    /// <summary>Whether a stop found an environment running.</summary>
    public static JsonObject Stopped(bool hadRun) => new() { ["stopped"] = hadRun };
}
