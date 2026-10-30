using System.Diagnostics;
using Microsoft.Extensions.Options;
using Snail.MCP.E2E.Adapters;
using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Configuration;

namespace Snail.MCP.E2E.Tests.Adapters;

public sealed class FileManifestsTests : IDisposable
{
    private readonly string _copy = TestFolder.Create();

    public void Dispose() => Directory.Delete(_copy, recursive: true);

    private Task<Answer<EnvironmentManifest>> ReadAsync(string? text)
    {
        if (text is not null)
        {
            Directory.CreateDirectory(Path.Combine(_copy, ".snail"));
            File.WriteAllText(Path.Combine(_copy, ".snail", "environment.json"), text);
        }

        return new FileManifests(Options.Create(new E2EOptions())).ReadAsync(_copy, CancellationToken.None);
    }

    [Fact]
    public async Task ReadAsync_ShortManifest_FillsThePortAndTheHealthPath()
    {
        var answer = await ReadAsync("""{ "app": { "project": "src/Api/Api.csproj" } }""");

        Assert.Equal((8080, "/health"), (answer.Value!.App.Port, answer.Value.App.Health));
        Assert.Empty(answer.Value.Services);
        Assert.Null(answer.Value.Tests);
    }

    [Fact]
    public async Task ReadAsync_ManifestWithCommentsAndTrailingCommas_IsRead()
    {
        var answer = await ReadAsync("""
            {
              // the shop
              "app": { "project": "src/Api/Api.csproj", "env": { "Mongo__Url": "mongodb://{mongo}:27017" }, },
              "services": { "mongo": { "image": "mongo:8.0", "port": 27017 } },
            }
            """);

        Assert.Equal("mongo:8.0", answer.Value!.Services["mongo"].Image);
    }

    [Fact]
    public async Task ReadAsync_NoManifest_SaysWhereAndHowToWriteOne()
    {
        var answer = await ReadAsync(null);

        Assert.Equal(Messages.NoManifest(".snail/environment.json"), answer.Error);
        Assert.Equal(Messages.NoManifestHint, answer.Hint);
    }

    [Fact]
    public async Task ReadAsync_BrokenJson_SaysWhatTheReaderSaid()
    {
        var answer = await ReadAsync("""{ "app": """);

        Assert.StartsWith("The environment manifest at .snail/environment.json cannot be read", answer.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_ManifestBreakingRules_NamesEveryRule()
    {
        var answer = await ReadAsync("""{ "app": { "project": "../x.csproj", "health": "health" } }""");

        Assert.Contains("app.project", answer.Error, StringComparison.Ordinal);
        Assert.Contains("app.health", answer.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_ExampleTheHintGives_IsAManifestTheRulesAccept()
    {
        var answer = await ReadAsync(Messages.ExampleManifest);

        Assert.Null(answer.Error);
    }

    /// <summary>
    /// A named pipe reports a length of 0, and opening it blocks until a writer comes, synchronously and out of reach of
    /// any cancellation: the raise's job held a pool thread for ever, and called on the test's own thread the whole run hung.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ManifestThatIsAPipe_IsRefusedWithoutOpeningIt()
    {
        string manifest = Path.Combine(_copy, ".snail", "environment.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        using (var mkfifo = Process.Start("mkfifo", manifest))
            await mkfifo.WaitForExitAsync();

        try
        {
            var answer = await Task.Run(() => ReadAsync(null)).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("not a regular file", answer.Error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(manifest);
        }
    }

    [Fact]
    public async Task ReadAsync_EmptyFile_IsRefused()
    {
        var answer = await ReadAsync(string.Empty);

        Assert.Contains("empty", answer.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The server sees every task's copy. A manifest linked to another task's file was read through the link, and the
    /// rules quoted its values — a health path, a project — back to the task that made the link.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ManifestLinkedIntoAnotherTasksCopy_IsRefusedWithoutItsValues()
    {
        string theirs = Theirs("""{ "app": { "project": "src/Secret/Secret.csproj", "health": "secret-health-path" } }""");
        Directory.CreateDirectory(Path.Combine(_copy, ".snail"));
        File.CreateSymbolicLink(Path.Combine(_copy, ".snail", "environment.json"), Path.Combine(theirs, ".snail", "environment.json"));

        var answer = await ReadAsync(null);

        Assert.Null(answer.Value);
        Assert.Contains("link", answer.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-health-path", answer.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_ManifestFolderLinkedIntoAnotherTasksCopy_IsRefused()
    {
        string theirs = Theirs(Messages.ExampleManifest);
        Directory.CreateSymbolicLink(Path.Combine(_copy, ".snail"), Path.Combine(theirs, ".snail"));

        var answer = await ReadAsync(null);

        Assert.Null(answer.Value);
        Assert.Contains("link", answer.Error, StringComparison.Ordinal);
    }

    /// <summary>Another task's copy beside this one, holding a manifest of <paramref name="text"/>.</summary>
    private string Theirs(string text)
    {
        string theirs = Path.Combine(_copy, "theirs");
        Directory.CreateDirectory(Path.Combine(theirs, ".snail"));
        File.WriteAllText(Path.Combine(theirs, ".snail", "environment.json"), text);

        return theirs;
    }
}
