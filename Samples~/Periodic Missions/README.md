# Periodic Missions

Import **Periodic Missions** by itself from Package Manager. It does not use
Mission Feature scripts, prefab, Resources catalog, or installer. Both samples
can coexist: this assembly and namespace are `Dreamy.Feature.Missions.PeriodicSample`.
The original Permanent sample remains independent.

The two serialized TextAssets are `periodicDailyMissionCatalog.json` and
`periodicWeeklyMissionCatalog.json`. Do not copy them into Resources/DataConfig
or replace the Permanent catalog. Each selects three of six missions, with one
free reroll per window. Both explicitly use `PreferGroupsThenRelax`. These demo
policies use UTC+7, midnight cutoff, and Monday week start; these are catalog
choices, not timer rules. Every reward is a currency + clearly labeled fake-item
bundle, delivered in that order.

## Composition and opening

The host supplies its initialized `IDatasaveService`, `IRewardDeliveryGateway`,
and `IMissionClock`. Nothing creates a save service, wallet, backend, or global
service registry. The composition controller accepts dependencies explicitly:

```csharp
controller.Configure(save, gateway, clock);
```

For the supplied fake-item component, configure it **before** constructing mission
models, because model construction resumes saved deliveries. A host method with
an existing strict Economy wallet can compose the actual Economy gateway:

```csharp
using Dreamy.Datasave;
using Dreamy.Economy;
using Dreamy.Economy.Rewards;
using Dreamy.Missions;
using Dreamy.Feature.Missions.PeriodicSample;
using UnityEngine;

// Call from the application's composition root after initializing save and wallet.
void OpenPeriodicDemo(GameObject inactiveInstance, IDatasaveService save,
    DatasaveResourceWallet wallet, IMissionClock clock)
{
    var items = inactiveInstance.GetComponent<DemoPeriodicItemDestination>();
    var controller = inactiveInstance.GetComponent<PeriodicMissionDemoController>();
    items.Configure(save);
    var gateway = new RewardDeliveryGateway(new IRewardDestination[] { wallet, items });
    controller.Configure(save, gateway, clock);
    inactiveInstance.SetActive(true);
}
```

Instantiate the configured prefab under an existing Canvas with an EventSystem.
Keep the prefab root inactive during instantiation/configuration. All serialized
references below must already be assigned. Configure a given controller and item
destination once, not each time it opens. Close deactivates the root; reopen that
same instance with `instance.SetActive(true)`. Destroying it requires instantiating
and configuring a new instance. Preserve the host's gateway/owners for as long as
the models can be used.

This sample owns `MissionPresenter` directly and uses the standalone activation
lifecycle. Do not register a second presenter in `PanelPresenterFactory`, or open
this instance through `PanelManager.Show`, `UIPanel.Show` or `UIPanel.Hide`.
`UIPanel` inheritance supplies the expected panel component shape; close/reopen
here is deliberately driven by root activation. If integrating into a managed
panel navigation flow, adapt that lifecycle rather than binding two presenters.

Save keys are intentionally distinct from Permanent and from one another:

- `dreamy.periodic-sample.daily.v1`
- `dreamy.periodic-sample.weekly.v1`
- `dreamy.periodic-sample.demo-items.v1`

Use one configured demo instance per save lineage. The host owns the wallet save
key and must not run a second independent wallet owner over the same document.
Changing catalog identity, period policy, or an incompatible active revision
requires explicit migration, not a new save key to hide the old state.

## Exact prefab/component wiring

Create `PeriodicMissionDemoPanel` as an inactive RectTransform root under a Canvas.
Put `CanvasGroup`, `PeriodicMissionPanel`, `PeriodicMissionDemoController`, and
`DemoPeriodicItemDestination` on this same root. A `UITweenPlayer` is not needed
for this standalone activation flow. Assign inherited `canvasGroup` to the root
CanvasGroup; leave inherited `tweenPlayer` empty. Add a child ScrollRect/viewport
and a vertical layout content Transform; the content starts empty.

`PeriodicMissionPanel` serialized fields (all required):

| Field | Component / role |
| --- | --- |
| `closeButton` | Button labeled Close |
| `dailyTabButton` | Button labeled Daily |
| `weeklyTabButton` | Button labeled Weekly |
| `winButton` | Button labeled Simulate win +1 |
| `collectButton` | Button labeled Simulate collect +100 |
| `retryButton` | Button labeled Retry pending (both tabs) |
| `refreshButton` | Button labeled Refresh windows |
| `headingText` | TMP_Text for selected tab heading |
| `countdownText` | TMP_Text for reset countdown |
| `rerollText` | TMP_Text for remaining free reroll |
| `statusText` | TMP_Text with room for wrapping delivery/error details |
| `itemContainer` | Empty vertical content Transform in ScrollRect |
| `itemPrefab` | PeriodicMissionItem component on the row prefab asset |

