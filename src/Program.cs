using Snail.MCP.E2E.Extensions;

var app = WebApplication.CreateBuilder(args)
    .ConfigureEnvironmentServer()
    .Build()
    .UseEnvironmentEndpoints();

await app.RunAsync();

/// <summary>The entry point, named so the contract tests can host the server in memory.</summary>
public partial class Program;
