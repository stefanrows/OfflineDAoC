using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

public static partial class AutonomousRvrEventLayer
{
    // Reservations direct one force to each free keep. They never grant
    // ownership: the bot still has to reach and use the native steward.
    private static readonly Dictionary<string, (string Force, long Until)> ClaimForces = new(StringComparer.Ordinal);
    private const long ClaimLeaseMilliseconds = 10 * 60_000;

    private static Plan ChooseClaimPlan(Force force, IReadOnlyCollection<LiveObjective> objectives, long now)
    {
        foreach (string id in ClaimForces.Where(pair => pair.Value.Until <= now ||
                     objectives.Any(target => target.Id == pair.Key && !target.AwaitingClaim))
                     .Select(pair => pair.Key).ToArray())
            ClaimForces.Remove(id);
        if (string.IsNullOrWhiteSpace(force.GuildName) || !force.SingleGuild || !force.CanClaim) return null;
        LiveObjective[] free = objectives.Where(target => target.AwaitingClaim && target.Claimable &&
            !target.IsRelicKeep && !target.IsPortalKeep && string.IsNullOrEmpty(target.OwningGuild) &&
            !IsAbandonedLocked(force.GroupId, target.Id, now) && !KeepRouteBlockedLocked(target.Id, now)).ToArray();
        LiveObjective keep = free.FirstOrDefault(target => ClaimForces.TryGetValue(target.Id, out var lease) &&
            lease.Force == force.GroupId) ?? free.FirstOrDefault(target => !ClaimForces.ContainsKey(target.Id));
        if (keep == null) return null;
        foreach (string id in ClaimForces.Where(pair => pair.Value.Force == force.GroupId && pair.Key != keep.Id)
                     .Select(pair => pair.Key).ToArray())
            ClaimForces.Remove(id);
        ClaimForces[keep.Id] = (force.GroupId, now + ClaimLeaseMilliseconds);
        return ToPlan(Intent.ClaimKeep, keep, false, "Secure a defeated, unclaimed keep for the guild.");
    }

    public static bool RenewClaimPlan(string forceId, string targetId, long now)
    {
        using (EnterSync())
        {
            if (!ClaimForces.TryGetValue(targetId, out var lease) || lease.Force != forceId ||
                IsAbandonedLocked(forceId, targetId, now)) return false;
            ClaimForces[targetId] = (forceId, now + ClaimLeaseMilliseconds);
            return true;
        }
    }

    public static void ReleaseClaimPlan(string forceId, string targetId)
    {
        if (targetId == null) return;
        using (EnterSync())
            if (ClaimForces.TryGetValue(targetId, out var lease) && lease.Force == forceId)
                ClaimForces.Remove(targetId);
    }
}
