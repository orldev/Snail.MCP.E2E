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
}
