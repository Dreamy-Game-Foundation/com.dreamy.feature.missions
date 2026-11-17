# v0.4.0 validation

The release runtime/test source hashes are in `inputs.json`. They match the final successful test inputs; version, changelog and release documentation were changed afterward. These results describe the tested source/dependency combinations, not every possible consumer.

| Gate | Result | Receipt |
| --- | --- | --- |
| Missions Runtime and reward-presentation EditMode suites | 90/90 passed, zero skipped | package-final.xml |
| Minimum-dependency source host, batch regressions | 30/30 passed, zero skipped | minimum-unity.xml |
| Isolated migrated Planet Bunker Quest/XP consumer | 103/103 passed, zero skipped | consumer-migrated.xml |
| Android ARM64 IL2CPP probe | Succeeded, zero errors/warnings; APK signature v2 verified | inputs.json |
| Physical-device smoke/profiling | Not run: no device available | — |

Tests ran on Unity 6000.4.12f1/Linux. The minimum host compiles the actual relevant source from DataConfig/Datasave 0.2.0, Feature 0.2.0 and Economy 0.7.0, with exact commits in `minimum-dependencies.json`. This is source/API/runtime validation, not a complete minimum-version UPM graph rehearsal. Economy 0.7.0 has no remote release tag; its source commit is identified explicitly. Existing dependency declarations are unchanged.

Coverage includes sequential/batch equivalence, randomized chunks and reloads, ID encoding and duplicate replay, Permanent retention, max-long caps, distinct/completed/claimed behavior, wrong windows/rollover/rollback, callbacks/reentry, detached copy ownership, strict claim journal recovery, and before/after-write failures against both fault stores and concrete filesystem Datasave services. Consumer assertions were unchanged. An initial migration attempt exposed six claim-recovery failures; the corrected migration avoids implicit recovery during rendering and handles MissionReloadRequiredException from construction. All 103 selected tests then passed.

The consumer migration was exercised in an isolated snapshot using the source batch API. The actual consumer remains pinned to its prior version; this release does not change that pin. The final APK also uses an isolated minimum-source probe host, not a production game build. Its inclusion of libil2cpp.so for arm64-v8a and signature were checked; runtime output was not observed on a device.

`allocations-net10.jsonl` contains raw measurements from .NET 10.0.15/X64, three samples per case after warm-up: 4,096/50,000 existing receipts, 30/60/100 shots, one/three events per shot, additive/distinct objectives and eight completed claim journals. With 50,000 receipts and 100 shots × 3 events, median reference-store model allocations fell from 499.87 MB sequential to 2.88 MB batched; commits fell from 300 to 1. With JSON serialization included, allocations fell from 2,121.90 MB to 8.38 MB. These are managed .NET measurements, not device allocation claims. See `Benchmarks~` for reproduction.

Unity Mono returned unsupported zero values for GC.GetAllocatedBytesForCurrentThread; no zero-allocation claim is made. Unity real Datasave tests separately observed 100 events/50,000 receipts at 1,702.290 ms sequential versus 52.104 ms batched in one local run, with 100 versus 1 commits. Local timings are not performance guarantees.

Static toolkit project inspection encountered an unrelated third-party asmdef BOM parser error in the sandbox, and package-check on a package-only directory expected Unity project files. These checks are not counted as passes. Runtime asmdefs were unchanged and the direct Runtime-to-Editor reference check passed. No package release is a substitute for the consumer's dependency-resolution, device or rollout gates.
