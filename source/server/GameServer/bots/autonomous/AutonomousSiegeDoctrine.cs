using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// How an autonomous warband works a keep gate, as a decent 2003 group did:
    /// one or two operators bring rams from the hub, the casters ride the ram to
    /// speed it up (or nuke the gate when no seat is free), melee classes hit
    /// the same gate once the guards in reach are dead, healers stay free, and
    /// the lord is only attacked after every gate is down. Pure rules, so they can
    /// be tested without a running world.
    /// </summary>
    public static class AutonomousSiegeDoctrine
    {
        /// <summary>Operators start siege-equipment work this close to the keep.</summary>
        public const int SiegeJobRadius = 6000;
        /// <summary>
        /// Melee classes pick up the gate from here. The keep approach points lie
        /// 400-900 units out from a gate with up to 480 units of lateral spread
        /// (AutonomousRvrApproach.TryGateApproach), so a 400-unit reach would miss
        /// most of a warband standing at its approach.
        /// </summary>
        public const int DoorMeleeReach = 1100;
        /// <summary>Riders look for their operator's ram this close and board within 300.</summary>
        public const int RamBoardSearchRadius = 1500;
        public const int RamBoardRadius = 300;

        public enum KeepTarget { None, Lord, Guard, Door }

        /// <summary>
        /// Target order at a keep: the lord once every gate is down, otherwise any
        /// reachable guard, otherwise (classes that work the gate, see
        /// <see cref="CanDamageDoor"/>, not while operating an engine) the
        /// outermost standing gate within <see cref="DoorMeleeReach"/>.
        /// </summary>
        public static KeepTarget PickKeepTarget(bool anyGateClosed, bool lordTargetable, int otherGuards,
            double gateDistance, bool canMeleeDoor, bool operatingEngine)
        {
            if (lordTargetable && !anyGateClosed) return KeepTarget.Lord;
            if (otherGuards > 0) return KeepTarget.Guard;
            return anyGateClosed && canMeleeDoor && !operatingEngine && gateDistance <= DoorMeleeReach
                ? KeepTarget.Door : KeepTarget.None;
        }

        /// <summary>Pure casters do not melee a door (they nuke it, see
        /// <see cref="CanNukeDoor(eCharacterClass)"/>); the main healers and the
        /// Bard stay free to heal.</summary>
        public static bool CanMeleeDoor(eCharacterClass characterClass) =>
            characterClass != eCharacterClass.Unknown && !IsPureCaster(characterClass) && characterClass is not
                (eCharacterClass.Cleric or eCharacterClass.Healer or eCharacterClass.Shaman or
                 eCharacterClass.Druid or eCharacterClass.Bard);

        public static bool CanMeleeDoor(GameBot bot) =>
            bot?.CharacterClass != null && CanMeleeDoor((eCharacterClass)bot.CharacterClass.ID);

        /// <summary>Casters ride their group's ram first: on the ram each rider
        /// adds damage and shortens the reload.</summary>
        public static bool RidesRam(eCharacterClass characterClass) => IsPureCaster(characterClass);

        /// <summary>Since 1.46 direct-damage spells hurt a door at half effect
        /// (Keeps.KeepDoorSpellPolicy). A pure caster nukes the gate when no seat
        /// on its group's ram is free (owner decision 5, 2026-09-28).</summary>
        public static bool CanNukeDoor(eCharacterClass characterClass) => IsPureCaster(characterClass);

        /// <summary>Whether this class works the gate itself: melee classes always,
        /// pure casters only while no seat on their group's ram is free.</summary>
        public static bool CanDamageDoor(eCharacterClass characterClass, bool freeRamSeat) =>
            CanMeleeDoor(characterClass) || CanNukeDoor(characterClass) && !freeRamSeat;

        public static bool RidesRam(GameBot bot) =>
            bot?.CharacterClass != null && RidesRam((eCharacterClass)bot.CharacterClass.ID);

        public static bool IsPureCaster(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Cabalist or eCharacterClass.Necromancer or eCharacterClass.Sorcerer or
            eCharacterClass.Theurgist or eCharacterClass.Wizard or eCharacterClass.Bonedancer or
            eCharacterClass.Runemaster or eCharacterClass.Spiritmaster or eCharacterClass.Warlock or
            eCharacterClass.Animist or eCharacterClass.Bainshee or eCharacterClass.Eldritch or
            eCharacterClass.Enchanter or eCharacterClass.Mentalist;

        /// <summary>
        /// Siege-equipment work runs only for a force committed to an automatic
        /// keep assault: within <see cref="SiegeJobRadius"/> of the keep, while a
        /// started purchase trip is still running, or at the hub/home before the
        /// march when a ram can be bought on the way.
        /// </summary>
        public static bool ShouldRunSiegeJob(AutonomousRvrEventLayer.Intent intent, bool sharedEvent,
            bool inTargetRegion, double distanceToKeep, bool supplyTripRunning, bool canBuyBeforeMarch) =>
            intent == AutonomousRvrEventLayer.Intent.AssaultKeep && sharedEvent &&
            (supplyTripRunning || canBuyBeforeMarch || inTargetRegion && distanceToKeep <= SiegeJobRadius);

        /// <summary>A rider boards or stays on a ram run by an operator of its
        /// own group while that ram works a standing gate and a seat is free.</summary>
        public static bool ShouldRide(bool riderClass, bool sameGroupOperator, bool ramOnClosedGate,
            bool alreadyRiding, int riders, int seats, bool attackedInMelee) =>
            riderClass && sameGroupOperator && ramOnClosedGate && !attackedInMelee && (alreadyRiding || riders < seats);

        /// <summary>A caster walking to a ram it already chose keeps walking on
        /// every think; only a new search waits for the five-second throttle.
        /// Otherwise the keep-approach hold pulls it back between searches.</summary>
        public static bool ShouldSearchForRam(bool rememberedRamValid, long now, long nextSearch) =>
            rememberedRamValid || now >= nextSearch;

        /// <summary>A warband of one guild: every member in the leader's (non-null) guild.</summary>
        public static bool IsSingleGuild<TGuild>(TGuild guild, IEnumerable<TGuild> memberGuilds) where TGuild : class =>
            guild != null && memberGuilds != null && memberGuilds.All(member => ReferenceEquals(member, guild));

        /// <summary>The gate farthest from the keep centre: the outer gate before the inner one.</summary>
        public static T Outermost<T>(IEnumerable<T> gates, Func<T, Vector2> position, Vector2 keepCentre) where T : class =>
            gates?.OrderByDescending(gate => Vector2.DistanceSquared(position(gate), keepCentre)).FirstOrDefault();
    }
}
