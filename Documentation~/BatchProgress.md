# Batched mission progress

Introduced in v0.4.0. `MissionModel` implements the optional `IMissionBatchProgress` capability. Existing `IMissionService` implementations and both single-event overloads keep their signatures; adapters do not need a new required member.

```csharp
var events = new[]
{
    new MissionProgressEvent("battle.shot", originalShotId, "battle", capturedWindowId),
    new MissionProgressEvent("battle.hit", originalHitId, "battle", capturedWindowId, damage)
};
IReadOnlyList<MissionProgressStatus> statuses = model.ReportProgressBatch(events);
```

Capture each window when producing the event, and retain its original ID and namespace through every retry. Do not mint replacement IDs, recapture windows or collapse distinct events into a sum. The API is synchronous and belongs to the same main-thread owner as the other model methods; it is not a thread-safe queue. Inputs must remain stable during the call.

The result contains one status per input in input order. Validation, encoded identity, wrong-window precedence, selected missions, distinct values, capped arithmetic and completed/claimed behavior match the scoped single-event API. A valid matching event records a receipt and returns Updated even if its matching missions were already complete or claimed. Rejected events do not consume an ID. A duplicate ID keeps the first accepted payload; it cannot change the amount on replay.

An empty list does nothing; a null list throws `ArgumentNullException`. All-invalid input does not advance the period. A nonempty batch containing structurally valid input evaluates the period once. Rollover and progress are prepared in one detached candidate, saved at most once, then published and notified once. Rollover still commits when all events refer to the previous window. When neither rollover nor progress changes state, there is no save or notification. GetState may independently refresh when called outside an operation, as before.

Persisted receipts seed an ordinal HashSet; receipts accepted earlier in the same call remain in this set even if Permanent retention evicts them from the persisted list. Periodic receipts last for their window, and old-window events return WrongWindow. Permanent preserves the configured historyLimit and eviction order across calls. An ID evicted from that bounded history can count again in a later call. This API does not promise indefinite exactly-once processing for arbitrary Permanent IDs.

## Save failure and retry

All commits save a detached snapshot before publishing the candidate. A Save exception can mean either no write or a write whose acknowledgement failed. The original exception propagates from progress/refresh/reroll; strict reward coordination may instead report its existing failure result. In either case, inspect the new `MissionModel.RequiresReload` property. A failed constructor is not a usable model. If automatic journal recovery in construction encounters an ambiguous Save failure, construction throws MissionReloadRequiredException; hosts can handle this recovery condition without suppressing unrelated configuration exceptions.

When RequiresReload is true:

1. Discard the model. Reads of GetState/Window return the last published snapshot without automatic refresh and may be stale.
2. Recreate or invalidate the persistence service if it can cache stale data. Reconstruct the model using authoritative, read-after-write storage. If reconstruction/load fails, keep the operation pending and do not use the old instance.
3. Retry the original input list, including original IDs, namespaces and captured windows. A committed candidate contains both progress and receipts; replay cannot double-count IDs still retained. An uncommitted candidate can apply on retry.

The old instance rejects further progress, refresh, claim, recovery and reroll mutations with MissionReloadRequiredException (an InvalidOperationException subtype). This stricter recovery rule also applies to failures in existing single-event/claim transactions. Callers previously retrying the same instance after a Save exception must reconstruct it. No save schema migration is needed.

StateChanged runs after publication while the model is Busy, so callback mutations return Busy and callback reads see committed state. A subscriber exception propagates after commit; it does not set RequiresReload or roll back progress. Retrying with the same retained IDs remains safe. Hosts must distinguish notification failure from persistence failure via RequiresReload.

## Copy ownership and performance

Progress transactions use an audited schema-v2 copier instead of JSON round-trip copying. It detaches dictionaries, progress records, distinct/receipt/selection lists, policies, claim snapshots and reward journals. MissionDefinition nodes contain only scalars, strings and immutable ResolvedRewardBundle values and may be shared; definition lists are detached. Reward bundles defensively own readonly entries. Legacy aliases point into the corresponding detached graph, never into the published model. Claim journals and reward coordinator identity remain authoritative.

The batch uses O(receipts + inputs) temporary identity storage; persisted state is still bounded only where the existing period policy specifies a bound. Persistence serializers can still allocate, and actual disk writes still serialize the entire save. See `Benchmarks~` for reproducible managed allocation measurements and Runtime tests for Unity/Datasave commit-count measurements.

## Consumer adoption gate

Do not replace the v0.2.0 public-DTO compatibility adapter in the consumer until an upstream release has been validated and published. This release builds on v0.3.1 and also carries that release's Economy/UniTask requirements; compatibility with the consumer's current dependency pins is a separate gate.

After pinning that validated release, translate QuestProgressEvent values to MissionProgressEvent and call the owned model directly. Remove pre-batch Refresh and unconditional success invalidation: the API handles rollover and keeps the live model current. Invalidate/reconstruct only when RequiresReload or authoritative storage recovery requires it, including claim/recovery failures. Keep forwarding duplicate Quest events to XP because a Quest transaction can succeed before an XP save fails. Preserve the host's reentry guard and pending input buffer.

Required consumer gates include CompletedBattleBatchRetriesAfterWeeklyFailureWithoutRepeatingDaily, PartialPeriodSaveRetriesOriginalPayloadWithoutRepeatingSavedPeriod, PartialSaveRetryAfterRolloverNeverRecapturesEitherWindow, XP partial-save regressions, and before/after-write retry cases. No cross-owner atomic Quest/XP transaction is introduced by this API.