Create a separate active row prefab named `PeriodicMissionItem`, with
`PeriodicMissionItem` on its RectTransform root. It is not a preplaced content
row. Give it enough height for two reward lines and a wrapping title.

| Row field | Component / role |
| --- | --- |
| `titleText` | TMP_Text mission title |
| `progressText` | TMP_Text progress / target |
| `rewardText` | TMP_Text currency and DEMO item lines |
| `claimLabel` | TMP_Text child label of claimButton |
| `claimButton` | Button for Claim / Retry |
| `rerollButton` | Button labeled Free reroll |

`PeriodicMissionDemoController` fields:

| Field | Reference |
| --- | --- |
| `dailyCatalog` | periodicDailyMissionCatalog.json TextAsset |
| `weeklyCatalog` | periodicWeeklyMissionCatalog.json TextAsset |
| `panel` | PeriodicMissionPanel on the same root |

`DemoPeriodicItemDestination` fields:

| Field | Reference |
| --- | --- |
| `pauseDeliveriesToggle` | Toggle labeled DEMO: pause item delivery (Pending); initially off |
| `rejectDeliveriesToggle` | Toggle labeled DEMO: reject item delivery (Error); initially off |
| `balanceText` | TMP_Text displaying DEMO tokens owned |

Leave Button.onClick and Toggle.onValueChanged persistent event lists empty.
Buttons are subscribed by the sample source; the fake destination reads toggle
values directly. Give the root suitable anchors/safe-area containment and use
layout/scrolling rather than fixed row positions. The source does not generate
or repair a prefab, locate controls by name, or create missing references.

## Behavior and manual QA

Daily/Weekly changes dispose the old presenter before binding the other model.
Disable releases the presenter and button listeners; enable binds once again.
Rows are pooled and updated on presenter state changes, not rebuilt each frame.
The only per-frame timer work is reading the injected clock and comparing its
whole UTC second. Once that changes, the visible model receives `Refresh` and
the countdown reads its immutable `Window` snapshot. Countdown text changes
only when the displayed remaining second/window changes. The timer never
clears progress, claims, selection, or reroll counts. Hidden models refresh
when selected or when a demo action/explicit refresh touches them.

Simulated win and collect emit unique, window-scoped demo events to both models.
Collect is progress only: it does not grant 100 coins to the wallet. Use actual
trusted business events in a game, retaining original event/window context for
replays. The sample does not generate distinct-value objectives.

Rehearse these on the actual wired prefab:

1. Reroll an incomplete row for free; another attempt in that tab/window is
   disabled. Switch tabs and confirm independent selection/reroll state.
2. Simulate enough wins/collects to complete a mission. Its bundle shows coins
   and DEMO tokens. Claim and observe the host wallet plus demo token count.
3. Enable pause before a new claim. Currency may commit first, while the item
   remains Pending. Disable pause and press Retry pending; the persisted bundle
   resumes without granting currency twice. This also retries old-window claims.
4. Enable reject on another new claim to display GrantFailed/error details;
   disable it before manually retrying. If both toggles are on, reject wins.
5. Close/reopen and repeatedly switch tabs: a click must cause one progress
   action/claim, not duplicate subscriptions. Restart with the same save lineage
   and confirm selected IDs, progress, claims, demo receipts and rerolls persist.
6. Inject a controlled `IMissionClock`, move it past a daily/weekly end, then
   click Refresh or let its next displayed second tick. Only domain Refresh
   chooses rollover. Move time backward: an old window must not reopen.
7. Check narrow/wide ratios, safe area, wrapping, scroll reachability, raycasts,
   missing references/scripts, and countdown without row churn in the Profiler.

The fake destination supports only kind `demo.periodic-item` and target
`demo.periodic-token`. It validates combined amounts and receipt fingerprints,
and writes token count plus transaction receipt in the same save candidate.
Its receipts are deliberately never pruned. It is demo-only local storage, not
a general inventory or backend. Bundle delivery is recoverable and idempotent
under owner/save-lineage contracts, not an atomic all-or-nothing bundle.
Retain mission journals and destination saves together; independent backup
rollback can require reconciliation.

Named time zones/DST, paid/ad rerolls, backend anti-cheat, inventory SDKs, and
production error remediation are outside this sample. Source inspection alone
does not establish Unity compilation, import, visual, or runtime/lifetime QA.
