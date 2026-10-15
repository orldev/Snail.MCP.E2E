using Microsoft.AspNetCore.Http;
using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Application.Ports;
using Snail.MCP.E2E.Configuration;

namespace Snail.MCP.E2E.Tests.Application;

/// <summary>The environments service over fakes: a session opened at an address, a manifest, a scheduler and an app that answer what a test says.</summary>
internal sealed class Stage
{
    public const string Copy = "/workspaces/0c2b9e1f/step-2";

    public static readonly EnvironmentManifest Shop = new(
        new AppPart("src/Shop.Api/Shop.Api.csproj", 8080, "/health", new Dictionary<string, string>()),
        new Dictionary<string, ServicePart> { ["mongo"] = new("mongo:8.0", 27017, new Dictionary<string, string>(), []) },
        new TestPart("tests/Shop.E2E/Shop.E2E.csproj", new Dictionary<string, string> { ["APP_URL"] = "{app}" }));

    public FakeJobs Jobs { get; } = new();

    public FakeCalls Calls { get; } = new();

    public EnvironmentManifest? Manifest { get; set; } = Shop;

    public E2EOptions Options { get; } = new() { StartSeconds = 0 };

    public TaskEnvironments Environments(string? root = Copy)
    {
        var context = new DefaultHttpContext();
        if (root is not null)
            context.Request.QueryString = QueryString.Create(SessionCopy.Parameter, root);

        var options = Microsoft.Extensions.Options.Options.Create(Options);
        var session = new SessionCopy(new HttpContextAccessor { HttpContext = context }, options);

        return new TaskEnvironments(session, new FakeManifests(this), Jobs, Calls, new EnvironmentUse(TimeProvider.System), options, TimeProvider.System);
    }

    private sealed class FakeManifests(Stage stage) : IManifests
    {
        public Task<Answer<EnvironmentManifest>> ReadAsync(string copy, CancellationToken cancellationToken) =>
            Task.FromResult(stage.Manifest is { } manifest ? Answer.Ok(manifest) : Answer.Failed<EnvironmentManifest>(Messages.NoManifest(".snail/environment.json"), Messages.NoManifestHint));
    }
}

internal sealed class FakeJobs : IEnvironmentJobs
{
    public List<EnvironmentPlan> Raised { get; } = [];

    public List<TestPlan> Tested { get; } = [];

    public EnvironmentRun? Run { get; set; }

    public Dictionary<string, string> Logs { get; } = [];

    public TestRun Tests { get; set; } = new("Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1", false);

    public Task RaiseAsync(EnvironmentPlan plan, CancellationToken cancellationToken)
    {
        Raised.Add(plan);
        Run ??= new EnvironmentRun("a1", [new PartState("app", "running", 0, false), new PartState("mongo", "running", 0, false)]);

        return Task.CompletedTask;
    }

    public Task<EnvironmentRun?> RunAsync(TaskEnvironment name, CancellationToken cancellationToken) => Task.FromResult(Run);

    public Task<bool> HasStoppedAsync(TaskEnvironment name, CancellationToken cancellationToken)
    {
        bool hadRun = Run is not null;
        Run = null;

        return Task.FromResult(hadRun);
    }

    public Task<string> LogsAsync(TaskEnvironment name, string part, bool isError, int bytes, CancellationToken cancellationToken) =>
        Task.FromResult(Logs.GetValueOrDefault($"{part}:{(isError ? "err" : "out")}", string.Empty));

    public Task<TestRun> TestAsync(TestPlan plan, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Tested.Add(plan);

        return Task.FromResult(Tests);
    }

    public Task<IReadOnlyList<TaskEnvironment>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskEnvironment>>(Run is null ? [] : [TaskEnvironment.Of("0c2b9e1f")!.Value]);

    public Task<Answer<string>> ReachAsync(CancellationToken cancellationToken) => Task.FromResult(Answer.Ok("2.0.5"));
}

internal sealed class FakeCalls : IAppCalls
{
    public List<(Uri App, HttpCall Call)> Sent { get; } = [];

    public HttpExchange? Answer { get; set; } = new(200, new Dictionary<string, string>(), "ok", false, 3);

    public Task<HttpExchange?> SendAsync(Uri app, HttpCall call, int limit, CancellationToken cancellationToken)
    {
        Sent.Add((app, call));

        return Task.FromResult(Answer);
    }
}
