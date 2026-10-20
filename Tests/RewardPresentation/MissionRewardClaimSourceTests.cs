using System;
using System.IO;
using System.Threading;
using Dreamy.DataConfig;
using Dreamy.Datasave;
using Dreamy.Economy;
using Dreamy.Economy.Rewards;
using Dreamy.Economy.UI;
using Dreamy.Missions.Presentation;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Dreamy.Missions.Tests
{
    public sealed class MissionRewardClaimSourceTests
    {
        private sealed class Clock : IMissionClock { public DateTimeOffset UtcNow { get; set; } = new(2026,10,20,12,0,0,TimeSpan.Zero); }
        private string path;
        private DatasaveResourceWallet wallet;
        private MissionRewardClaimSource source;
        private Clock clock;
        [SetUp] public void Setup()
        {
            path=Path.Combine(Path.GetTempPath(),"DreamyMissionPresentation-"+Guid.NewGuid().ToString("N"));clock=new Clock();
            var save=new DatasaveService(new DatasaveOptions{DirectoryName=path,CreateFileOnFirstLoad=false});wallet=new DatasaveResourceWallet(save);
            var config=JsonConvert.DeserializeObject<MissionCatalogConfig>("{\"catalogId\":\"missions\",\"period\":{\"period\":\"Daily\"},\"missions\":[{\"id\":\"win\",\"titleKey\":\"Win\",\"eventKey\":\"win\",\"target\":1,\"rewardResourceId\":\"currency.coin\",\"rewardAmount\":10}]}",DataConfigJson.Settings);
            var model=new MissionModel(config,save,new RewardDeliveryGateway(new IRewardDestination[]{wallet}),clock:clock);
            model.ReportProgress("win","run","game",model.WindowId);
            source=new MissionRewardClaimSource("profile","win",model);
        }
        [TearDown] public void Cleanup(){if(Directory.Exists(path))Directory.Delete(path,true);}
        [Test] public void PreviewThenClaimThenRepeatCreditsOnce()
        {
            var id=source.CurrentReference();var preview=source.QueryAsync(id,CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(0,wallet.GetBalance(new ResourceId("currency.coin")));
            var command=new RewardClaimCommand(id,preview.Fingerprint);
            Assert.AreEqual(RewardClaimStatus.Claimed,source.ClaimAsync(command).GetAwaiter().GetResult().Status);
            Assert.AreEqual(RewardClaimStatus.AlreadyClaimed,source.ClaimAsync(command).GetAwaiter().GetResult().Status);
            Assert.AreEqual(10,wallet.GetBalance(new ResourceId("currency.coin")));
        }
        [Test] public void OpenAcrossWindowCannotGrantTheNewMission()
        {
            var id=source.CurrentReference();var preview=source.QueryAsync(id,CancellationToken.None).GetAwaiter().GetResult();clock.UtcNow=clock.UtcNow.AddDays(1);
            Assert.AreEqual(RewardClaimStatus.Expired,source.QueryAsync(id,CancellationToken.None).GetAwaiter().GetResult().Status);
            Assert.IsFalse(source.ClaimAsync(new RewardClaimCommand(id,preview.Fingerprint)).GetAwaiter().GetResult().IsCompleted);
            Assert.AreEqual(0,wallet.GetBalance(new ResourceId("currency.coin")));
        }
    }
}
