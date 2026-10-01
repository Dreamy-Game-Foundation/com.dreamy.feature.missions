using System;
using System.Collections.Generic;
using System.Linq;
using Dreamy.DataConfig;
using Dreamy.Datasave;
using Dreamy.Economy;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Dreamy.Missions.Tests
{
    public sealed class MissionModelTests
    {
        private const string Json = "{\"catalogId\":\"test\",\"missions\":[{\"id\":\"wins\",\"titleKey\":\"Win\",\"eventKey\":\"win\",\"target\":3,\"rewardResourceId\":\"currency.coin\",\"rewardAmount\":10},{\"id\":\"wins-long\",\"titleKey\":\"Win more\",\"eventKey\":\"win\",\"target\":10,\"rewardResourceId\":\"currency.coin\",\"rewardAmount\":20}]}";
        private static MissionCatalogConfig Catalog(string json = Json) => JsonConvert.DeserializeObject<MissionCatalogConfig>(json);

        [Test] public void Progress_UpdatesAllMatchingMissions_AndCapsWithoutOverflow()
        {
            MissionModel model = new(Catalog(), new Store(), new Wallet());
            Assert.That(model.ReportProgress("win", "event", long.MaxValue), Is.EqualTo(MissionProgressStatus.Updated));
            Assert.That(model.GetState().Select(s => s.Progress), Is.EqualTo(new long[] { 3, 10 }));
            model.ReportProgress("win", "another", long.MaxValue);
            Assert.That(model.GetState().Select(s => s.Progress), Is.EqualTo(new long[] { 3, 10 }));
        }

        [Test] public void DuplicateEvent_RemainsRejectedAfterReload()
        {
            Store store = new(); Wallet wallet = new();
            new MissionModel(Catalog(), store, wallet).ReportProgress("win", "event");
            MissionModel model = new(Catalog(), store, wallet);
            Assert.That(model.ReportProgress("win", "event"), Is.EqualTo(MissionProgressStatus.AlreadyProcessed));
            Assert.That(model.GetState()[0].Progress, Is.EqualTo(1));
        }

        [Test] public void UnknownKey_DoesNotConsumeEventId()
        {
            MissionModel model = new(Catalog(), new Store(), new Wallet());
            Assert.That(model.ReportProgress("missing", "event"), Is.EqualTo(MissionProgressStatus.NoMatchingMission));
            Assert.That(model.ReportProgress("win", "event"), Is.EqualTo(MissionProgressStatus.Updated));
        }

        [TestCase(0)] [TestCase(-1)] public void InvalidAmount_DoesNotConsumeEvent(long amount)
        {
            MissionModel model = new(Catalog(), new Store(), new Wallet());
            Assert.That(model.ReportProgress("win", "event", amount), Is.EqualTo(MissionProgressStatus.InvalidEvent));
            Assert.That(model.ReportProgress("win", "event"), Is.EqualTo(MissionProgressStatus.Updated));
        }

        [Test] public void Claim_RequiresCompletion_AndPersistsClaimedState()
        {
            Store store = new(); Wallet wallet = new(); MissionModel model = new(Catalog(), store, wallet);
            Assert.That(model.Claim("missing"), Is.EqualTo(MissionClaimStatus.UnknownMission));
            Assert.That(model.Claim("wins"), Is.EqualTo(MissionClaimStatus.NotComplete));
            model.ReportProgress("win", "event", 3);
            Assert.That(model.Claim("wins"), Is.EqualTo(MissionClaimStatus.Claimed));
            Assert.That(new MissionModel(Catalog(), store, wallet).Claim("wins"), Is.EqualTo(MissionClaimStatus.AlreadyClaimed));
            Assert.That(wallet.Grants, Is.EqualTo(1));
        }

        [Test] public void WalletFailure_LeavesRewardClaimable()
        {
            Wallet wallet = new() { Reject = true }; MissionModel model = new(Catalog(), new Store(), wallet);
            model.ReportProgress("win", "event", 3);
            Assert.That(model.Claim("wins"), Is.EqualTo(MissionClaimStatus.GrantFailed));
            Assert.That(model.GetState()[0].CanClaim, Is.True);
            wallet.Reject = false;
            Assert.That(model.Claim("wins"), Is.EqualTo(MissionClaimStatus.Claimed));
        }

        [Test] public void SaveFailureAfterGrant_RetryUsesSameTransaction_AndGrantsOnce()
        {
            Store store = new(); Wallet wallet = new(); MissionModel model = new(Catalog(), store, wallet);
            model.ReportProgress("win", "event", 3);
            store.ThrowOnSave = true;
            Assert.Throws<InvalidOperationException>(() => model.Claim("wins"));
            Assert.That(model.GetState()[0].CanClaim, Is.True);
            store.ThrowOnSave = false;
            model = new MissionModel(Catalog(), store, wallet);
            Assert.That(model.Claim("wins"), Is.EqualTo(MissionClaimStatus.Claimed));
            Assert.That(wallet.Grants, Is.EqualTo(1));
        }

        [Test] public void SaveFailureDuringProgress_DoesNotMutateMemoryOrConsumeEvent()
        {
            Store store = new(); MissionModel model = new(Catalog(), store, new Wallet());
            store.ThrowOnSave = true;
            Assert.Throws<InvalidOperationException>(() => model.ReportProgress("win", "event"));
            Assert.That(model.GetState()[0].Progress, Is.Zero);
            store.ThrowOnSave = false;
            Assert.That(model.ReportProgress("win", "event"), Is.EqualTo(MissionProgressStatus.Updated));
        }

        [Test] public void WalletReentry_IsRejected()
        {
            Wallet wallet = new(); MissionModel model = new(Catalog(), new Store(), wallet);
            model.ReportProgress("win", "event", 3);
            wallet.OnGrant = () => Assert.That(model.Claim("wins"), Is.EqualTo(MissionClaimStatus.Busy));
            Assert.That(model.Claim("wins"), Is.EqualTo(MissionClaimStatus.Claimed));
        }

        [Test] public void HistoryLimit_EvictsOldEventsWithExplicitBoundedSemantics()
        {
            Store store = new(); MissionModel model = new(Catalog(), store, new Wallet(), historyLimit: 2);
            foreach (string id in new[] { "one", "two", "three" }) model.ReportProgress("win", id);
            Assert.That(store.Load<MissionSaveData>("missions").ProcessedEventIds, Is.EqualTo(new[] { "two", "three" }));
            Assert.That(model.ReportProgress("win", "one"), Is.EqualTo(MissionProgressStatus.Updated));
        }

        [Test] public void CatalogChange_ResetsProgressIntentionally()
        {
            Store store = new(); Wallet wallet = new(); new MissionModel(Catalog(), store, wallet).ReportProgress("win", "event");
            MissionModel model = new(Catalog(Json.Replace("\"test\"", "\"new\"")), store, wallet);
            Assert.That(model.GetState()[0].Progress, Is.Zero);
            Assert.That(model.ReportProgress("win", "event"), Is.EqualTo(MissionProgressStatus.Updated));
        }

        [TestCase("\"target\":3", "\"target\":0")]
        [TestCase("\"id\":\"wins-long\"", "\"id\":\"wins\"")]
        [TestCase("\"eventKey\":\"win\"", "\"eventKey\":\"\"")]
        [TestCase("\"rewardAmount\":10", "\"rewardAmount\":-1")]
        public void InvalidCatalog_IsRejected(string source, string replacement) =>
            Assert.Throws<DataConfigException>(() => Catalog(Json.Replace(source, replacement)).Initialize("test"));

        private sealed class Wallet : IResourceWallet
        {
            private readonly HashSet<string> transactions = new();
            public bool Reject; public int Grants; public Action OnGrant;
            public bool TryGrant(ResourceGrantRequest request)
            {
                OnGrant?.Invoke();
                if (Reject) return false;
                if (transactions.Add(request.TransactionId)) Grants++;
                return true;
            }
            public bool TryExchange(ResourceExchangeRequest request) => false;
        }
        private sealed class Store : IDatasaveService
        {
            private readonly Dictionary<string, string> data = new();
            public bool ThrowOnSave;
            public T Load<T>(string key = null) where T : SaveData, new() =>
                data.TryGetValue(key, out string json) ? JsonConvert.DeserializeObject<T>(json) : new T();
            public void Save<T>(T value, string key = null) where T : SaveData
            {
                if (ThrowOnSave) throw new InvalidOperationException("Simulated persistence failure");
                data[key] = JsonConvert.SerializeObject(value);
            }
            public void SaveAll() { }
            public bool Exists(string key) => data.ContainsKey(key);
            public void Delete(string key) => data.Remove(key);
            public void DeleteAll() => data.Clear();
        }
    }
}
