using System.Collections.Generic;
using DOL.AI.Brain;
using DOL.GS.ServerRules;

namespace DOL.GS
{
    /// <summary>
    /// A realm raid recruits about fifteen eight-person parties of mixed realms
    /// and guilds. PvpCombatant.AreAllied only knows the same group, guild or
    /// battlegroup, so without this the parties of one raid killed each other in
    /// the full-PvP world. Every raid owns one BattleGroup; its bots carry it in
    /// the same temp property a human battlegroup member carries. BattleGroup's
    /// own roster API is player-only, so the property is set directly and only
    /// ever removed again while it still points at this raid's BattleGroup.
    /// (Bug 74.)
    /// </summary>
    public static class RealmRaidBattleGroup
    {
        /// <summary>Puts every living in the raid's battlegroup. Returns how many changed.</summary>
        public static int Attach(BattleGroup battleGroup, IEnumerable<GameLiving> members)
        {
            if (battleGroup == null || members == null) return 0;
            int changed = 0;
            foreach (GameLiving member in members)
            {
                if (member == null || ReferenceEquals(member.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY), battleGroup)) continue;
                member.TempProperties.SetProperty(BattleGroup.BATTLEGROUP_PROPERTY, battleGroup);
                changed++;
            }
            return changed;
        }

        /// <summary>Takes livings out of the raid's battlegroup; another battlegroup they joined since is left alone.</summary>
        public static int Detach(BattleGroup battleGroup, IEnumerable<GameLiving> members)
        {
            if (battleGroup == null || members == null) return 0;
            int changed = 0;
            foreach (GameLiving member in members)
            {
                if (member == null || !ReferenceEquals(member.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY), battleGroup)) continue;
                member.TempProperties.RemoveProperty(BattleGroup.BATTLEGROUP_PROPERTY);
                changed++;
            }
            return changed;
        }

        /// <summary>
        /// Bots that were already fighting a member of another raid party stop
        /// now: target selection re-checks AreAllied on its own and the threat
        /// list drops allies on its next clean-up, but a swing already aimed at
        /// one would otherwise run on.
        /// </summary>
        public static void DropFightsAgainstAllies(IEnumerable<GameBot> members)
        {
            if (members == null) return;
            foreach (GameBot bot in members)
            {
                if (bot == null) continue;
                if (bot.TargetObject is GameLiving target && !ReferenceEquals(target, bot) && PvpCombatant.AreAllied(bot, target))
                {
                    (bot.Brain as BotBrain)?.RemoveFromAggroList(target);
                    bot.StopAttack();
                    bot.TargetObject = null;
                }
            }
        }
    }
}
