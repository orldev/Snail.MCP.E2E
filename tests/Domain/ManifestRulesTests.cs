namespace Snail.MCP.E2E.Tests.Domain;

public sealed class ManifestRulesTests
{
    private static EnvironmentManifest Manifest(AppPart? app = null, Dictionary<string, ServicePart>? services = null, TestPart? tests = null) =>
        new(app ?? new AppPart("src/Shop.Api/Shop.Api.csproj", 8080, "/health", new Dictionary<string, string> { ["Mongo__Url"] = "mongodb://{mongo}:27017" }),
            services ?? new Dictionary<string, ServicePart> { ["mongo"] = new("mongo:8.0", 27017, new Dictionary<string, string>(), []) },
            tests);

    [Fact]
    public void Problems_ManifestOfTheShop_IsEmpty()
    {
        var tests = new TestPart("tests/Shop.E2E/Shop.E2E.csproj", new Dictionary<string, string> { ["APP_URL"] = "{app}" });

        Assert.Empty(ManifestRules.Problems(Manifest(tests: tests)));
    }

    [Theory]
    [InlineData("../other/Api.csproj")]
    [InlineData("/etc/Api.csproj")]
    [InlineData("src\\Api.csproj")]
    [InlineData("src/Api/Program.cs")]
    [InlineData("")]
    public void Problems_ProjectOutsideTheCopyOrNotAProject_IsNamed(string project)
    {
        var problems = ManifestRules.Problems(Manifest(new AppPart(project, 8080, "/health", new Dictionary<string, string>())));

        Assert.Contains(problems, problem => problem.StartsWith("app.project", StringComparison.Ordinal));
    }

    [Fact]
    public void Problems_MarkOfAServiceTheManifestLacks_IsNamed()
    {
        var app = new AppPart("src/Api/Api.csproj", 8080, "/health", new Dictionary<string, string> { ["Rabbit__Host"] = "{rabbit}" });

        var problems = ManifestRules.Problems(Manifest(app));

        Assert.Contains("app.env.Rabbit__Host names {rabbit}, which is neither app nor a service", problems);
    }

    [Theory]
    [InlineData("app")]
    [InlineData("tests")]
    [InlineData("Mongo")]
    [InlineData("my_db")]
    [InlineData("b--c")]
    [InlineData("db-")]
    [InlineData("1db")]
    [InlineData("a-name-longer-than-20")]
    public void Problems_ServiceWithANameThePlatformKeepsOrCannotHost_IsNamed(string name)
    {
        var services = new Dictionary<string, ServicePart> { [name] = new("mongo:8.0", null, new Dictionary<string, string>(), []) };

        var problems = ManifestRules.Problems(Manifest(new AppPart("src/Api/Api.csproj", 8080, "/health", new Dictionary<string, string>()), services));

        Assert.Contains(problems, problem => problem.Contains($"\"{name}\" cannot name a service", StringComparison.Ordinal));
    }

    [Fact]
    public void Problems_HealthThatIsNotAPathAndAPortOutOfRange_AreBothNamed()
    {
        var problems = ManifestRules.Problems(Manifest(new AppPart("src/Api/Api.csproj", 70000, "health", new Dictionary<string, string>())));

        Assert.Contains(problems, problem => problem.StartsWith("app.port", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("app.health", StringComparison.Ordinal));
    }

    /// <summary>The health check is sent from the server like a call of e2e_http, so a health path naming Nomad would reach Nomad.</summary>
    [Fact]
    public void Problems_HealthNamingAnotherHost_IsNamed()
    {
        var problems = ManifestRules.Problems(Manifest(new AppPart("src/Api/Api.csproj", 8080, "//nomad:4646/v1/jobs", new Dictionary<string, string>())));

        Assert.Contains(problems, problem => problem.StartsWith("app.health", StringComparison.Ordinal));
    }
}
