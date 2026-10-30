using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Snail.MCP.E2E.Application;
using Snail.Sdk.Mcp.Hosting.Jobs;

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
    public static CallToolResult From(Answer<EnvironmentStatus> answer) => From(Envelope(answer));

    /// <summary>The answer as the JSON a tool result carries: the ok flag, and the data or the error and hint.</summary>
    public static JsonObject Envelope(Answer<EnvironmentStatus> answer) =>
        answer.Value is { } status ? Ok(Data(status)) : NotOk(answer);

    /// <summary>The answer as the JSON a tool result carries: the ok flag, and the data or the error and hint.</summary>
    public static JsonObject Envelope(Answer<TestReport> answer) =>
        answer.Value is { } report ? Ok(Data(report)) : NotOk(answer);

    /// <summary>A failure as the JSON a tool result carries; a success of this type carries nothing worth a job.</summary>
    public static JsonObject Envelope(Answer<bool> answer) => NotOk(answer);

    /// <summary>The JSON a tool result carries, as the result itself.</summary>
    public static CallToolResult From(JsonObject envelope) =>
        envelope["ok"]?.GetValue<bool>() == true
            ? Success(envelope["data"]?.AsObject() ?? [])
            : Failure(envelope["error"]?.GetValue<string>() ?? "The call did not happen.", envelope["hint"]?.GetValue<string>());

    /// <summary>
    /// A job as a tool result: the text is what the model reads — the tool's own answer once the job ended, or the job in
    /// flight with the resource it is read at — and the structured content is the job's report, which a client of
    /// <c>Snail.Sdk.Mcp.JobClient</c> awaits.
    /// </summary>
    public static CallToolResult From(JobReport report)
    {
        var job = JsonNode.Parse(report.ToJson())!.AsObject();
        var said = Said(report, job);

        return new CallToolResult
        {
            IsError = said.IsError,
            Content = said.Content,
            StructuredContent = JsonSerializer.SerializeToElement(job, Options)
        };
    }

    private static CallToolResult Said(JobReport report, JsonObject job)
    {
        if (report.State == JobState.Succeeded && report.Result is { } result && JsonObject.Create(result) is { } envelope)
            return From(envelope);

        if (report.State == JobState.Running)
        {
            job["note"] = Messages.JobRunning(report.Uri);

            return Success(job);
        }

        return report.State == JobState.Cancelled
            ? Failure(Messages.JobCancelled, Messages.NotRaisedHint)
            : Failure(report.Message ?? "The job ended without a result.", null);
    }

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
    public static CallToolResult From(Answer<TestReport> answer) => From(Envelope(answer));

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

    private static JsonObject Ok(JsonObject data) => new() { ["ok"] = true, ["data"] = data };

    private static JsonObject NotOk<T>(Answer<T> answer) => new() { ["ok"] = false, ["error"] = answer.Error ?? "The call did not happen.", ["hint"] = answer.Hint };

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
