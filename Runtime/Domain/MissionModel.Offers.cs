using System;
using System.Globalization;
using System.Linq;
using Dreamy.Economy.Rewards;

namespace Dreamy.Missions
{
    public sealed partial class MissionModel
    {
        // Read only: does not advance/reset a window while a popup is querying it.
        public MissionClaimOffer GetClaimOffer(string missionId,string token = null)
        {
            if(gateway == null || string.IsNullOrWhiteSpace(missionId))return null;
            foreach(var pending in save.PendingClaims.Where(p=>p.MissionId == missionId))
            {
                var key=OfferToken(pending.WindowSnapshot,missionId);
                if(token != key && (token != null || pending.Journal.State == RewardJournalState.Completed))continue;
                if(!pending.Journal.IsValid())return null;
                var bundle=pending.Journal.State == RewardJournalState.IntentPersisted ? pending.Bundle : pending.Journal.GetResolvedBundle();
                return new MissionClaimOffer(key,bundle,pending.Journal.State != RewardJournalState.Completed,pending.Journal.State == RewardJournalState.Completed);
            }
            var active=save.ActiveWindow;
            if(active.Period.Period != MissionPeriod.Permanent && catalog.Period.GetWindow(clock.UtcNow).StartUtcTicks > active.StartUtcTicks)return null;
            if(!active.SelectedIds.Contains(missionId) || !active.Missions.TryGetValue(missionId,out var progress))return null;
            var mission=active.Definitions.First(m=>m.Id == missionId);
            if(progress.Claimed || progress.Progress < mission.Target)return null;
            var current=OfferToken(active,missionId);
            if(token != null && token != current)return null;
            return new MissionClaimOffer(current,mission.GetBundle(),false,false);
        }
        public MissionClaimStatus ClaimOffer(string missionId,string token,string expectedFingerprint)
        {
            if(string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(expectedFingerprint))return MissionClaimStatus.UnknownMission;
            return ClaimCore(missionId,token,expectedFingerprint);
        }
        private string OfferToken(MissionWindowData window,string missionId)
        {
            var mission=window.Definitions.First(m=>m.Id == missionId);
            return RewardIdentity.Encode("mission-offer-v1",saveKey,save.CatalogId,window.Id,missionId,
                window.Revision ?? string.Empty,window.RerollsUsed.ToString(CultureInfo.InvariantCulture),mission.GetBundle().Fingerprint);
        }
    }
}
