using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Dreamy.Economy.UI;

namespace Dreamy.Missions.Presentation
{
    // One adapter is bound to one mission; original window/selection identity is in the token.
    public sealed class MissionRewardClaimSource : IRewardClaimSource
    {
        private readonly string scope,missionId;
        private readonly IMissionClaimOffers offers;
        private bool requiresReload;
        public MissionRewardClaimSource(string scope,string missionId,IMissionClaimOffers offers)
        { this.scope=scope ?? throw new ArgumentNullException(nameof(scope));this.missionId=missionId ?? throw new ArgumentNullException(nameof(missionId));this.offers=offers ?? throw new ArgumentNullException(nameof(offers)); }
        public RewardClaimReference CurrentReference()
        { var offer=offers.GetClaimOffer(missionId);return offer == null ? null : new RewardClaimReference(scope,"missions",offer.Token); }
        private RewardClaimSnapshot Query(RewardClaimReference reference)
        {
            if(reference.Scope != scope || reference.SourceId != "missions")return new RewardClaimSnapshot(reference,"missing",null,RewardClaimStatus.Unavailable,"WrongScope");
            var offer=offers.GetClaimOffer(missionId,reference.ClaimId);
            return offer == null ? new RewardClaimSnapshot(reference,"missing",null,RewardClaimStatus.Expired,"OriginalClaimUnavailable") :
                new RewardClaimSnapshot(reference,offer.Fingerprint,offer.Bundle,requiresReload ? RewardClaimStatus.RequiresReload : offer.IsCompleted ? RewardClaimStatus.AlreadyClaimed : offer.IsPending ? RewardClaimStatus.Pending : RewardClaimStatus.Available,
                    requiresReload ? "Reconstruct source and affected owners from durable state" : null,offer.IsPending && !requiresReload);
        }
        public UniTask<RewardClaimSnapshot> QueryAsync(RewardClaimReference reference,CancellationToken token)
        { token.ThrowIfCancellationRequested();return UniTask.FromResult(Query(reference)); }
        public UniTask<RewardClaimSnapshot> ClaimAsync(RewardClaimCommand command)
        {
            var before=Query(command.Reference);
            if(before.Fingerprint != command.ExpectedFingerprint)return UniTask.FromResult(new RewardClaimSnapshot(command.Reference,command.ExpectedFingerprint,before.Bundle,RewardClaimStatus.Blocked,"PayloadMismatch"));
            if(before.IsCompleted || requiresReload || before.Status == RewardClaimStatus.Expired || before.Status == RewardClaimStatus.Unavailable)return UniTask.FromResult(before);
            MissionClaimStatus status;
            try { status=offers.ClaimOffer(missionId,command.Reference.ClaimId,command.ExpectedFingerprint); }
            catch { requiresReload=true;throw; }
            var after=Query(command.Reference);
            if(status == MissionClaimStatus.Claimed && after.IsCompleted)return UniTask.FromResult(new RewardClaimSnapshot(command.Reference,before.Fingerprint,after.Bundle,RewardClaimStatus.Claimed));
            if(after.IsCompleted)return UniTask.FromResult(after);
            // Missions exposes no reliable per-result persistence-uncertain discriminator here.
            // Stop this adapter on non-success except Busy; host reloads/reconstructs before resuming.
            if(status != MissionClaimStatus.Busy)requiresReload=true;
            return UniTask.FromResult(new RewardClaimSnapshot(command.Reference,before.Fingerprint,before.Bundle,
                requiresReload ? RewardClaimStatus.RequiresReload : RewardClaimStatus.Busy,status.ToString(),!requiresReload));
        }
    }
}
