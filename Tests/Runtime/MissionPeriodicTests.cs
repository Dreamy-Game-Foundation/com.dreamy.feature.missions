using System;
using System.Collections.Generic;
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
    public sealed class MissionPeriodicTests
    {
        [TestCase(MissionPeriod.Daily, 420, 0, DayOfWeek.Monday, "2026-10-11T17:00:00Z", 1)]
        [TestCase(MissionPeriod.Weekly, 420, 0, DayOfWeek.Monday, "2026-10-11T17:00:00Z", 7)]
        [TestCase(MissionPeriod.Weekly, 0, 240, DayOfWeek.Sunday, "2027-01-03T04:00:00Z", 7)]
        [TestCase(MissionPeriod.Daily, 0, 0, DayOfWeek.Monday, "2028-02-29T00:00:00Z", 1)]
        [TestCase(MissionPeriod.Daily, -300, 90, DayOfWeek.Friday, "2027-01-01T06:30:00Z", 1)]
        public void Windows_AreHalfOpenAtConfiguredBoundaries(MissionPeriod period, int offset, int cutoff,
            DayOfWeek weekStart, string boundary, int days)
        {
            var policy = new MissionPeriodPolicy
            { Period = period, OffsetMinutes = offset, CutoffMinutes = cutoff, WeekStart = weekStart };
            DateTimeOffset at = DateTimeOffset.Parse(boundary);
            MissionWindow before = policy.GetWindow(at.AddTicks(-1));
            MissionWindow exact = policy.GetWindow(at);
            MissionWindow after = policy.GetWindow(at.AddTicks(1));
            Assert.That(before.EndUtcTicks, Is.EqualTo(at.UtcDateTime.Ticks));
            Assert.That(exact.StartUtcTicks, Is.EqualTo(at.UtcDateTime.Ticks));
            Assert.That(exact.EndUtcTicks, Is.EqualTo(at.AddDays(days).UtcDateTime.Ticks));
            Assert.That(after.Id, Is.EqualTo(exact.Id));
            Assert.That(before.Id, Is.Not.EqualTo(exact.Id));
        }

        [Test] public void RollbackAndForward_KeepHighWaterAndOpenOnlyCurrentWindow()
        {
            Store store = new(); Clock clock = new(); Gateway gateway = new(store);
            MissionModel model = Model(Config(), store, gateway, clock);
            model.ReportProgress("act", "one", "battle/act", model.WindowId);
            string first = model.WindowId;
            clock.UtcNow = clock.UtcNow.AddDays(-2);
            Assert.That(model.Refresh(), Is.EqualTo(MissionRefreshStatus.Unchanged));
            Assert.That(model.WindowId, Is.EqualTo(first));
            model = Model(Config(), store, new Gateway(store), clock);
            Assert.That(model.WindowId, Is.EqualTo(first));
            clock.UtcNow = clock.UtcNow.AddDays(12);
            Assert.That(model.Refresh(), Is.EqualTo(MissionRefreshStatus.Updated));
            Assert.That(model.GetState().All(s => s.Progress == 0 && !s.IsClaimed), Is.True);
            Assert.That(store.Load<MissionSaveData>("missions").ActiveWindow.StartUtcTicks,
                Is.EqualTo(Config().Period.GetWindow(clock.UtcNow).StartUtcTicks));
            Assert.That(gateway.Total, Is.Zero);
        }

        [Test] public void RolloverSaveFailure_DoesNotPublishNewWindow()
        {
            Store store = new(); Clock clock = new(); MissionModel model = Model(Config(), store, new Gateway(store), clock);
            string original = model.WindowId;
            MissionWindow originalWindow = model.Window;
            int events = 0;
            model.StateChanged += () => events++;
            clock.UtcNow = clock.UtcNow.AddDays(1);
            store.Fail = true;
            Assert.Throws<InvalidOperationException>(() => model.Refresh());
            Assert.That(model.WindowId, Is.EqualTo(original));
            Assert.That(model.Window.Id, Is.EqualTo(originalWindow.Id));
            Assert.That(model.Window.StartUtcTicks, Is.EqualTo(originalWindow.StartUtcTicks));
            Assert.That(model.Window.EndUtcTicks, Is.EqualTo(originalWindow.EndUtcTicks));
            Assert.That(events, Is.Zero);
            store.Fail = false;
            Assert.That(model.Refresh(), Is.EqualTo(MissionRefreshStatus.Updated));
            Assert.That(model.Window.StartUtcTicks, Is.EqualTo(originalWindow.EndUtcTicks));
            Assert.That(events, Is.EqualTo(1));
        }

        [Test] public void RevisionIsDeferred_AndOldDefinitionCanClaimUntilBoundary()
        {
            Store store = new(); Clock clock = new(); MissionCatalogConfig old = Config(revision: "one");
            MissionModel model = Model(old, store, new Gateway(store), clock);
            model.ReportProgress("act", "one", "battle/act", model.WindowId, 3);
            JObject updated = Document(revision: "two");
            updated["missions"] = new JArray(Mission("replacement"));
            model = Model(Parse(updated), store, new Gateway(store), clock);
            Assert.That(model.GetState()[0].Definition.Id, Is.EqualTo("a"));
            Assert.That(model.Claim("a"), Is.EqualTo(MissionClaimStatus.Claimed));
            clock.UtcNow = clock.UtcNow.AddDays(1);
            model.Refresh();
            Assert.That(model.GetState().Select(s => s.Definition.Id), Is.EqualTo(new[] { "replacement" }));
            Assert.That(store.Load<MissionSaveData>("missions").ActiveWindow.Revision, Is.EqualTo("two"));
        }

        [Test] public void PermanentRevision_AddsButRejectsChangedExistingReward()
        {
            Store store = new(); JObject first = Document(revision: "one");
            first.Remove("period");
            MissionModel model = Model(Parse(first), store, new Gateway(store), new Clock());
            model.ReportProgress("act", "one");
            JObject added = (JObject)first.DeepClone();
            added["revision"] = "two";
            ((JArray)added["missions"]).Add(Mission("added"));
            model = Model(Parse(added), store, new Gateway(store), new Clock());
            Assert.That(model.GetState()[0].Progress, Is.EqualTo(1));
            Assert.That(model.GetState().Count, Is.EqualTo(4));
            JObject changed = (JObject)added.DeepClone();
            changed["revision"] = "three";
            changed["missions"][0]["rewardAmount"] = 999;
            Assert.Throws<InvalidOperationException>(() => Model(Parse(changed), store, new Gateway(store), new Clock()));
            Assert.That(store.Load<MissionSaveData>("missions").ActiveWindow.Revision, Is.EqualTo("two"));
        }

        [TestCase(3, 6)] [TestCase(5, 10)]
        public void Selection_IsPersistedAcrossRestart_WithoutDrawingAgain(int count, int size)
        {
            JObject doc = Document();
            doc["missions"] = new JArray(Enumerable.Range(0, size).Select(i => Mission("m" + i)));
            doc["selection"] = Selection(count);
            Store store = new(); Clock clock = new(); RandomSource random = new();
            MissionModel model = Model(Parse(doc), store, new Gateway(store), clock, random);
            string[] ids = model.GetState().Select(s => s.Definition.Id).ToArray();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(count));
            var noDraws = new RandomSource { RejectDraws = true };
            model = Model(Parse(doc), store, new Gateway(store), clock, noDraws);
            Assert.That(model.GetState().Select(s => s.Definition.Id), Is.EqualTo(ids));
            Assert.That(noDraws.Draws, Is.Zero);
        }

        [Test] public void ConflictRelaxation_NeverDuplicatesIds_AndStrictRejectsInsufficiency()
        {
            JObject doc = Document();
            foreach (JObject mission in (JArray)doc["missions"]) mission["conflictGroup"] = "same";
            doc["selection"] = Selection(2);
            Store strict = new();
            Assert.Throws<InvalidOperationException>(() => Model(Parse(doc), strict, new Gateway(strict), new Clock(), new RandomSource()));
            Assert.That(strict.Exists("missions"), Is.False);
            doc["selection"]["conflictPolicy"] = "PreferGroupsThenRelax";
            Store store = new();
            MissionModel model = Model(Parse(doc), store, new Gateway(store), new Clock(), new RandomSource());
            Assert.That(model.GetState().Select(s => s.Definition.Id).Distinct().Count(), Is.EqualTo(2));
            doc["selection"]["count"] = 4;
            Store insufficient = new();
            Assert.Throws<InvalidOperationException>(() => Model(Parse(doc), insufficient, new Gateway(insufficient), new Clock(), new RandomSource()));
        }

        [Test] public void Selection_UsesExplicitPool_AndDoesNotPublishFailedSave()
        {
            JObject doc = Document();
            doc["missions"][0]["poolId"] = "included";
            doc["missions"][1]["poolId"] = "included";
            doc["selection"] = Selection(2);
            doc["selection"]["poolId"] = "included";
            Store store = new() { Fail = true };
            Assert.Throws<InvalidOperationException>(() => Model(Parse(doc), store, new Gateway(store), new Clock(), new RandomSource()));
            Assert.That(store.Exists("missions"), Is.False);
            store.Fail = false;
            MissionModel model = Model(Parse(doc), store, new Gateway(store), new Clock(), new RandomSource());
            Assert.That(model.GetState().Select(s => s.Definition.Id), Is.EquivalentTo(new[] { "a", "b" }));
        }

        [Test] public void Reroll_ExcludesFullSelection_PersistsCountAndResetsAtNextWindow()
        {
            JObject doc = Document();
            doc["selection"] = Selection(2);
            Store store = new(); Clock clock = new(); RandomSource random = new();
            MissionModel model = Model(Parse(doc), store, new Gateway(store), clock, random);
            string[] original = model.GetState().Select(s => s.Definition.Id).ToArray();
            Assert.That(model.Reroll(original[0]), Is.EqualTo(MissionRerollStatus.Rerolled));
            string[] replaced = model.GetState().Select(s => s.Definition.Id).ToArray();
            Assert.That(original.Contains(replaced[0]), Is.False);
            Assert.That(replaced[1], Is.EqualTo(original[1]));
            model = Model(Parse(doc), store, new Gateway(store), clock, random);
            Assert.That(model.GetState().Select(s => s.Definition.Id), Is.EqualTo(replaced));
            Assert.That(model.Reroll(replaced[0]), Is.EqualTo(MissionRerollStatus.LimitReached));
            clock.UtcNow = clock.UtcNow.AddDays(1);
            model.Refresh();
            Assert.That(model.RerollsUsed, Is.Zero);
        }

        [Test] public void Reroll_NoCandidateAndFailedSave_DoNotConsumeCount()
        {
            Store store = new(); Clock clock = new();
            MissionModel full = Model(Config(), store, new Gateway(store), clock);
            Assert.That(full.Reroll("a"), Is.EqualTo(MissionRerollStatus.NoCandidate));
            Assert.That(full.RerollsUsed, Is.Zero);
            JObject doc = Document();
            doc["selection"] = Selection(1);
            store = new Store();
            MissionModel model = Model(Parse(doc), store, new Gateway(store), clock, new RandomSource());
            string original = model.GetState()[0].Definition.Id;
            store.Fail = true;
            Assert.Throws<InvalidOperationException>(() => model.Reroll(original));
            Assert.That(model.GetState()[0].Definition.Id, Is.EqualTo(original));
            Assert.That(model.RerollsUsed, Is.Zero);
            store.Fail = false;
            Assert.That(model.Reroll(original), Is.EqualTo(MissionRerollStatus.Rerolled));
        }

        [Test] public void Reroll_RejectsCompletedClaimedInactiveAndUsesExplicitLimit()
        {
            JObject doc = Document();
            doc["selection"] = Selection(1);
            doc["period"]["freeRerolls"] = 2;
            Store store = new(); Clock clock = new();
            MissionModel model = Model(Parse(doc), store, new Gateway(store), clock, new RandomSource());
            string selected = model.GetState()[0].Definition.Id;
            Assert.That(model.Reroll("missing"), Is.EqualTo(MissionRerollStatus.UnknownMission));
            model.ReportProgress("act", "complete", "battle/act", model.WindowId, 3);
            Assert.That(model.Reroll(selected), Is.EqualTo(MissionRerollStatus.Ineligible));
            model.Claim(selected);
            Assert.That(model.Reroll(selected), Is.EqualTo(MissionRerollStatus.Ineligible));
            clock.UtcNow = clock.UtcNow.AddDays(1);
            model.Refresh();
            Assert.That(model.Reroll(model.GetState()[0].Definition.Id), Is.EqualTo(MissionRerollStatus.Rerolled));
            Assert.That(model.Reroll(model.GetState()[0].Definition.Id), Is.EqualTo(MissionRerollStatus.Rerolled));
            Assert.That(model.Reroll(model.GetState()[0].Definition.Id), Is.EqualTo(MissionRerollStatus.LimitReached));
        }

        [Test] public void DistinctValues_AreSeparateFromBusinessEventReplayAndPersisted()
        {
            JObject doc = Document();
            doc["missions"][0]["objective"] = "DistinctValue";
            Store store = new(); Clock clock = new();
            MissionModel model = Model(Parse(doc), store, new Gateway(store), clock);
            string window = model.WindowId;
            model.ReportProgress("act", "1", "battle/fire", window, distinctValue: "unit-a");
            model.ReportProgress("act", "2", "battle/fire", window, distinctValue: "unit-a");
            model = Model(Parse(doc), store, new Gateway(store), clock);
            Assert.That(model.GetState()[0].Progress, Is.EqualTo(1));
            Assert.That(model.GetState()[1].Progress, Is.EqualTo(2));
            Assert.That(model.ReportProgress("act", "2", "battle/fire", window, distinctValue: "unit-b"),
                Is.EqualTo(MissionProgressStatus.AlreadyProcessed));
            // Same business ordinal under a different authoritative action namespace is a different event.
            model.ReportProgress("act", "2", "battle/won", window, distinctValue: "unit-b");
            Assert.That(model.GetState()[0].Progress, Is.EqualTo(2));
            Assert.That(model.GetState()[1].Progress, Is.EqualTo(3));
            Assert.That(store.Load<MissionSaveData>("missions").ActiveWindow.Missions["a"].DistinctValues,
                Is.EqualTo(new[] { "unit-a", "unit-b" }));
        }

        [Test] public void PeriodicEvents_RequireOriginalWindow_AndAreNotBoundedByLegacyHistoryLimit()
        {
            Store store = new(); Clock clock = new();
            MissionModel model = new(Config(), store, new Gateway(store), historyLimit: 2, clock: clock);
            string old = model.WindowId;
            Assert.That(model.ReportProgress("act", "unscoped"), Is.EqualTo(MissionProgressStatus.InvalidEvent));
            Assert.That(model.ReportProgress("missing", "one", "battle/fire", old), Is.EqualTo(MissionProgressStatus.NoMatchingMission));
            foreach (string id in new[] { "one", "two", "three" })
                model.ReportProgress("act", id, "battle/fire", old);
            Assert.That(model.ReportProgress("act", "one", "battle/fire", old), Is.EqualTo(MissionProgressStatus.AlreadyProcessed));
            clock.UtcNow = clock.UtcNow.AddDays(1);
            Assert.That(model.ReportProgress("act", "one", "battle/fire", old), Is.EqualTo(MissionProgressStatus.WrongWindow));
            Assert.That(model.GetState()[0].Progress, Is.Zero);
            Assert.That(model.ReportProgress("act", "one", "battle/fire", model.WindowId), Is.EqualTo(MissionProgressStatus.Updated));
        }

        [Test] public void OwnerAndStateChangedCallbacks_AreBusy_AndProgressSaveFailureDoesNotConsumeDistinctValue()
        {
            JObject doc = Document();
            doc["missions"][0]["objective"] = "DistinctValue";
            Store store = new(); Clock clock = new();
            MissionModel model = Model(Parse(doc), store, new Gateway(store), clock);
            string window = model.WindowId;
            store.OnMissionSave = () =>
            {
                Assert.That(model.Refresh(), Is.EqualTo(MissionRefreshStatus.Busy));
                Assert.That(model.ReportProgress("act", "nested", "n", window, distinctValue: "unit-b"), Is.EqualTo(MissionProgressStatus.Busy));
            };
            model.StateChanged += () => Assert.That(model.Claim("a"), Is.EqualTo(MissionClaimStatus.Busy));
            store.Fail = true;
            Assert.Throws<InvalidOperationException>(() => model.ReportProgress("act", "one", "n", window, distinctValue: "unit-a"));
            Assert.That(model.GetState()[0].Progress, Is.Zero);
            store.Fail = false;
            model.ReportProgress("act", "one", "n", window, distinctValue: "unit-a");
            Assert.That(model.GetState()[0].Progress, Is.EqualTo(1));
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void StrictClaim_EachJournalSaveFailureRecoversWithoutDuplicateBundleDelivery(int claimSave)
        {
            JObject doc = Document();
            SetBundle((JObject)doc["missions"][0]);
            Store store = new(); Clock clock = new(); Gateway gateway = new(store);
            MissionModel model = Model(Parse(doc), store, gateway, clock);
            model.ReportProgress("act", "complete", "battle/fire", model.WindowId, 3);
            store.FailOnMissionSave = store.MissionSaves + claimSave;
            Assert.That(model.Claim("a"), Is.EqualTo(MissionClaimStatus.Pending));
            store.FailOnMissionSave = 0;
            model = Model(Parse(doc), store, new Gateway(store), clock);
            if (!model.GetState()[0].IsClaimed)
                Assert.That(model.Claim("a"), Is.EqualTo(MissionClaimStatus.Claimed));
            Assert.That(model.GetState()[0].IsClaimed, Is.True);
            Assert.That(new Gateway(store).Total, Is.EqualTo(17));
            MissionPendingClaim pending = store.Load<MissionSaveData>("missions").PendingClaims.Single();
            Assert.That(pending.Journal.State, Is.EqualTo(RewardJournalState.Completed));
            Assert.That(pending.WindowSnapshot.Missions["a"].Claimed, Is.True);
        }

        [Test] public void PendingOldWindow_DoesNotBlockSameMissionNextWindow_AndFinalizesOnlyOriginalSnapshot()
        {
            Store store = new(); Clock clock = new(); Gateway gateway = new(store) { Pending = true };
            MissionModel model = Model(Config(), store, gateway, clock);
            string old = model.WindowId;
            model.ReportProgress("act", "complete", "battle/fire", old, 3);
            Assert.That(model.Claim("a"), Is.EqualTo(MissionClaimStatus.Pending));
            clock.UtcNow = clock.UtcNow.AddDays(1);
            gateway = new Gateway(store) { Pending = true };
            model = Model(Config(), store, gateway, clock);
            Assert.That(model.WindowId, Is.Not.EqualTo(old));
            Assert.That(model.GetState()[0].Progress, Is.Zero);
            gateway.Pending = false;
            gateway.BlockedRequest = store.Load<MissionSaveData>("missions").PendingClaims.Single().Journal.RequestId;
            model.ReportProgress("act", "complete", "battle/fire", model.WindowId, 3);
            Assert.That(model.Claim("a"), Is.EqualTo(MissionClaimStatus.Claimed));
            Assert.That(gateway.Total, Is.EqualTo(10));
            gateway.BlockedRequest = null;
            Assert.That(model.RecoverClaims(), Is.EqualTo(MissionClaimStatus.Claimed));
            Assert.That(gateway.Total, Is.EqualTo(20));
            Assert.That(model.GetState()[0].IsClaimed, Is.True);
            Assert.That(store.Load<MissionSaveData>("missions").PendingClaims.Count, Is.EqualTo(2));
        }

        [Test] public void OldPendingRecovery_DoesNotMarkCurrentUnclaimedMission()
        {
            Store store = new(); Clock clock = new(); Gateway gateway = new(store) { Pending = true };
            MissionModel model = Model(Config(), store, gateway, clock);
            model.ReportProgress("act", "complete", "battle/fire", model.WindowId, 3);
            model.Claim("a");
            clock.UtcNow = clock.UtcNow.AddDays(1);
            model.Refresh();
            gateway.Pending = false;
            Assert.That(model.RecoverClaims(), Is.EqualTo(MissionClaimStatus.Claimed));
            Assert.That(model.GetState()[0].IsClaimed, Is.False);
            Assert.That(model.GetState()[0].Progress, Is.Zero);
        }

        [Test] public void Recovery_AttemptsCurrentPendingClaimEvenWhenOlderJournalIsUnavailable()
        {
            Store store = new(); Clock clock = new(); Gateway gateway = new(store) { Pending = true };
            MissionModel model = Model(Config(), store, gateway, clock);
            model.ReportProgress("act", "complete", "battle/fire", model.WindowId, 3);
            model.Claim("a");
            string oldRequest = store.Load<MissionSaveData>("missions").PendingClaims.Single().Journal.RequestId;
            clock.UtcNow = clock.UtcNow.AddDays(1);
            model.Refresh();
            model.ReportProgress("act", "complete", "battle/fire", model.WindowId, 3);
            Assert.That(model.Claim("a"), Is.EqualTo(MissionClaimStatus.Pending));
            gateway.Pending = false;
            gateway.BlockedRequest = oldRequest;
            Assert.That(model.RecoverClaims(), Is.EqualTo(MissionClaimStatus.Pending));
            Assert.That(model.GetState()[0].IsClaimed, Is.True);
            Assert.That(gateway.Total, Is.EqualTo(10));
            var claims = store.Load<MissionSaveData>("missions").PendingClaims;
            Assert.That(claims[0].Journal.State, Is.EqualTo(RewardJournalState.IntentPersisted));
            Assert.That(claims[1].Journal.State, Is.EqualTo(RewardJournalState.Completed));
        }

        [Test] public void WindowPolicyAndPeriodicIdentityChanges_RequireExplicitMigration()
        {
            Store store = new(); Clock clock = new();
            MissionModel model = Model(Config(), store, new Gateway(store), clock);
            string original = model.WindowId;
            JObject changed = Document();
            changed["period"]["offsetMinutes"] = 0;
            Assert.Throws<InvalidOperationException>(() => Model(Parse(changed), store, new Gateway(store), clock));
            changed = Document();
            changed["catalogId"] = "different";
            Assert.Throws<InvalidOperationException>(() => Model(Parse(changed), store, new Gateway(store), clock));
            changed.Remove("period");
            changed.Remove("revision");
            Assert.Throws<InvalidOperationException>(() => Model(Parse(changed), store, new Gateway(store), clock));
            Assert.That(store.Load<MissionSaveData>("missions").ActiveWindow.Id, Is.EqualTo(original));
        }

        [Test] public void PendingRevisionChange_RetainsOriginalPayloadAndRemovedMissionSnapshot()
        {
            Store store = new(); Clock clock = new(); Gateway gateway = new(store) { Pending = true };
            MissionModel model = Model(Config(revision: "one"), store, gateway, clock);
            model.ReportProgress("act", "complete", "battle/fire", model.WindowId, 3);
            model.Claim("a");
            JObject changed = Document(revision: "two");
            changed["missions"] = new JArray(Mission("other"));
            clock.UtcNow = clock.UtcNow.AddDays(1);
            gateway = new Gateway(store) { Pending = true };
            model = Model(Parse(changed), store, gateway, clock);
            Assert.That(model.GetState()[0].Definition.Id, Is.EqualTo("other"));
            gateway.Pending = false;
            model.RecoverClaims();
            Assert.That(gateway.Total, Is.EqualTo(10));
            MissionPendingClaim pending = store.Load<MissionSaveData>("missions").PendingClaims.Single();
            Assert.That(pending.WindowSnapshot.Revision, Is.EqualTo("one"));
            Assert.That(pending.WindowSnapshot.Missions["a"].Claimed, Is.True);
            Assert.That(model.GetState()[0].IsClaimed, Is.False);
        }

        [Test] public void GatewayReentry_IsBusyThroughoutValidationDeliveryAndFinalEvent()
        {
            Store store = new(); Gateway gateway = new(store); Clock clock = new();
            MissionModel model = Model(Config(), store, gateway, clock);
            model.ReportProgress("act", "complete", "battle/fire", model.WindowId, 3);
            gateway.OnCall = () =>
            {
                Assert.That(model.Claim("a"), Is.EqualTo(MissionClaimStatus.Busy));
                Assert.That(model.RecoverClaims(), Is.EqualTo(MissionClaimStatus.Busy));
                Assert.That(model.Reroll("b"), Is.EqualTo(MissionRerollStatus.Busy));
            };
            model.StateChanged += () => Assert.That(model.Claim("a"), Is.EqualTo(MissionClaimStatus.Busy));
            Assert.That(model.Claim("a"), Is.EqualTo(MissionClaimStatus.Claimed));
        }

        [Test] public void BundleConfig_RoundTrips_RejectsLegacyAmbiguityAndWalletOverload()
        {
            JObject doc = Document();
            SetBundle((JObject)doc["missions"][0]);
            MissionCatalogConfig config = Parse(doc);
            config.Initialize("test");
            MissionCatalogConfig restored = JsonConvert.DeserializeObject<MissionCatalogConfig>(
                JsonConvert.SerializeObject(config, DataConfigJson.Settings), DataConfigJson.Settings);
            restored.Initialize("test");
            Assert.That(restored.Missions[0].RewardBundle.Fingerprint, Is.EqualTo(config.Missions[0].RewardBundle.Fingerprint));
            Assert.Throws<ArgumentException>(() => new MissionModel(config, new Store(), new Wallet(), clock: new Clock()));
            doc["missions"][0]["rewardResourceId"] = "currency.coin";
            doc["missions"][0]["rewardAmount"] = 1;
            Assert.Throws<DataConfigException>(() => Parse(doc).Initialize("test"));
        }

        [Test] public void LegacySaveAndClaimIdentity_ArePreserved()
        {
            JObject doc = Document();
            doc.Remove("period");
            doc.Remove("revision");
            Store store = new();
            store.Save(new MissionSaveData
            {
                CatalogId = "test",
                Missions = new Dictionary<string, MissionProgressData> { ["a"] = new() { Progress = 3 } },
                ProcessedEventIds = new List<string> { "one" }
            }, "missions");
            Wallet wallet = new(); MissionModel model = new(Parse(doc), store, wallet);
            Assert.That(model.GetState().Count, Is.EqualTo(3));
            Assert.That(model.ReportProgress("act", "one"), Is.EqualTo(MissionProgressStatus.AlreadyProcessed));
            Assert.That(model.Claim("a"), Is.EqualTo(MissionClaimStatus.Claimed));
            Assert.That(wallet.Transaction, Is.EqualTo("mission:4:test:1:a:claim"));
        }

        private static MissionModel Model(MissionCatalogConfig config, Store store, Gateway gateway, Clock clock, RandomSource random = null) =>
            new(config, store, gateway, clock: clock, random: random);
        private static MissionCatalogConfig Config(string revision = "one") => Parse(Document(revision));
        private static MissionCatalogConfig Parse(JObject doc) =>
            JsonConvert.DeserializeObject<MissionCatalogConfig>(doc.ToString(), DataConfigJson.Settings);
        private static JObject Document(string revision = "one") => new()
        {
            ["catalogId"] = "test", ["revision"] = revision,
            ["period"] = new JObject { ["period"] = "Daily", ["offsetMinutes"] = 420, ["weekStart"] = "Monday" },
            ["missions"] = new JArray(Mission("a"), Mission("b"), Mission("c"))
        };
        private static JObject Mission(string id) => new()
        {
            ["id"] = id, ["titleKey"] = id, ["eventKey"] = "act", ["target"] = 3,
            ["rewardResourceId"] = "currency.coin", ["rewardAmount"] = 10
        };
        private static JObject Selection(int count) => new() { ["count"] = count, ["conflictPolicy"] = "Strict" };
        private static void SetBundle(JObject mission)
        {
            mission.Remove("rewardResourceId");
            mission.Remove("rewardAmount");
            mission["rewardBundle"] = JObject.FromObject(new ResolvedRewardBundle(1, "fixed-test-v1", new[]
            {
                new ResolvedRewardEntry("coin", "currency", "currency.coin", 10),
                new ResolvedRewardEntry("item", "item", "item.fixture", 7)
            }));
        }

        private sealed class Clock : IMissionClock
        { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-10-14T10:00:00Z"); }
        private sealed class RandomSource : IMissionRandom
        {
            public int Draws;
            public bool RejectDraws;
            public int Next(int exclusiveMaximum)
            {
                if (RejectDraws) throw new InvalidOperationException("Unexpected random draw.");
                Draws++;
                return 0;
            }
        }
        private sealed class Wallet : IResourceWallet
        {
            public string Transaction;
            public bool TryGrant(ResourceGrantRequest request) { Transaction = request.TransactionId; return true; }
            public bool TryExchange(ResourceExchangeRequest request) => false;
        }
        private sealed class Store : IDatasaveService
        {
            private readonly Dictionary<string, string> documents = new();
            public bool Fail;
            public int MissionSaves;
            public int FailOnMissionSave;
            public Action OnMissionSave;
            public T Load<T>(string key = null) where T : SaveData, new() =>
                documents.TryGetValue(key, out string json) ? JsonConvert.DeserializeObject<T>(json) : new T();
            public void Save<T>(T value, string key = null) where T : SaveData
            {
                if (key == "missions")
                {
                    MissionSaves++;
                    OnMissionSave?.Invoke();
                    if (Fail || MissionSaves == FailOnMissionSave) throw new InvalidOperationException("Save failure");
                }
                documents[key] = JsonConvert.SerializeObject(value);
            }
            public void SaveAll() { }
            public bool Exists(string key) => documents.ContainsKey(key);
            public void Delete(string key) => documents.Remove(key);
            public void DeleteAll() => documents.Clear();
        }
        private sealed class OwnerSave : SaveData
        {
            public long Total;
            public Dictionary<string, string> Receipts = new();
        }
        // Concrete test owner commits receipts and effects together, and is recreated for crash recovery.
        private sealed class Gateway : IRewardDeliveryGateway
        {
            private readonly Store store;
            public bool Pending;
            public string BlockedRequest;
            public Action OnCall;
            public Gateway(Store store) { this.store = store; }
            public long Total => store.Load<OwnerSave>("owner").Total;
            public RewardDeliveryResult Validate(string requestId, string requestFingerprint, ResolvedRewardBundle bundle)
            {
                OnCall?.Invoke();
                if (Pending || requestId == BlockedRequest)
                    return RewardDeliveryResult.Pending(RewardDeliveryReason.DestinationUnavailable);
                OwnerSave data = store.Load<OwnerSave>("owner");
                foreach (ResolvedRewardEntry entry in bundle.Entries)
                {
                    RewardDeliveryRequest request = new(RewardIdentity.EntryTransaction(requestId, entry.EntryId),
                        requestFingerprint, bundle.Fingerprint, entry);
                    if (data.Receipts.TryGetValue(request.TransactionId, out string fingerprint) && fingerprint != request.Fingerprint)
                        return RewardDeliveryResult.Failed(RewardDeliveryReason.PayloadMismatch);
                }
                return RewardDeliveryResult.Delivered();
            }
            public RewardDeliveryResult Deliver(RewardDeliveryRequest request)
            {
                OnCall?.Invoke();
                OwnerSave data = store.Load<OwnerSave>("owner");
                if (data.Receipts.TryGetValue(request.TransactionId, out string fingerprint))
                    return fingerprint == request.Fingerprint ? RewardDeliveryResult.AlreadyDelivered() :
                        RewardDeliveryResult.Failed(RewardDeliveryReason.PayloadMismatch);
                data.Total = checked(data.Total + request.Entry.Amount);
                data.Receipts.Add(request.TransactionId, request.Fingerprint);
                store.Save(data, "owner");
                return RewardDeliveryResult.Delivered();
            }
        }
    }
}
