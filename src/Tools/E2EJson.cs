using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Snail.MCP.E2E.Tools;

/// <summary>The types a job of this server ends with, generated so nothing is looked up at run time.</summary>
/// <remarks>A raise and a test run end with the same JSON their tool answers with when the work ends in time, so the job's result reads like the tool's answer.</remarks>
[JsonSerializable(typeof(JsonObject))]
public sealed partial class E2EJson : JsonSerializerContext;
