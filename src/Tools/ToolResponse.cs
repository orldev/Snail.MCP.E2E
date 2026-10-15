using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Snail.MCP.E2E.Application;

namespace Snail.MCP.E2E.Tools;

/// <summary>Answers as tool results: JSON with an ok flag, the data on success, error and hint otherwise.</summary>
/// <remarks>An app that answered 500 or tests that failed are successes of the tool: the failure is the app's, told in the data. Only a call that could not happen is flagged as an error.</remarks>
public static class ToolResponse
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>The answer as a tool result.</summary>
    public static CallToolResult From(Answer<EnvironmentStatus> answer) =>
        answer.Value is { } status ? Success(Data(status)) : Failure(answer);

    /// <summary>The answer as a tool result.</summary>
    public static CallToolResult From(Answer<bool> answer) =>
        answer.Error is null ? Success(new JsonObject { ["stopped"] = answer.Value }) : Failure(answer);

    /// <summary>The answer as a tool result.</summary>
    public static CallToolResult From(Answer<HttpExchange> answer) =>
        answer.Value is { } exchange
            ? Success(new JsonObject
            {
                ["status"] = exchange.Status,
                ["headers"] = new JsonObject([.. exchange.Headers.Select(header => KeyValuePair.Create(header.Key, (JsonNode?)header.Value))]),
                ["body"] = exchange.Body,
                ["bodyCut"] = exchange.IsBodyCut,
                ["milliseconds"] = exchange.Milliseconds
            })
            : Failure(answer);

    /// <summary>The answer as a tool result.</summary>
    public static CallToolResult From(Answer<LogTail> answer) =>
        answer.Value is { } tail
            ? Success(new JsonObject { ["part"] = tail.Part, ["stdout"] = tail.Stdout, ["stderr"] = tail.Stderr })
            : Failure(answer);

    /// <summary>The answer as a tool result.</summary>
    public static CallToolResult From(Answer<TestReport> answer) =>
        answer.Value is { } report ? Success(Data(report)) : Failure(answer);

    /// <summary>A result carrying the data.</summary>
    public static CallToolResult Success(JsonObject data) => new()
    {
        Content = [new TextContentBlock { Text = new JsonObject { ["ok"] = true, ["data"] = data.DeepClone() }.ToJsonString(Options) }],
        StructuredContent = JsonSerializer.SerializeToElement(data, Options)
    };

    /// <summary>A result saying why nothing happened.</summary>
    public static CallToolResult Failure(string error, string? hint) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = new JsonObject { ["ok"] = false, ["error"] = error, ["hint"] = hint }.ToJsonString(Options) }],
        StructuredContent = JsonSerializer.SerializeToElement(new JsonObject { ["error"] = error, ["hint"] = hint }, Options)
    };

    private static CallToolResult Failure<T>(Answer<T> answer) => Failure(answer.Error ?? "The call did not happen.", answer.Hint);

    private static JsonObject Data(EnvironmentStatus status) => new()
    {
        ["environment"] = status.Name,
        ["state"] = Word(status.State),
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

    private static JsonObject Data(TestReport report) => new()
    {
        ["outcome"] = Word(report.Outcome),
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

    /// <summary>An enum value as the answer spells it: camel case, the way the descriptions name it.</summary>
    public static string Word<TEnum>(TEnum value) where TEnum : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
