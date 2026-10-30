namespace Snail.MCP.E2E.Tests.Domain;

public sealed class AppAddressTests
{
    private static readonly Uri App = new("http://e2e-0c2b9e1f--app:8080/");

    /// <summary>The first path once reached Nomad's API: joined with its slash cut, it was an absolute address.</summary>
    [Theory]
    [InlineData("/http://nomad:4646/v1/jobs", "http://e2e-0c2b9e1f--app:8080/http://nomad:4646/v1/jobs")]
    [InlineData("/orders?status=new", "http://e2e-0c2b9e1f--app:8080/orders?status=new")]
    [InlineData("/", "http://e2e-0c2b9e1f--app:8080/")]
    [InlineData("/api/../health", "http://e2e-0c2b9e1f--app:8080/health")]
    [InlineData("/@nomad:4646/x", "http://e2e-0c2b9e1f--app:8080/@nomad:4646/x")]
    public void Of_PathStartingWithOneSlash_StaysOnTheApp(string path, string expected)
    {
        Assert.Equal(expected, AppAddress.Of(App, path)?.ToString());
    }

    [Theory]
    [InlineData("//nomad:4646/v1/jobs")]
    [InlineData("//evil.example/x")]
    [InlineData("orders")]
    [InlineData("http://nomad:4646/v1/jobs")]
    [InlineData("")]
    public void Of_PathNamingAnotherHostOrNoRoot_IsNull(string path)
    {
        Assert.Null(AppAddress.Of(App, path));
    }

    [Fact]
    public void IsAppPath_PathNamingAHostOfItsOwn_IsRefusedWhateverTheApp()
    {
        Assert.False(AppAddress.IsAppPath("//nomad:4646/v1/jobs"));
    }
}
