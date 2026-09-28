using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    /// <summary>
    /// Owner-aware generalisation of "in the same Group as its leader" for companion
    /// squads (task 44). A squad member's own Group is its squad's, never the owner's,
    /// so every player-led combat gate that only compared Group equality left every
    /// squad silent. These helpers treat a live squad member exactly like an ordinary
    /// companion in the owner's own group for assist, defense, heal and rez purposes.
    /// Never alters autonomous world bots or temporary /spawn helpers, neither of
    /// which can ever be squad members.
    /// </summary>
    public static class CompanionSquads
    {
        public static bool IsActiveSquadMember(GameBot bot) =>
            bot?.IsPersistentPlayerCompanion == true && bot.PlayerCompanionRecord?.SquadIndex > 0;

        /// <summary>True if <paramref name="bot"/> currently fights for <paramref name="player"/>'s
        /// force: his own live Group, or one of his companion squads.</summary>
        public static bool SharesOwnerForce(GameBot bot, GamePlayer player) =>
            bot != null && player != null && bot.Group != null && bot.Group.IsInTheGroup(bot) &&
            (bot.Group == player.Group && bot.Group.IsInTheGroup(player) ||
             bot.Owner == player && IsActiveSquadMember(bot));

        /// <summary>True if <paramref name="living"/> is <paramref name="owner"/> himself, or a
        /// GameBot fighting for his force (own group or a squad).</summary>
        public static bool IsOwnerForceMember(GameLiving living, GamePlayer owner) =>
            owner != null && (living == owner || (living is GameBot bot && SharesOwnerForce(bot, owner)));

        /// <summary>True if <paramref name="bot"/> and <paramref name="other"/> both fight for
        /// the same owner's force: the owner himself, his own group, and every one of his
        /// squads. Mirrors the existing "sisterParty" pattern used for autonomous raid
        /// defense, but for one owner's companions instead of an autonomous expedition.</summary>
        public static bool ShareOwnerForce(GameBot bot, GameLiving other) =>
            bot?.Owner != null && SharesOwnerForce(bot, bot.Owner) && IsOwnerForceMember(other, bot.Owner);

        /// <summary>Every live GameBot across <paramref name="owner"/>'s own group and every one
        /// of his active companion squads. The player-led broadcasts that used to scan only
        /// the owner's own Group (pull/engage/PvP focus) now reach his whole force.</summary>
        public static IEnumerable<GameBot> OwnerForceBots(GamePlayer owner)
        {
            if (owner == null)
                yield break;

            if (owner.Group != null)
                foreach (GameLiving member in owner.Group.GetMembersInTheGroup())
                    if (member is GameBot bot)
                        yield return bot;

            for (int squadIndex = 1; squadIndex <= CompanionSquadFormation.MaxSquadCount; squadIndex++)
            {
                Group squad = PlayerCompanionRoster.GetLiveSquadGroup(owner, squadIndex);
                if (squad == null)
                    continue;

                foreach (GameLiving member in squad.GetMembersInTheGroup())
                    if (member is GameBot bot)
                        yield return bot;
            }
        }

        private sealed class RezState { public readonly CompanionRaidResurrectionReservations<GameLiving> Reservations = new(); }
        private static readonly ConditionalWeakTable<GamePlayer, RezState> RezReservations = new();

        /// <summary>Out-of-combat rez across an owner's whole force (task 44): a squad with
        /// nobody left to raise may reach into another live squad, or the owner himself, for
        /// a resurrector. One shared reservation per owner keeps two squads from casting on
        /// the same corpse, reusing the existing raid reservation pattern.</summary>
        public static GameLiving ReserveCrossSquadCorpse(GameBot caster, Spell resurrection)
        {
            GamePlayer owner = caster?.Owner;
            if (owner == null || resurrection == null || !caster.IsAlive || caster.IsCasting ||
                caster.IsCrowdControlled || caster.IsSilenced)
                return null;

            int range = caster.castingComponent.CalculateSpellRange(resurrection);
            GameLiving[] force = OwnerForceBots(owner).Cast<GameLiving>().Append(owner).ToArray();
            // Stays an out-of-combat rez, exactly like the ordinary same-group one: nobody
            // in the whole force may still be fighting.
            if (force.Any(member => member.IsAlive && member.InCombat))
                return null;

            RezState state = RezReservations.GetOrCreateValue(owner);
            foreach (GameLiving corpse in force
                         .Where(member => member != caster && !member.IsAlive &&
                             member.ObjectState == GameObject.eObjectState.Active &&
                             member.CurrentRegionID == caster.CurrentRegionID && caster.IsWithinRadius(member, range))
                         .OrderBy(member => member == owner ? 0 : 1)
                         .ThenBy(caster.GetDistanceTo))
            {
                if (!BotGroupSupport.HasCorpseLineOfSight(caster, corpse))
                    continue;

                if (state.Reservations.TryReserve(caster, corpse, GameLoop.GameLoopTime, resurrection.CastTime,
                        true, false, int.MaxValue, false, caster.CanCastHealSpells))
                    return corpse;
            }

            return null;
        }

        public static void ReleaseCrossSquadReservation(GameBot caster)
        {
            if (caster?.Owner != null && RezReservations.TryGetValue(caster.Owner, out RezState state))
                state.Reservations.ReleaseCaster(caster);
        }
    }
}
