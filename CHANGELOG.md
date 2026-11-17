# Changelog

## [0.4.0] - 2026-11-17

- Add immutable MissionProgressEvent and optional IMissionBatchProgress capability. ReportProgressBatch returns ordered per-input statuses, evaluates the period once and saves/publishes/notifies at most once, including rollover. Existing IMissionService and single-event signatures remain unchanged.
- Deduplicate persisted and earlier in-batch identities with an ordinal HashSet, retaining batch-local protection when Permanent history evicts receipts. Preserve encoded IDs, window validation, capped arithmetic, distinct values and completed/claimed behavior. Permanent cross-call replay protection remains bounded by historyLimit.
- Replace JSON round-trip copying of hot progress candidates and persistence snapshots with audited detached copies of mutable state, policies and reward journals. Save schema remains version 2; claim/reward identity and journaling remain authoritative.
- Add RequiresReload and MissionReloadRequiredException. After an ambiguous Save failure, discard the stale instance, reconstruct from authoritative persistence and replay original IDs/windows. Host migration: do not implicitly restart claim recovery while rendering a failed operation; handle construction-time recovery failures separately from configuration errors. Subscriber exceptions after commit do not roll back progress.
- Add transaction/reload/copy regressions and reproducible allocation benchmarks. Unity 6000.4.12f1 verification: package 90/90 tests, minimum-dependency source host 30/30, isolated migrated Quest/XP consumer 103/103. Android ARM64 IL2CPP probe builds with zero errors/warnings and a valid APK signature; physical-device execution/profiling was not run.
- Synchronize integration docs and dependency listing. No asset GUID, assembly dependency or consumer pin change. See Documentation~/BatchProgress.md and Documentation~/Validation~/v0.4.0/README.md for scope and receipts.

## [0.3.1] - 2026-11-06

- Hide the periodic-missions sample rebuild menu; keep the supplied sample assets and validation entry points. Runtime/save contracts are unchanged.
- Synchronize package metadata and README; no asset GUID or game-save migration. Prepared locally; no release tag or publication is implied.

## [0.3.0] - 2026-10-20 (source candidate; included in 0.3.1)

- Add optional IMissionClaimOffers and an engine-independent MissionRewardClaimSource. Bind confirmations to original window/mission/reroll/revision/payload, preserve old pending/completed query, and reject stale unstarted offers. Existing Claim API/save schema remains unchanged.
- New presentation integration requires Economy 0.7.0 and UniTask; release pins must be updated together.

## Historical periodic-mission and installer work (included in 0.2.0 and later)

- Add opt-in Daily/Weekly fixed-offset windows, injected clock/RNG, persisted revision snapshots and rollback high-water.
- Add persisted pool selection, explicit conflict relaxation, transactional free reroll, distinct-value objectives and window-scoped event replay protection.
- Add strict Economy gateway overload with embedded claim journals and isolated old-window recovery; retain legacy Permanent wallet/config behavior.
- Add deterministic periodic/recovery regressions; sample UI and host battle emitters are unchanged.

- Add an editable feature installer that registers services and presenters with the shared UI factory.
- Keep supplied views passive and delegate presenter lifetime to the shared host; update sample integration documentation.


## 0.1.0

- One-time counter missions, bounded event deduplication, persistent progress and explicit reward claims.
- Presenter/view contracts and copy-ready safe-area UGUI sample.
- MissionPanel and MissionItem sample prefabs inherit BaseFeaturePanel and BaseFeatureItem.
