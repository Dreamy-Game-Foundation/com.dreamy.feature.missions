# Progress allocation benchmark

Requires .NET 10 SDK, sibling Dreamy source packages from the validated consumer, and its Newtonsoft.Json DLL. No NuGet package download is needed. The project compiles the real source files; it does not substitute a model implementation.

```sh
dotnet run -c Release --project Missions.Benchmarks.csproj \
  -p:NewtonsoftJsonPath=/absolute/path/to/Newtonsoft.Json.dll
```

Pass `-p:DreamyPackagesRoot=/absolute/path/to/sibling/packages/` when the dependencies are elsewhere. Keep the trailing slash. Redirect stdout to a JSONL receipt and record the source/dependency commits and diff hash with it. The first line identifies the runtime; subsequent lines contain raw samples. Builds may print diagnostic lines before JSONL output; use `dotnet build` first, then `dotnet run --no-build` to capture only measurements.

The matrix uses 4,096/50,000 existing receipts, 30/60/100 shots, one/three events per shot, an additive and a distinct objective, and eight completed claim journals. It compares sequential/batched model operations, both with reference storage and with JSON serialization, with three samples per case after warm-up. Fixture construction, load, input creation and constructor saves are outside each measured region. Persistence serialization and detached save snapshots are inside it.

This is .NET managed allocation evidence, not Unity Mono/IL2CPP allocation or actual Datasave filesystem latency. Unity EditMode benchmarks separately verify commit/notification counts and Editor timing. On Unity runtimes where `GC.GetAllocatedBytesForCurrentThread` returns zero, they explicitly report allocation as unsupported instead of treating zero as a measurement.
