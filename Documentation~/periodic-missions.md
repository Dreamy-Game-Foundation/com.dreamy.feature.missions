# Periodic Missions and durable claims

The released constructor and single-resource catalog remain supported. With no
`period`, Missions is Permanent and the entire catalog is active. Its legacy
claim ID remains `mission:<catalog-length>:<catalog>:<mission-length>:<mission>:claim`.
Changing a legacy Permanent catalog ID intentionally resets progress. Do not
change catalog identity to update periodic configuration.

## Public API

`MissionModel` has two constructors with the same trailing parameters:

```csharp
MissionModel(MissionCatalogConfig catalog, IDatasaveService datasave,
    IResourceWallet wallet, string saveKey = "missions", int historyLimit = 4096,
    IMissionClock clock = null, IMissionRandom random = null);
MissionModel(MissionCatalogConfig catalog, IDatasaveService datasave,
    IRewardDeliveryGateway gateway, string saveKey = "missions", int historyLimit = 4096,
    IMissionClock clock = null, IMissionRandom random = null);
```

The explicit `MissionInstaller.Install` overloads accept either reward boundary,
then `saveKey`, `clock`, and `random`. The parameterless installer remains the
legacy wallet composition. No installer supplies a clock, random source, wallet,
or persistence fallback.

`IMissionClock.UtcNow` returns `DateTimeOffset`. `IMissionRandom.Next(max)` must
return an integer in `[0,max)`. Periodic catalogs require a clock; configured
selection requires an RNG. The default full catalog never draws random numbers.

`IMissionService` exposes:

- Existing `GetState()`, `Claim(missionId)` and
  `ReportProgress(eventKey, eventId, amount = 1)`.
- `WindowId`, `Refresh()`, `Reroll(missionId)`, and `RecoverClaims()`.
- `ReportProgress(eventKey, eventId, eventNamespace, windowId, amount = 1,
  distinctValue = null)`.

`MissionModel` additionally exposes `RerollsUsed` and `LastDeliveryResult`.
`MissionClaimStatus.Pending` means the original claim requires recovery, not a
new request. `LastDeliveryResult` provides Economy reason/status/retryability.
Persistence failures in progress, rollover or reroll throw and leave the prior
state published. Coordinator persistence failures return Pending.

All operations are synchronous and main-thread-only. Save, clock, RNG, reward,
and `StateChanged` callbacks run under Busy; callback attempts to mutate return
Busy. `GetState()` checks rollover before returning state; while Busy it only
reads the committed state. `WindowId` reads the last accepted persisted window,
so call `Refresh()` before capturing it for a newly generated gameplay event.
Capture the window at the authoritative action, not when replaying that action.

## Catalog policy

```json
{
  "catalogId": "quests",
  "revision": "2026-10",
  "period": {
    "period": "Weekly",
    "offsetMinutes": 420,
    "cutoffMinutes": 0,
    "weekStart": "Monday",
    "freeRerolls": 1
  },
  "selection": {
    "poolId": "weekly",
    "count": 5,
    "conflictPolicy": "PreferGroupsThenRelax"
  },
  "missions": [{
    "id": "different-units",
    "titleKey": "mission.differentUnits",
    "eventKey": "battle.unitUsed",
    "target": 3,
    "objective": "DistinctValue",
    "poolId": "weekly",
    "conflictGroup": "unit-use",
    "rewardResourceId": "currency.coin",
    "rewardAmount": 10
  }]
}
```

This abbreviated catalog requires at least five eligible unique missions before
construction succeeds. `Daily` and `Weekly` windows are half-open UTC intervals
derived from fixed offset, local cutoff, and week start. Offset supports -840
through +840 minutes, cutoff 0 through 1439. This is not a named timezone/DST
policy or an anti-cheat clock. Window IDs encode period plus UTC start/end ticks,
not revision. Persisted start ticks are the high-water mark: rollback keeps the
accepted window; a forward jump opens only the actual current window, without
retroactive progress or reward.

Missing selection means full catalog. Selection defaults to Strict and throws
on insufficient eligible IDs/groups. PreferGroupsThenRelax first selects distinct
nonempty conflict groups, then fills from remaining unique IDs. Empty groups do
not conflict. `poolId` filters only when specified. Selected order/IDs and the
entire active definition/policy snapshot are saved before exposure; restart does
not draw again.

Reroll replaces only an active, incomplete, unclaimed mission and excludes the
entire prior selection, including the outgoing ID. It uses the same pool/group
policy; there is no paid/ad path. Free limit defaults to one per periodic window
and can be zero or higher. NoCandidate and save failure do not consume a reroll.
Permanent reroll is disabled.

