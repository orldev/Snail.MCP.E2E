using BenchmarkDotNet.Running;
using Snail.MCP.E2E.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(EnvironmentBenchmarks).Assembly).Run(args);
