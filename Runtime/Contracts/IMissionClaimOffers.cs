using Dreamy.Economy.Rewards;

namespace Dreamy.Missions
{
    public interface IMissionClaimOffers
    {
        MissionClaimOffer GetClaimOffer(string missionId,string token = null);
        MissionClaimStatus ClaimOffer(string missionId,string token,string expectedFingerprint);
    }
    public sealed class MissionClaimOffer
    {
        internal MissionClaimOffer(string token,ResolvedRewardBundle bundle,bool pending,bool completed)
        { Token=token;Bundle=bundle;IsPending=pending;IsCompleted=completed;Fingerprint=RewardIdentity.Fingerprint(token,bundle.Fingerprint); }
        public string Token { get; }
        public string Fingerprint { get; }
        public ResolvedRewardBundle Bundle { get; }
        public bool IsPending { get; }
        public bool IsCompleted { get; }
    }
}
