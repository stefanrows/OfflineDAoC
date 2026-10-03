using System.Linq;

namespace DOL.GS;

public static partial class AutonomousRvrEventLayer
{
    public static void WithdrawForKeepDefense(string forceId, long memberId, bool retireForce)
    {
        using (EnterSync())
        {
            if (retireForce)
            {
                RemoveForce(forceId);
                ReleasedForces.Remove(forceId);
                foreach (string target in ClaimForces.Where(pair => pair.Value.Force == forceId)
                             .Select(pair => pair.Key).ToArray())
                    ClaimForces.Remove(target);
                return;
            }
            // Other guild members keep the force's event and muster. Remove
            // only the recalled member's physical attendance and contribution.
            foreach (var active in Events.Values)
            {
                active.Present.Remove(memberId);
                active.Travel.Remove(memberId);
                active.RegionEntryCredited.Remove(memberId);
                var side = BucketOf(active, forceId);
                if (side != null && side[forceId] > 0) side[forceId]--;
            }
            foreach (var carrier in CarrierEvents.Values)
                foreach (var side in carrier.Participants.Values)
                    if (side.TryGetValue(forceId, out int count) && count > 0) side[forceId] = count - 1;
        }
    }
}
