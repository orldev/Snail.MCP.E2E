namespace Snail.MCP.E2E.Domain;

/// <summary>The end of one part's output: what it wrote to standard output and to standard error.</summary>
public sealed record LogTail(string Part, string Stdout, string Stderr);
