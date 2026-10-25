using System;
using System.Collections.Generic;
using DOL.GS.ServerRules;

namespace DOL.GS
{
    /// <summary>
    /// Relic rules of the RvR world (save marker Realm-1, package R3b,
    /// docs/RVR_RELICS.md). Pure functions on realms and keep owners so the
    /// rules are testable without a running world; <see cref="RelicMgr"/>
    /// feeds them the live relics, pads and keeps.
    ///
    /// The six relics sit on the realm pads in the six relic temples (relic
    /// keeps). A relic is guarded by its temple's milegate: the temple stands
    /// open to the enemy only while every keep of its first milegate chain is
    /// held by an enemy realm (the 1.65 Old Frontiers rule, "the first milegate
    /// is opened by taking these three keeps"). The second milegate keep of
    /// 1.65 only opened another route to the same temple, so it does not seal
    /// the relic. A Frontier Warden or realm-less keep is nobody's capture:
    /// it keeps the milegate shut, so a fresh world, where every frontier keep
    /// starts neutral, starts with every relic sealed.
    /// </summary>
    public static class RvrRelicRules
    {
        /// <summary>
        /// RvR world: the pad crowd of the upstream 0.38c pickup. Upstream lowered
        /// relic_players_required_on_pad from 16 to 8 for the offline game (owner
        /// there, 2026-10-15) and counts players, gamebots and a player's own
        /// companion bots. The Camlann world keeps relic_players_required_on_pad
        /// (16); the RvR world reads this key so the two saves cannot overwrite
        /// each other's value.
        /// </summary>
        [ServerProperties.ServerProperty("pvp", "rvr_relic_players_required_on_pad",
            "RvR world (Realm-1): enemy players, gamebots and companion bots needed within 500 units of a relic pad before a relic can be lifted (upstream 0.38c: 8). The Camlann world uses relic_players_required_on_pad.", 8)]
        public static int RvrPlayersRequiredOnPad = 8;

        /// <summary>The pad crowd needed to lift a mounted relic in <paramref name="world"/>.</summary>
        public static int PlayersRequiredOnPad(WorldModelKind world) =>
            world == WorldModelKind.Realm ? RvrPlayersRequiredOnPad : ServerProperties.Properties.RELIC_PLAYERS_REQUIRED_ON_PAD;

        /// <summary>The pad crowd needed in the running world.</summary>
        public static int PlayersRequiredOnPad() => PlayersRequiredOnPad(WorldMode.Current);

        /// <summary>The milegate chain that guards one relic temple.</summary>
        /// <param name="Realm">The temple's realm (the pad's realm).</param>
        /// <param name="Type">The relic type the temple pad takes.</param>
        /// <param name="RelicKeepId">The relic keep (skin 99) that houses the temple.</param>
        /// <param name="FirstGate">Keeps that open the first milegate when all are in enemy hands.</param>
        /// <param name="SecondGate">The 1.65 second milegate keep (informational: another route, no seal).</param>
        public sealed record TempleChain(eRealm Realm, eRelicType Type, int RelicKeepId, string TempleName,
            int[] FirstGate, int SecondGate);

        /// <summary>
        /// Old Frontiers chains. The keep sets are the 1.65 milegate rules
        /// (Camelot Herald relic guide, kept in RelicGateMgr). Each chain is
        /// keyed to the temple by geography and by the relic-temple guard
        /// table (RelicGuardsOnKeepTaken): Caer Renaris stands beside Castle
        /// Excalibur and Caer Hurbury beside Castle Myrddin, so in Albion the
        /// Herald's "power" and "strength" wall names are swapped against the
        /// pads of this world (Excalibur holds the Scabbard, a strength relic).
        /// </summary>
        public static readonly IReadOnlyList<TempleChain> Chains =
        [
            new(eRealm.Albion, eRelicType.Strength, 57, "Castle Excalibur", [50, 51, 53], 56),
            new(eRealm.Albion, eRelicType.Magic, 58, "Castle Myrddin", [50, 52, 54], 55),
            new(eRealm.Midgard, eRelicType.Strength, 198, "Mjollner Faste", [75, 78, 77], 80),
            new(eRealm.Midgard, eRelicType.Magic, 82, "Grallarhorn Faste", [75, 76, 79], 81),
            new(eRealm.Hibernia, eRelicType.Strength, 111, "Dun Lamfhota", [100, 102, 104], 106),
            new(eRealm.Hibernia, eRelicType.Magic, 110, "Dun Dagda", [100, 101, 103], 105),
        ];

        public static TempleChain ChainFor(eRealm templeRealm, eRelicType type)
        {
            foreach (TempleChain chain in Chains)
            {
                if (chain.Realm == templeRealm && chain.Type == type)
                    return chain;
            }

            return null;
        }

        public static TempleChain ChainForRelicKeep(int relicKeepId)
        {
            foreach (TempleChain chain in Chains)
            {
                if (chain.RelicKeepId == relicKeepId)
                    return chain;
            }

            return null;
        }

        /// <summary>A keep counts as taken from <paramref name="templeRealm"/> when another playable realm holds it.</summary>
        public static bool IsTakenFrom(eRealm templeRealm, eRealm keepHolder) =>
            PvpCombatant.IsPlayableRealm(keepHolder) && keepHolder != templeRealm;

