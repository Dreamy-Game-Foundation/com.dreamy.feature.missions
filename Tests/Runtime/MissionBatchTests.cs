using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Dreamy.DataConfig;
using Dreamy.Datasave;
using Dreamy.Economy;
using Dreamy.Economy.Rewards;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Dreamy.Missions.Tests
{
    public sealed class MissionBatchTests
    {
        private static MissionCatalogConfig Config(bool periodic = false, bool distinct = false, long target = 1000000)
        {
            var definition = new JObject
            {
                ["id"] = "a", ["titleKey"] = "a", ["eventKey"] = "shot", ["target"] = target,
                ["rewardResourceId"] = "currency.coin", ["rewardAmount"] = 1
            };
            var missions = new JArray(definition);
            if (distinct)
            {
                var other = (JObject)definition.DeepClone();
                other["id"] = "distinct"; other["objective"] = "DistinctValue"; missions.Add(other);
            }
            var doc = new JObject { ["catalogId"] = "batch", ["missions"] = missions };
            if (periodic) { doc["period"] = new JObject { ["period"] = "Daily" }; doc["revision"] = "one"; }
            return JsonConvert.DeserializeObject<MissionCatalogConfig>(doc.ToString(), DataConfigJson.Settings);
        }

        private static MissionModel Model(Store store, Clock clock = null, bool distinct = false,
            int limit = 4096, long target = 1000000) =>
            new(Config(clock != null, distinct, target), store, new Wallet(), historyLimit: limit, clock: clock);

        private static MissionProgressEvent E(MissionModel model, string id, long amount = 1, string value = null,
            string key = "shot", string ns = "game") => new(key, id, ns, model.WindowId, amount, value);

        [Test] public void Batch_MatchesSequentialStateAndStatuses_WithMixedInputs()
        {
            Store one = new(), many = new();
            MissionModel sequential = Model(one, distinct: true, target: 4), batch = Model(many, distinct: true, target: 4);
            var inputs = new[]
            {
                E(batch, "one", value: "A"), E(batch, "one", value: "B"), default,
                E(batch, "missing", key: "none"), E(batch, "missing", value: "A"),
                E(batch, "blank"), E(batch, "two", long.MaxValue, "B"), E(batch, "three", value: "C"),
                E(batch, "four", value: "D"), E(batch, "five", value: "E")
            };
            var expected = inputs.Select(e => sequential.ReportProgress(e.EventKey, e.OriginalId,
                e.EventNamespace, e.WindowId, e.Amount, e.DistinctValue)).ToArray();
            int saves = many.Saves, notifications = 0;
            batch.StateChanged += () => notifications++;
            Assert.That(batch.ReportProgressBatch(inputs), Is.EqualTo(expected));
            Assert.That(JsonConvert.SerializeObject(many.Data), Is.EqualTo(JsonConvert.SerializeObject(one.Data)));
            Assert.That(many.Saves - saves, Is.EqualTo(1));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(batch.GetState().Select(s => s.Progress), Is.EqualTo(new long[] { 4, 4 }));
            Assert.That(many.Data.ActiveWindow.Missions["distinct"].DistinctValues, Is.EqualTo(new[] { "A", "B", "C", "D" }));
        }

        [Test] public void Duplicates_InAcrossBatchesAndReload_KeepOriginalIdentity()
        {
            Store store = new(); MissionModel model = Model(store);
            var inputs = new[] { E(model, "a:|b/Đ"), E(model, "a:|b/Đ"), E(model, "a:|b/Đ", ns: "other") };
            Assert.That(model.ReportProgressBatch(inputs), Is.EqualTo(new[]
                { MissionProgressStatus.Updated, MissionProgressStatus.AlreadyProcessed, MissionProgressStatus.Updated }));
            Assert.That(model.ReportProgressBatch(inputs).All(s => s == MissionProgressStatus.AlreadyProcessed), Is.True);
            model = Model(store);
            Assert.That(model.ReportProgressBatch(inputs).All(s => s == MissionProgressStatus.AlreadyProcessed), Is.True);
            Assert.That(model.GetState()[0].Progress, Is.EqualTo(2));
            Assert.That(store.Data.ProcessedEventIds[0], Is.EqualTo(RewardIdentity.Encode("mission-event", "game", "permanent", "a:|b/Đ")));
            // Existing raw legacy identity remains separate from a namespaced identity.
            Assert.That(model.ReportProgress("shot", "a:|b/Đ"), Is.EqualTo(MissionProgressStatus.Updated));
        }

        [TestCase(17, false)] [TestCase(913, false)]
        [TestCase(17, true)] [TestCase(913, true)]
        public void RandomizedChunksAndReload_MatchSequentialPublicApi(int seed, bool periodic)
        {
            var random = new Random(seed);
            Store one = new(), many = new();
            Clock clock = periodic ? new Clock() : null;
            var sequential = Model(one, clock, distinct: true, target: 37);
            var batch = Model(many, clock, distinct: true, target: 37);
            for (int chunk = 0; chunk < 20; chunk++)
            {
                var inputs = new MissionProgressEvent[random.Next(1, 31)];
                for (int i = 0; i < inputs.Length; i++)
                {
                    int amountKind = random.Next(5), keyKind = random.Next(8);
                    inputs[i] = new MissionProgressEvent(keyKind == 0 ? null : keyKind == 1 ? "missing" : "shot",
                        random.Next(15) == 0 ? " " : "event-" + random.Next(50),
                        random.Next(20) == 0 ? null : "ns:" + random.Next(3),
                        random.Next(10) == 0 ? "wrong-window" : batch.WindowId,
                        amountKind == 0 ? 0 : amountKind == 1 ? -1 : amountKind == 2 ? long.MaxValue : random.Next(1, 10),
                        random.Next(5) == 0 ? null : "value-" + random.Next(60));
                }
                var expected = inputs.Select(e => sequential.ReportProgress(e.EventKey, e.OriginalId,
                    e.EventNamespace, e.WindowId, e.Amount, e.DistinctValue)).ToArray();
                Assert.That(batch.ReportProgressBatch(inputs), Is.EqualTo(expected), "chunk " + chunk);
                Assert.That(JsonConvert.SerializeObject(many.Data), Is.EqualTo(JsonConvert.SerializeObject(one.Data)), "chunk " + chunk);
                if (chunk % 3 == 0)
                {
                    sequential = Model(one, clock, distinct: true, target: 37);
                    batch = Model(many, clock, distinct: true, target: 37);
                }
            }
        }

        [Test] public void PermanentEviction_DoesNotForgetEarlierItemsWithinBatch()
        {
            Store store = new(); MissionModel model = Model(store, limit: 2);
            var inputs = new[] { E(model, "one"), E(model, "two"), E(model, "three"), E(model, "one") };
            Assert.That(model.ReportProgressBatch(inputs).Last(), Is.EqualTo(MissionProgressStatus.AlreadyProcessed));
            Assert.That(model.GetState()[0].Progress, Is.EqualTo(3));
            Assert.That(store.Data.ProcessedEventIds.Count, Is.EqualTo(2));
            // Across calls the released bounded-retention contract still applies.
            Assert.That(model.ReportProgressBatch(new[] { inputs[0] })[0], Is.EqualTo(MissionProgressStatus.Updated));
        }

        [Test] public void CounterCaps_ClaimedAndCompletedMissionsStillConsumeReceipts()
        {
            Store store = new(); MissionModel model = Model(store, target: long.MaxValue);
            Assert.That(model.ReportProgressBatch(new[] { E(model, "bad", 0), E(model, "negative", -1),
                E(model, "one", long.MaxValue - 1), E(model, "two", long.MaxValue) }),
                Is.EqualTo(new[] { MissionProgressStatus.InvalidEvent, MissionProgressStatus.InvalidEvent,
                    MissionProgressStatus.Updated, MissionProgressStatus.Updated }));
            Assert.That(model.GetState()[0].Progress, Is.EqualTo(long.MaxValue));
            model.ReportProgressBatch(new[] { E(model, "completed") });
            Assert.That(model.Claim("a"), Is.EqualTo(MissionClaimStatus.Claimed));
            model.ReportProgressBatch(new[] { E(model, "claimed", long.MaxValue) });
            model = Model(store, target: long.MaxValue);
            Assert.That(model.GetState()[0].IsClaimed, Is.True);
            Assert.That(model.GetState()[0].Progress, Is.EqualTo(long.MaxValue));
            Assert.That(model.ReportProgressBatch(new[] { E(model, "completed"), E(model, "claimed") }),
                Is.All.EqualTo(MissionProgressStatus.AlreadyProcessed));
        }

        [Test] public void RolloverAndRollback_UseOneClockReadAndOneCommit()
        {
            Store store = new(); Clock clock = new(); MissionModel model = Model(store, clock);
            string old = model.WindowId;
            clock.Now = clock.Now.AddDays(1);
            string current = Config(true).Period.GetWindow(clock.Now).Id;
            int saves = store.Saves, reads = clock.Reads, notifications = 0;
            model.StateChanged += () => notifications++;
            var inputs = new[] { E(model, "old"), new MissionProgressEvent("shot", "new", "game", current) };
            Assert.That(model.ReportProgressBatch(inputs), Is.EqualTo(new[] { MissionProgressStatus.WrongWindow, MissionProgressStatus.Updated }));
            Assert.That(clock.Reads - reads, Is.EqualTo(1));
            Assert.That(store.Saves - saves, Is.EqualTo(1));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(store.Data.ActiveWindow.Missions["a"].Progress, Is.EqualTo(1));
            clock.Now = clock.Now.AddDays(-1);
            Assert.That(model.ReportProgressBatch(new[] { new MissionProgressEvent("shot", "rollback", "game", old) })[0],
                Is.EqualTo(MissionProgressStatus.WrongWindow));
            Assert.That(model.WindowId, Is.EqualTo(current));
        }

        [Test] public void EmptyInvalidAndDuplicateOnlyBatches_DoNotCommitOrNotify()
        {
            Store store = new(); Clock clock = new(); MissionModel model = Model(store, clock);
            var input = E(model, "one"); model.ReportProgressBatch(new[] { input });
            int saves = store.Saves, reads = clock.Reads, notifications = 0;
            model.StateChanged += () => notifications++;
            Assert.That(model.ReportProgressBatch(new[] { input })[0], Is.EqualTo(MissionProgressStatus.AlreadyProcessed));
            Assert.That(store.Saves, Is.EqualTo(saves));
            clock.Now = clock.Now.AddDays(1); reads = clock.Reads;
            Assert.That(model.ReportProgressBatch(Array.Empty<MissionProgressEvent>()), Is.Empty);
            Assert.That(model.ReportProgressBatch(new MissionProgressEvent[2]), Is.All.EqualTo(MissionProgressStatus.InvalidEvent));
            Assert.Throws<ArgumentNullException>(() => model.ReportProgressBatch(null));
            Assert.That(clock.Reads, Is.EqualTo(reads));
            Assert.That(store.Saves, Is.EqualTo(saves));
            Assert.That(notifications, Is.Zero);
            Assert.That(model.ReportProgressBatch(new[] { input })[0], Is.EqualTo(MissionProgressStatus.WrongWindow));
            Assert.That(store.Saves, Is.EqualTo(saves + 1));
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(false)] [TestCase(true)]
        public void PersistenceFailure_RequiresAuthoritativeReload_ThenRetryIsExactlyOnce(bool afterWrite)
        {
            Store store = new(); MissionModel model = Model(store);
            var inputs = new[] { E(model, "one"), E(model, "two") };
            int notifications = 0; model.StateChanged += () => notifications++;
            store.FailBefore = !afterWrite; store.FailAfter = afterWrite;
            Assert.Throws<InvalidOperationException>(() => model.ReportProgressBatch(inputs));
            Assert.That(model.RequiresReload, Is.True);
            Assert.That(model.GetState()[0].Progress, Is.Zero);
            Assert.That(notifications, Is.Zero);
            store.FailBefore = store.FailAfter = false;
            Assert.Throws<MissionReloadRequiredException>(() => model.ReportProgressBatch(inputs));
            Assert.Throws<MissionReloadRequiredException>(() => model.ReportProgress("shot", "unsafe"));
            Assert.Throws<MissionReloadRequiredException>(() => model.Refresh());
            Assert.Throws<MissionReloadRequiredException>(() => model.Reroll("a"));
            Assert.Throws<MissionReloadRequiredException>(() => model.Claim("a"));
            Assert.Throws<MissionReloadRequiredException>(() => model.RecoverClaims());
            store.FailLoad = true;
            Assert.Throws<InvalidOperationException>(() => Model(store));
            Assert.That(model.RequiresReload, Is.True);
            store.FailLoad = false;
            model = Model(store);
            Assert.That(model.ReportProgressBatch(inputs), Is.All.EqualTo(afterWrite
                ? MissionProgressStatus.AlreadyProcessed : MissionProgressStatus.Updated));
            model = Model(store);
            Assert.That(model.GetState()[0].Progress, Is.EqualTo(2));
            Assert.That(model.ReportProgressBatch(inputs), Is.All.EqualTo(MissionProgressStatus.AlreadyProcessed));
        }

        [TestCase(false)] [TestCase(true)] public void RolloverFailure_DoesNotPublishPartialWindow(bool afterWrite)
        {
            Store store = new(); Clock clock = new(); MissionModel model = Model(store, clock);
            string old = model.WindowId; clock.Now = clock.Now.AddDays(1);
            var input = new MissionProgressEvent("shot", "new", "game", Config(true).Period.GetWindow(clock.Now).Id);
            store.FailBefore = !afterWrite; store.FailAfter = afterWrite;
            Assert.Throws<InvalidOperationException>(() => model.ReportProgressBatch(new[] { input }));
            Assert.That(model.WindowId, Is.EqualTo(old));
            Assert.That(model.GetState()[0].Progress, Is.Zero);
            store.FailBefore = store.FailAfter = false;
            model = Model(store, clock);
            model.ReportProgressBatch(new[] { input });
            Assert.That(model.GetState()[0].Progress, Is.EqualTo(1));
        }

        [Test] public void SaveAndNotificationReentry_AreBusy_AndSubscriberFailureIsAfterCommit()
        {
            Store store = new(); MissionModel model = Model(store);
            var input = E(model, "one");
            Action check = () =>
            {
                Assert.That(model.ReportProgressBatch(new[] { input }), Is.All.EqualTo(MissionProgressStatus.Busy));
                Assert.That(model.ReportProgress("shot", "nested"), Is.EqualTo(MissionProgressStatus.Busy));
                Assert.That(model.Claim("a"), Is.EqualTo(MissionClaimStatus.Busy));
                Assert.That(model.Refresh(), Is.EqualTo(MissionRefreshStatus.Busy));
                Assert.That(model.Reroll("a"), Is.EqualTo(MissionRerollStatus.Busy));
                Assert.That(model.RecoverClaims(), Is.EqualTo(MissionClaimStatus.Busy));
            };
            store.OnSave = () => { check(); Assert.That(model.GetState()[0].Progress, Is.Zero); };
            Action subscriber = () => { check(); Assert.That(model.GetState()[0].Progress, Is.EqualTo(1)); throw new ApplicationException("subscriber"); };
            model.StateChanged += subscriber;
            Assert.Throws<ApplicationException>(() => model.ReportProgressBatch(new[] { input }));
            Assert.That(model.RequiresReload, Is.False);
            model.StateChanged -= subscriber; store.OnSave = null;
            Assert.That(model.ReportProgressBatch(new[] { input }), Is.All.EqualTo(MissionProgressStatus.AlreadyProcessed));
            Assert.That(model.ReportProgressBatch(new[] { E(model, "two") }), Is.All.EqualTo(MissionProgressStatus.Updated));
        }

        [Test] public void DetachedCopies_PreservePoliciesDefinitionsAndJournal_WithoutMutableAliases()
        {
            Store store = new(); MissionModel seed = Model(store);
            var window = store.Data.ActiveWindow;
            window.Period.FreeRerolls = 7;
            window.Selection = new MissionSelectionPolicy { Count = 1, PoolId = "p", ConflictPolicy = MissionConflictPolicy.PreferGroupsThenRelax };
            var bundle = new ResolvedRewardBundle(1, "test", new[] { new ResolvedRewardEntry("entry", "currency", "currency.coin", 9) });
            var journal = new RewardJournal
            {
                SchemaVersion = 1, RequestId = "old-claim", RequestFingerprint = "fingerprint", State = RewardJournalState.Completed,
                BundleSchemaVersion = 1, ResolverVersion = "test", ResolvedFingerprint = bundle.Fingerprint,
                Entries = new List<RewardJournalEntry> { new() { EntryId = "entry", KindId = "currency", TargetId = "currency.coin", Amount = 9, Acknowledged = true } }
            };
            store.Data.PendingClaims.Add(new MissionPendingClaim { CatalogId = "batch", MissionId = "a", WindowSnapshot = window, Bundle = bundle, Journal = journal });
            // Revisioned Permanent needs policy agreement. Use the already built model to load this sentinel graph
            // through a periodic fixture instead, avoiding a catalog policy migration.
            Clock clock = new(); Store periodic = new(); MissionModel model = Model(periodic, clock);
            periodic.Data.PendingClaims = store.Data.PendingClaims;
            model = Model(periodic, clock);
            string before = JsonConvert.SerializeObject(periodic.Data.PendingClaims);
            var retained = periodic.Data;
            model.ReportProgressBatch(new[] { E(model, "one") });
            Assert.That(JsonConvert.SerializeObject(periodic.Data.PendingClaims), Is.EqualTo(before));
            Assert.That(periodic.Data.Missions, Is.SameAs(periodic.Data.ActiveWindow.Missions));
            Assert.That(periodic.Data.ProcessedEventIds, Is.SameAs(periodic.Data.ActiveWindow.ProcessedEventIds));
            Assert.That(periodic.Data.ActiveWindow.Definitions[0].Target, Is.EqualTo(1000000));
            Assert.That(periodic.Data.PendingClaims[0].WindowSnapshot.Period.FreeRerolls, Is.EqualTo(7));
            Assert.That(periodic.Data.PendingClaims[0].WindowSnapshot.Selection.PoolId, Is.EqualTo("p"));
            Assert.That(periodic.Data.PendingClaims[0].WindowSnapshot.Selection.ConflictPolicy, Is.EqualTo(MissionConflictPolicy.PreferGroupsThenRelax));
            retained.PendingClaims[0].Journal.Entries[0].Amount = 999;
            retained.PendingClaims[0].WindowSnapshot.Period.FreeRerolls = 999;
            periodic.Data.ActiveWindow.Missions["a"].Progress = 999;
            periodic.Data.ActiveWindow.ProcessedEventIds.Clear();
            periodic.Data.PendingClaims[0].Journal.Entries[0].Acknowledged = false;
            periodic.Data.ActiveWindow.Period.FreeRerolls = 999;
            periodic.Data.ActiveWindow.SelectedIds.Clear();
            periodic.Data.PendingClaims[0].WindowSnapshot.Selection.Count = 999;
            model.ReportProgressBatch(new[] { E(model, "two") });
            Assert.That(model.GetState()[0].Progress, Is.EqualTo(2));
            Assert.That(periodic.Data.ActiveWindow.ProcessedEventIds.Count, Is.EqualTo(2));
            Assert.That(periodic.Data.ActiveWindow.Period.FreeRerolls, Is.EqualTo(1));
            Assert.That(JsonConvert.SerializeObject(periodic.Data.PendingClaims), Is.EqualTo(before));
        }

        [TestCase(4096, 30)] [TestCase(4096, 100)]
        [TestCase(50000, 30)] [TestCase(50000, 100)]
        [Category("MissionBatchBenchmark")]
        public void Benchmark_RealDatasave(int receipts, int shots)
        {
            foreach (bool batched in new[] { false, true })
            {
                string directory = Path.Combine(Path.GetTempPath(), "dreamy-missions-benchmark-" + Guid.NewGuid().ToString("N"));
                var store = new CountingStore(new DatasaveService(new DatasaveOptions { DirectoryName = directory }));
                try
                {
                    var model = new MissionModel(Config(distinct: true), store, new Wallet(), historyLimit: receipts + 1000);
                    var seed = store.Load<MissionSaveData>("missions");
                    seed.ActiveWindow.ProcessedEventIds.AddRange(Enumerable.Range(0, receipts).Select(i => "seed-" + i));
                    store.Save(seed, "missions");
                    model = new MissionModel(Config(distinct: true), store, new Wallet(), historyLimit: receipts + 1000);
                    var inputs = Enumerable.Range(0, shots).Select(i => E(model, "event-" + i, value: "value-" + i)).ToArray();
                    int saves = store.Saves;
                    var timer = Stopwatch.StartNew();
                    if (batched) model.ReportProgressBatch(inputs);
                    else foreach (var e in inputs) model.ReportProgress(e.EventKey, e.OriginalId, e.EventNamespace, e.WindowId, e.Amount, e.DistinctValue);
                    timer.Stop();
                    Assert.That(store.Saves - saves, Is.EqualTo(batched ? 1 : shots));
                    var reloaded = new MissionModel(Config(distinct: true), store, new Wallet(), historyLimit: receipts + 1000);
                    Assert.That(reloaded.GetState().Select(s => s.Progress), Is.All.EqualTo(shots));
                    Assert.That(reloaded.ReportProgressBatch(inputs), Is.All.EqualTo(MissionProgressStatus.AlreadyProcessed));
                    TestContext.WriteLine(FormattableString.Invariant($"receipts={receipts}; shots={shots}; eventsPerShot=1; batch={batched}; ms={timer.Elapsed.TotalMilliseconds:F3}; commits={(batched ? 1 : shots)}; store=real-Datasave; allocation=unsupported-on-Unity-Mono"));
                }
                finally
                {
                    // Delete only this test's unique directory, including Datasave backup/temp files.
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                }
            }
        }

        private sealed class CountingStore : IDatasaveService
        {
            private readonly IDatasaveService inner;
            public CountingStore(IDatasaveService inner) { this.inner = inner; }
            public int Saves;
            public T Load<T>(string key = null) where T : SaveData, new() => inner.Load<T>(key);
            public void Save<T>(T data, string key = null) where T : SaveData { Saves++; inner.Save(data, key); }
            public void SaveAll() => inner.SaveAll();
            public bool Exists(string key) => inner.Exists(key);
            public void Delete(string key) => inner.Delete(key);
            public void DeleteAll() => inner.DeleteAll();
        }

        [TestCase(false, false)] [TestCase(true, false)]
        [TestCase(false, true)] [TestCase(true, true)]
        public void RealDatasaveFault_ReloadsDurableProgressAndOriginalReceipts(bool afterWrite, bool periodic)
        {
            string directory = Path.Combine(Path.GetTempPath(), "dreamy-missions-fault-" + Guid.NewGuid().ToString("N"));
            var codec = new FaultCodec();
            var options = new DatasaveOptions { DirectoryName = directory, Codec = codec };
            var store = new AfterWriteFaultStore(new DatasaveService(options));
            var clock = periodic ? new Clock() : null;
            var config = Config(periodic, distinct: true);
            try
            {
                var model = new MissionModel(config, store, new Wallet(), clock: clock);
                model.ReportProgressBatch(new[] { E(model, "seed", value: "seed") });
                var inputs = new[] { E(model, "one", value: "A"), E(model, "two", value: "B") };
                int notifications = 0;
                model.StateChanged += () => notifications++;
                codec.FailEncode = !afterWrite;
                store.FailAfterWrite = afterWrite;
                Assert.Throws<IOException>(() => model.ReportProgressBatch(inputs));
                Assert.That(model.RequiresReload, Is.True);
                Assert.That(model.GetState().Select(s => s.Progress), Is.All.EqualTo(1));
                Assert.That(notifications, Is.Zero);
                Assert.Throws<MissionReloadRequiredException>(() => model.ReportProgressBatch(inputs));

                // A new concrete service reads the actual envelope from disk, never the failed owner's memory.
                var freshStore = new DatasaveService(new DatasaveOptions { DirectoryName = directory });
                var durable = freshStore.Load<MissionSaveData>("missions");
                Assert.That(durable.ActiveWindow.Missions["a"].Progress, Is.EqualTo(afterWrite ? 3 : 1));
                Assert.That(durable.ActiveWindow.Missions["distinct"].DistinctValues,
                    Is.EqualTo(afterWrite ? new[] { "seed", "A", "B" } : new[] { "seed" }));
                model = new MissionModel(config, freshStore, new Wallet(), clock: clock);
                Assert.That(model.ReportProgressBatch(inputs), Is.All.EqualTo(afterWrite
                    ? MissionProgressStatus.AlreadyProcessed : MissionProgressStatus.Updated));
                model = new MissionModel(config, new DatasaveService(new DatasaveOptions { DirectoryName = directory }), new Wallet(), clock: clock);
                Assert.That(model.GetState().Select(s => s.Progress), Is.All.EqualTo(3));
                Assert.That(model.ReportProgressBatch(inputs), Is.All.EqualTo(MissionProgressStatus.AlreadyProcessed));
                Assert.That(Directory.GetFiles(directory, "*.bak").Length, Is.GreaterThan(0));
                Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        private sealed class FaultCodec : ISaveCodec
        {
            public bool FailEncode;
            public string Encode(string text)
            {
                if (FailEncode) throw new IOException("Injected failure before real Datasave AtomicWrite");
                return text;
            }
            public string Decode(string text) => text;
        }

        private sealed class AfterWriteFaultStore : IDatasaveService
        {
            private readonly IDatasaveService inner;
            public bool FailAfterWrite;
            public AfterWriteFaultStore(IDatasaveService inner) { this.inner = inner; }
            public T Load<T>(string key = null) where T : SaveData, new() => inner.Load<T>(key);
            public void Save<T>(T data, string key = null) where T : SaveData
            {
                inner.Save(data, key);
                if (FailAfterWrite) throw new IOException("Injected acknowledgement failure after real Datasave AtomicWrite");
            }
            public void SaveAll() => inner.SaveAll();
            public bool Exists(string key) => inner.Exists(key);
            public void Delete(string key) => inner.Delete(key);
            public void DeleteAll() => inner.DeleteAll();
        }

        [TestCase(4096, 30)] [TestCase(4096, 60)] [TestCase(4096, 100)]
        [TestCase(50000, 30)] [TestCase(50000, 60)] [TestCase(50000, 100)]
        [Category("MissionBatchBenchmark")]
        public void Benchmark_LargeReceiptJournal(int receipts, int shots)
        {
            // Isolate model allocations from serialization: store retains the detached save argument.
            foreach (bool batched in new[] { false, true })
            {
                Store store = new(); Model(store, limit: receipts + 1000);
                store.Data.ActiveWindow.ProcessedEventIds.AddRange(Enumerable.Range(0, receipts).Select(i => "existing-" + i));
                MissionModel model = Model(store, limit: receipts + 1000);
                var inputs = Enumerable.Range(0, shots).Select(i => E(model, "shot-" + i)).ToArray();
                // Warm up each operation without changing the measured fixture.
                Store warm = new(); var warmModel = Model(warm);
                if (batched) warmModel.ReportProgressBatch(new[] { E(warmModel, "warm") });
                else warmModel.ReportProgress("shot", "warm", "game", warmModel.WindowId);
                int saves = store.Saves, notifications = 0;
                model.StateChanged += () => notifications++;
                var timer = new Stopwatch();
                long start = GC.GetAllocatedBytesForCurrentThread(); timer.Start();
                if (batched) model.ReportProgressBatch(inputs);
                else foreach (var e in inputs) model.ReportProgress(e.EventKey, e.OriginalId, e.EventNamespace, e.WindowId, e.Amount, e.DistinctValue);
                timer.Stop(); long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
                Assert.That(store.Saves - saves, Is.EqualTo(batched ? 1 : shots));
                Assert.That(notifications, Is.EqualTo(batched ? 1 : shots));
                Assert.That(model.GetState()[0].Progress, Is.EqualTo(shots));
                TestContext.WriteLine($"receipts={receipts}; shots={shots}; eventsPerShot=1; batch={batched}; allocatedBytes={(bytes == 0 ? "unsupported-on-this-runtime" : bytes.ToString(System.Globalization.CultureInfo.InvariantCulture))}; ms={timer.Elapsed.TotalMilliseconds:F3}; commits={store.Saves - saves}; notifications={notifications}; store=reference");
            }
        }

        private sealed class Wallet : IResourceWallet
        {
            public bool TryGrant(ResourceGrantRequest request) => true;
            public bool TryExchange(ResourceExchangeRequest request) => false;
        }

        private sealed class Clock : IMissionClock
        {
            public DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
            public int Reads;
            public DateTimeOffset UtcNow { get { Reads++; return Now; } }
        }

        private sealed class Store : IDatasaveService
        {
            public MissionSaveData Data;
            public int Saves;
            public bool FailBefore, FailAfter, FailLoad;
            public Action OnSave;
            public T Load<T>(string key = null) where T : SaveData, new()
            {
                if (FailLoad) throw new InvalidOperationException("load failed");
                return Data == null ? new T() : (T)(SaveData)Data;
            }
            public void Save<T>(T data, string key = null) where T : SaveData
            {
                Saves++; OnSave?.Invoke();
                if (FailBefore) throw new InvalidOperationException("before write");
                Data = (MissionSaveData)(SaveData)data;
                if (FailAfter) throw new InvalidOperationException("after write");
            }
            public void SaveAll() { }
            public bool Exists(string key) => Data != null;
            public void Delete(string key) => Data = null;
            public void DeleteAll() => Data = null;
        }
    }
}