Periodic revision changes are deferred until the next window. The active
snapshot can still progress/claim a definition removed from the new catalog;
the new window adopts the new revision. Changing period/offset/cutoff/week start
requires explicit migration rather than guessing a boundary transition.
Permanent revision changes accept only additive definitions with unchanged
existing objectives/targets/rewards and unchanged selection. Incompatible
Permanent changes require migration. Revision never creates a new claim ID.

## Progress and replay

Periodic progress rejects the legacy unscoped report overload. Supply the
original WindowId and an authoritative business-event namespace, for example
`battle:<battleId>:shot` plus the shot ordinal as eventId. Use separate action
namespaces for completed versus won events. Fields are length-prefixed internally,
so delimiters in host IDs cannot collide.

`DistinctValue` requires a nonempty stable `distinctValue`, such as a unit ID.
Two different events using the same unit increase its objective once, while
matching Additive missions still increase for each event. Values and progress
are stored per mission/window. Unknown event keys and invalid distinct values
do not consume event IDs. Progress saturates at target without integer overflow.

Permanent event history remains bounded by `historyLimit` (default 4096);
an evicted ID may count again. Periodic active-window event IDs are not subject
to that bound. A pending claim retains its original full window snapshot,
including events and distinct values. Reports for any retired window return
WrongWindow and cannot hit the current window; they do not alter archived progress.
The producer must retain original context when replaying an event. The package
cannot recognize an old event falsely labeled as newly produced in a new window.

## Typed rewards and recovery

Use either legacy `rewardResourceId`/`rewardAmount` or a resolved Economy bundle,
never both:

```json
"rewardBundle": {
  "schemaVersion": 1,
  "resolverVersion": "fixed-catalog-v1",
  "entries": [
    { "entryId": "coins", "kindId": "currency", "targetId": "currency.coin", "amount": 10 },
    { "entryId": "item", "kindId": "item", "targetId": "item.fixture", "amount": 1 }
  ]
}
```

The wallet constructor rejects bundles. The gateway constructor also accepts
single-resource missions, translated to a concrete currency entry. The host
registers and validates target owners through Economy; Missions contains no game
enums, mystery-card resolver, arbitrary payload, or independent journal store.

Save version 2 retains released `missions`/`processedEventIds` fields and adds
`activeWindow` and `pendingClaims`. Each pending claim embeds an Economy journal,
fixed bundle, catalog identity, mission ID, revision and full original window
snapshot. Intent is saved before dispatch, fixed resolution before delivery,
and acknowledged entries before feature finalization. Finalization stores
Completed plus the original snapshot's Claimed transition in one detached save
candidate. Only a matching current catalog/window is marked Claimed.

Startup recovers pending claims before rollover. Explicit `RecoverClaims()`
can finish old windows later; it attempts each incomplete journal once even when
another is unavailable. An unavailable old claim does not block the same
mission in a new window. Retries use persisted bundle/identity; no new resolution
or reward ID is invented after configuration changes. Completed journals and
snapshots are retained without arbitrary pruning. Measure save growth before
shipping a long-lived profile; compaction/migration tooling is not supplied.

The strict path is recoverably idempotent only when every reward owner durably
commits its effects, receipt and fingerprint together. Bundle delivery is not
globally atomic across owners. Legacy wallet bool success keeps its original,
weaker guarantee; switching a torn legacy claim to strict delivery cannot prove
an old owner receipt. Permanent strict requests keep the legacy claim request
ID, while Economy derives per-entry transaction IDs. A host migrating old wallet
receipts must reconcile those receipts; this package does not infer payloads or
repair independently rolled-back feature/owner saves.
The host must use a save key/owner namespace unique within the profile served by
the gateway. Package dependency/release pins must select the source revision
containing Economy's Rewards API; the previously published Economy API alone is
not sufficient for this unreleased Missions implementation.

## Validation scope

`MissionPeriodicTests` adds deterministic clock/RNG/save-owner fixtures for
boundary ticks, alternate week start/cutoff, year/leap day, high-water rollback,
revision snapshots, selection restart and conflict relaxation, reroll failure/
limits, distinct/replay separation, old-window recovery and every journal-save
checkpoint of a two-entry bundle. `MissionModelTests` retains the released
Permanent catalog-reset, bounded dedup, detached-save and wallet-reentry behavior.
Unity execution belongs to the integrating project; host battle emitters and
sample UI are outside this domain change.
