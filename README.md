# Dreamy Missions

One-time counter missions. The host reports authoritative gameplay events; the package owns catalog validation, saved counters and explicit reward claims.

Register `MissionInstaller.RegisterConfig(dataConfig)` before DataConfig initialization. Once DataConfig, Datasave and a persistent `IResourceWallet` are registered, call `MissionInstaller.Install()`.

```csharp
missions.ReportProgress("stage.win", "match-42:win", 1);
missions.Claim("win-3-stages");
```

Event IDs must be globally unique within a catalog. The latest 4096 IDs are persisted by default; older events can be counted again after eviction. This is bounded retry protection, not unlimited deduplication. Unknown event keys are not recorded. Progress saturates at target, and claimed missions stop accumulating.

Claim transactions use stable catalog/mission IDs. The wallet must provide durable idempotency for the full claim retry window: an already-granted ID succeeds without granting again. Wallet grant and mission save are separate operations; if save throws after the grant, retry with the same transaction ID. A bounded wallet ledger alone cannot guarantee indefinite claim deduplication. Never change published mission meaning/reward under an existing ID. Changing catalogId intentionally resets mission progress and permits a new set of claims. Daily/weekly resets are outside this MVP.

Import **Mission Feature** for the host-side UGUI/TMP panel and item. MissionPanel and MissionItem are prefab variants of BaseFeaturePanel and BaseFeatureItem in Dreamy Feature. The host owns localization, audio, gameplay adapters, UI lifecycle and art. The sample requires Dreamy UI; Runtime does not reference its assembly. Prefabs are also authored under `Assets/Samples/Dreamy Missions/0.1.0/Mission Feature` in this sandbox.
