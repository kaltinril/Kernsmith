using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(FontGenerationBenchmarks).Assembly).Run(args);
