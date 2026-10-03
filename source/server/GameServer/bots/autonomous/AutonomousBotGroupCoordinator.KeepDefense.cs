using System.Linq;

namespace DOL.GS;

public static partial class AutonomousBotGroupCoordinator
{
    // Runs in population maintenance, before parallel brains. Keep a same-guild
    // party intact for healing and transport; retire its former activity session.
    public static bool RecallForKeepDefense(GameBot bot)
    {
        using (EnterSync())
        {
            AutonomousRealmRaid.CancelPendingForKeepDefense(bot);
            Group group = bot.Group;
            if (group == null) return true;
            var members = group.GetMembersInTheGroup();
            if (members.All(member => member is GameBot other &&
                    AutonomousGuildKeepDefense.Eligible(other) && other.Guild == bot.Guild))
            {
                OnDisbanding(group);
                return true;
            }
            // A mixed-guild raid loses only the recalled member. Native group
            // removal updates the remaining party's leader/roles normally.
            AutonomousRealmRaid.WithdrawForKeepDefense(group, bot);
            TransferringMembers.Add(MemberKey(bot));
            try { group.RemoveMember(bot, retainSingleRemainingMember: true); }
            finally { TransferringMembers.Remove(MemberKey(bot)); }
            return !Sessions.ContainsKey(group);
        }
    }
}