        /// <summary>
        /// The first-milegate keeps that still seal the temple: those not held
        /// by an enemy realm of the temple. Empty means the temple is open. A
        /// temple without chain data (another world layout) is open, as the
        /// upstream rules have no milegate at all.
        /// </summary>
        public static IReadOnlyList<int> SealingKeeps(eRealm templeRealm, eRelicType type, Func<int, eRealm> keepHolder)
        {
            TempleChain chain = ChainFor(templeRealm, type);
            if (chain == null)
                return [];

            List<int> sealing = [];
            foreach (int keepId in chain.FirstGate)
            {
                if (!IsTakenFrom(templeRealm, keepHolder(keepId)))
                    sealing.Add(keepId);
            }

            return sealing;
        }

        public static bool IsTempleOpen(eRealm templeRealm, eRelicType type, Func<int, eRealm> keepHolder) =>
            PvpCombatant.IsPlayableRealm(templeRealm) && SealingKeeps(templeRealm, type, keepHolder).Count == 0;

        public enum PickupVerdict
        {
            Allowed,
            /// <summary>The taker fights for no realm (a mob, a neutral encounter bot).</summary>
            NoRealm,
            /// <summary>The relic is mounted on the taker's own realm's pad.</summary>
            OwnRealmHolds,
            /// <summary>The temple's first milegate still stands.</summary>
            TempleSealed,
            /// <summary>Upstream rule: a realm must hold its own relic of this type before it takes another realm's.</summary>
            OwnRelicMissing,
        }

        /// <summary>
        /// Whether a fighter of <paramref name="takerRealm"/> may lift a relic.
        /// A dropped relic on the ground is free for anyone, as upstream. A
        /// mounted relic needs an enemy taker, an open temple and, for a
        /// foreign relic, the taker's realm holding its own relic of that type
        /// (upstream CanPickupRelicFromShrine). A realm may always win back its
        /// own relic from an open enemy temple. The pad crowd
        /// (relic_players_required_on_pad) is checked by the pickup itself.
        /// </summary>
        public static PickupVerdict Pickup(eRealm takerRealm, bool mounted, eRealm holderRealm, eRealm originalRealm,
            bool templeOpen, bool takerHoldsOwnRelicOfType)
        {
            if (!mounted)
                return PickupVerdict.Allowed;
            if (!PvpCombatant.IsPlayableRealm(takerRealm))
                return PickupVerdict.NoRealm;
            if (takerRealm == holderRealm)
                return PickupVerdict.OwnRealmHolds;
            if (!templeOpen)
                return PickupVerdict.TempleSealed;
            if (takerRealm != originalRealm && !takerHoldsOwnRelicOfType)
                return PickupVerdict.OwnRelicMissing;
            return PickupVerdict.Allowed;
        }

        /// <summary>One relic as the bonus sees it.</summary>
        public readonly record struct RelicState(eRealm Holder, eRealm Original, eRelicType Type, bool Mounted);

        /// <summary>
        /// The realm-wide damage modifier for <paramref name="realm"/> and a
        /// relic type, upstream rules: every enemy relic of that type mounted on
        /// the realm's pads adds <paramref name="percentPerRelic"/> per cent
        /// (relic_owning_bonus, 10 by default), and only while the realm also
        /// holds its own relic of that type. Strength relics raise melee damage,
        /// magic relics spell damage, heals and archery (RelicMgr callers).
        /// </summary>
        public static double BonusModifier(eRealm realm, eRelicType type, IEnumerable<RelicState> relics, int percentPerRelic)
        {
            if (!PvpCombatant.IsPlayableRealm(realm))
                return 1.0;

            bool ownsOwn = false;
            double modifier = 1.0;

            foreach (RelicState relic in relics)
            {
                if (!relic.Mounted || relic.Holder != realm || relic.Type != type)
                    continue;

                if (relic.Original == realm)
                    ownsOwn = true;
                else
                    modifier += percentPerRelic * 0.01;
            }

            return ownsOwn ? modifier : 1.0;
        }

        /// <summary>The player-facing refusal for a verdict, or null when the pickup may go on.</summary>
        public static string RefusalText(PickupVerdict verdict, string relicName, eRelicType type, IReadOnlyList<string> sealingKeeps)
        {
            return verdict switch
            {
                PickupVerdict.Allowed => null,
                PickupVerdict.NoRealm => $"You cannot pickup {relicName}. Only a fighter of a realm can carry a relic.",
                PickupVerdict.OwnRealmHolds => $"You cannot pickup {relicName}. It is owned by your realm.",
                PickupVerdict.TempleSealed => sealingKeeps == null || sealingKeeps.Count == 0
                    ? $"You cannot pickup {relicName}. Its milegate still stands."
                    : $"You cannot pickup {relicName}. Its milegate still stands until {JoinNames(sealingKeeps)} {(sealingKeeps.Count == 1 ? "is" : "are")} in enemy hands.",
                PickupVerdict.OwnRelicMissing => $"You cannot pickup {relicName}. You need to capture your realm's {type} relic first.",
                _ => $"You cannot pickup {relicName}.",
            };
        }

        public static string JoinNames(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0)
                return string.Empty;
            if (names.Count == 1)
                return names[0];
            return string.Join(", ", names, 0, names.Count - 1) + " and " + names[^1];
        }
    }
}
