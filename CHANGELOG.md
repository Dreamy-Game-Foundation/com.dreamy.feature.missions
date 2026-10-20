# Changelog

## Unreleased — 0.3.0 candidate

- Add optional IMissionClaimOffers and an engine-independent MissionRewardClaimSource. Bind confirmations to original window/mission/reroll/revision/payload, preserve old pending/completed query, and reject stale unstarted offers. Existing Claim API/save schema remains unchanged.
- New presentation integration requires Economy 0.7.0 and UniTask; release pins must be updated together.

## Unreleased

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
