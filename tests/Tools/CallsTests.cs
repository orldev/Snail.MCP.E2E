using Snail.MCP.E2E.Application;
using Snail.MCP.E2E.Tools;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.E2E.Tests.Tools;

/// <summary>What a model types into e2e_http, checked before anything is sent.</summary>
public sealed class CallsTests
{
    [Fact]
    public void Of_NoContentType_SendsJson()
    {
        var call = Calls.Of("POST", "/orders", "{}", null, null);

        Assert.Equal(Calls.DefaultContentType, call.Value!.ContentType);
    }

    [Fact]
    public void Of_HeaderWithItsColon_IsSplitAtTheFirstOne()
    {
        var call = Calls.Of("GET", "/orders", null, null, ["X-Trace: a:b"]);

        Assert.Equal(KeyValuePair.Create("X-Trace", "a:b"), Assert.Single(call.Value!.Headers));
    }

    /// <summary>The type went straight into StringContent, which threw FormatException the adapter did not catch.</summary>
    [Fact]
    public void Of_ContentTypeThatIsNoMediaType_IsRefusedAndSaysWhy()
    {
        var call = Calls.Of("POST", "/orders", "{}", "json please", null);

        Assert.Null(call.Value);
        Assert.Equal(Messages.BadContentType("json please"), call.Error);
        Assert.Equal(ToolFailures.Arguments, call.Type);
    }

    /// <summary>A header without a colon was dropped without a word: the model believed it had sent what it had not.</summary>
    [Theory]
    [InlineData("Authorization Bearer abc")]
    [InlineData(": value")]
    [InlineData("Bad Name: value")]
    public void Of_HeaderTheRequestCannotCarry_IsRefusedByName(string header)
    {
        var call = Calls.Of("GET", "/orders", null, null, [header]);

        Assert.Null(call.Value);
        Assert.Equal(Messages.BadHeader(header), call.Error);
        Assert.Equal(ToolFailures.Arguments, call.Type);
    }

    [Fact]
    public void Of_ContentHeaderWithoutABody_IsRefused()
    {
        var call = Calls.Of("GET", "/orders", null, null, ["Content-Language: de"]);

        Assert.Null(call.Value);
    }

    [Fact]
    public void Of_ContentHeaderWithABody_IsSent()
    {
        var call = Calls.Of("POST", "/orders", "{}", null, ["Content-Language: de"]);

        Assert.Single(call.Value!.Headers);
    }
}
