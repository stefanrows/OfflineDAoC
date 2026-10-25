using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Events;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS
{
    public class RelicMgr
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        // Relics are not expected to be modified after initialization.
        private static readonly Dictionary<int, GameRelic> _relicsMap = [];
        private static volatile GameRelic[] _relicsArray = [];

        // Relic pads are loaded concurrently via GameRelicPad.AddToWorld.
        private static readonly List<GameRelicPad> _relicPads = [];
        private static readonly Lock _relicPadsLock = new();

        public static bool Init()
        {
            lock (_relicPadsLock)
            {
                foreach (GameRelic relic in _relicsMap.Values)
                {
                    relic.SaveIntoDatabase();
                    relic.RemoveFromWorld();
                }

                _relicsMap.Clear();
                _relicsArray = [];

                foreach (GameRelicPad pad in _relicPads)
                    pad.RemoveRelics();

                List<GameRelic> lostRelics = [];
                IList<DbRelic> dbRelics = GameServer.Database.SelectAllObjects<DbRelic>();

                foreach (DbRelic dbRelic in dbRelics)
                {
                    if (dbRelic.relicType < 0 || dbRelic.relicType > 1 || dbRelic.OriginalRealm < 1 || dbRelic.OriginalRealm > 3)
                    {
                        if (log.IsWarnEnabled)
                            log.Warn($"Could not load {dbRelic.RelicID}: Realm or Type mismatch.");

                        continue;
                    }

                    if (WorldMgr.GetRegion((ushort) dbRelic.Region) == null)
                    {
                        if (log.IsWarnEnabled)
                            log.Warn($"Could not load {dbRelic.RelicID}: Region mismatch.");

                        continue;
                    }

                    GameRelic relic = new(dbRelic);
                    _relicsMap[dbRelic.RelicID] = relic;
                    relic.AddToWorld();
                    GameRelicPad pad = null;

                    if (relic.MountedKeepID > 0)
                    {
                        AbstractGameKeep mountedKeep = GameServer.KeepManager.GetKeepByID(relic.MountedKeepID);
                        mountedKeep?.EnsureRelicPad();
                        pad = mountedKeep?.RelicPad;
                    }

                    foreach (GameRelicPad relicPad in _relicPads)
                    {
                        if (pad == null && relic.IsWithinRadius(relicPad, 200))
                            pad = relicPad;
                    }

                    if (pad != null)
                    {
                        if (pad.AcceptsRelicType(relic.RelicType))
                        {
                            relic.RelicPadTakesOver(pad, true);

                            if (log.IsDebugEnabled)
                                log.Debug($"{relic.Name} has been loaded and added to pad {pad.Name}.");
                        }
                    }
                    else
                        lostRelics.Add(relic);
                }

                foreach (GameRelic lostRelic in lostRelics)
                {
                    eRealm returnRealm = lostRelic.LastRealm;

                    if (returnRealm is eRealm.None)
                        returnRealm = lostRelic.OriginalRealm;

                    foreach (GameRelicPad pad in _relicPads)
                    {
                        if (pad is not GameKeepRelicPad && pad.Realm == returnRealm && pad.PadType == lostRelic.RelicType && lostRelic.RelicPadTakesOver(pad, true))
                        {
                            if (log.IsDebugEnabled)
                                log.Debug($"Lost relic '{lostRelic.Name}' has returned to last pad '{pad.Name}'");
                        }
                    }
                }

                foreach (GameRelic lostRelic in lostRelics)
                {
                    if (lostRelic.CurrentRelicPad == null)
                    {
                        foreach (GameRelicPad pad in _relicPads)
                        {
                            if (pad is not GameKeepRelicPad && pad.PadType == lostRelic.RelicType && lostRelic.RelicPadTakesOver(pad, true))
                            {
                                if (log.IsDebugEnabled)
                                    log.Debug($"Lost relic '{lostRelic.Name}' auto assigned to pad '{pad.Name}'");
                            }
                        }
                    }
                }

                _relicsArray = [.. _relicsMap.Values];

                if (log.IsDebugEnabled)
                {
                    log.Debug($"{_relicPads.Count} relic pad{(_relicPads.Count > 1 ? "s were" : " was")} loaded.");
                    log.Debug($"{_relicsMap.Count} relic{(_relicsMap.Count > 1 ? "s were" : " was")} loaded.");
                }
            }

            return true;
        }

        public static int GetDaysSinceCapture(GameRelic relic)
        {
            TimeSpan daysPassed = WorldSimulationClock.LocalNow.Subtract(relic.LastCaptureDate);
            return daysPassed.Days;
        }

        public static void AddRelicPad(GameRelicPad pad)
        {
            lock (_relicPadsLock)
            {
                if (!_relicPads.Contains(pad))
                    _relicPads.Add(pad);
            }
        }

        public static GameRelicPad[] GetPadsSnapshot()
        {
            lock (_relicPadsLock)
                return _relicPads.ToArray();
        }

        public static void RemoveRelicPad(GameRelicPad pad)
        {
            lock (_relicPadsLock)
            {
                _relicPads.Remove(pad);
            }
        }

        public static GameRelic GetRelic(int id)
        {
            return _relicsMap.TryGetValue(id, out GameRelic relic) ? relic : null;
        }

        public static int GetRelicCount(eRealm realm)
        {
            int count = 0;
            GameRelic[] snapshot = _relicsArray;

            for (int i = 0; i < snapshot.Length; i++)
            {
                GameRelic relic = snapshot[i];

                if (relic.Realm == realm && relic.IsMounted)
                    count++;
            }

            return count;
        }

        public static int GetRelicCount(eRealm realm, eRelicType type)
        {
            int count = 0;
            GameRelic[] snapshot = _relicsArray;

            for (int i = 0; i < snapshot.Length; i++)
            {
                GameRelic relic = snapshot[i];

                if (relic.Realm == realm && relic.RelicType == type && relic.IsMounted)
                    count++;
            }

            return count;
        }

        public static GameRelic[] GetRelics()
        {
            return _relicsArray;
        }

        public static double GetRelicBonusModifier(GameLiving living, eRelicType type)
        {
            if (living == null || !living.BenefitsFromRelics)
                return 1.0;

            // RvR world (R3b): the realm-wide upstream bonus.
            if (WorldMode.IsRvr)
                return GetRealmRelicBonusModifier(ServerRules.PvpCombatant.AllianceRealm(living), type);

            Guild guild = ServerRules.PvpCombatant.GuildOf(living);
            if (!ServerRules.PvpCombatant.IsRealGuild(guild))
                return 1.0;

            int mountedCount = 0;
            foreach (GameRelic relic in _relicsArray)
            {
                if (relic.RelicType == type && relic.CurrentRelicPad is GameKeepRelicPad pad && pad.Guild == guild)
                    mountedCount++;
            }

            return 1.0 + mountedCount * ServerProperties.Properties.RELIC_OWNING_BONUS * 0.01;
        }

        public static bool CanPickupRelicFromShrine(GameLiving player, GameRelic relic)
        {
            if (player == null || relic == null)
                return false;

            // RvR world (R3b): realm pads behind the milegates, upstream pickup rules.
            if (WorldMode.IsRvr)
                return GetRvrPickupVerdict(player, relic) == RvrRelicRules.PickupVerdict.Allowed;

            if (GameServer.Instance == null || GameServer.ServerRules is not PvPServerRules)
            {
                if (!relic.IsMounted)
                    return true;

                if (player.Realm == relic.OriginalRealm)
                    return true;

                foreach (GameRelic otherRelic in _relicsArray)
                {
                    if (otherRelic.Realm == player.Realm && otherRelic.OriginalRealm == player.Realm &&
                        otherRelic.RelicType == relic.RelicType && otherRelic.IsMounted)
                        return true;
                }

                return false;
            }

            Guild guild = ServerRules.PvpCombatant.GuildOf(player);
            if (!ServerRules.PvpCombatant.IsRealGuild(guild) || guild.ClaimedKeeps.Count == 0) return false;
            if (!relic.IsMounted) return true;
            if (relic.CurrentRelicPad is GameKeepRelicPad keepPad)
                return keepPad.Keep.DBKeep.LordDefeated;
            // A shrine's guards are stationary world NPCs, not autonomous bots.
            AbstractGameKeep shrine = GameServer.KeepManager.GetClosestKeepToSpot(relic.CurrentRegionID, relic, 5000);
            return shrine?.IsRelic == true && !shrine.Guards.Values.Any(guard =>
                guard.IsAlive && guard.ObjectState == GameObject.eObjectState.Active &&
                guard is not GuardMerchant && guard is not FrontierHastener &&
                (guard.Flags & GameNPC.eFlags.PEACE) == 0);

        }

        // ---- RvR world (R3b): relics belong to realms -------------------------------------------------------------

        /// <summary>
        /// The upstream realm-wide relic bonus for <paramref name="realm"/>: +relic_owning_bonus per cent per enemy
        /// relic of the type on the realm's pads, while the realm holds its own relic of that type.
        /// </summary>
        public static double GetRealmRelicBonusModifier(eRealm realm, eRelicType type)
        {
            GameRelic[] snapshot = _relicsArray;
            var states = new RvrRelicRules.RelicState[snapshot.Length];

            for (int i = 0; i < snapshot.Length; i++)
                states[i] = StateOf(snapshot[i]);

            return RvrRelicRules.BonusModifier(realm, type, states, ServerProperties.Properties.RELIC_OWNING_BONUS);
        }

        private static RvrRelicRules.RelicState StateOf(GameRelic relic) =>
            new(relic.Realm, relic.OriginalRealm, relic.RelicType, relic.IsMounted && relic.CurrentRelicPad is not GameKeepRelicPad);

        /// <summary>
        /// Who holds a keep for the milegate rule: the keep's realm, or nobody for a missing keep or one the
        /// Frontier Wardens garrison (a neutral keep was captured by no realm).
        /// </summary>
        public static eRealm MilegateHolder(int keepId)
        {
            AbstractGameKeep keep = GameServer.KeepManager?.GetKeepByID(keepId);
            if (keep == null || PvpKeepCampaign.IsGarrison(keep.Guild))
                return eRealm.None;
            return keep.Realm;
        }

        /// <summary>Whether the temple behind <paramref name="pad"/> stands open to its realm's enemies.</summary>
        public static bool IsTempleOpen(GameRelicPad pad) => IsTempleOpen(pad, MilegateHolder);

        public static bool IsTempleOpen(GameRelicPad pad, Func<int, eRealm> keepHolder) =>
            pad != null && pad is not GameKeepRelicPad && RvrRelicRules.IsTempleOpen(pad.Realm, pad.PadType, keepHolder);

        public static RvrRelicRules.PickupVerdict GetRvrPickupVerdict(GameLiving taker, GameRelic relic) =>
            GetRvrPickupVerdict(taker, relic, MilegateHolder);

        public static RvrRelicRules.PickupVerdict GetRvrPickupVerdict(GameLiving taker, GameRelic relic, Func<int, eRealm> keepHolder)
        {
            if (taker == null || relic == null)
                return RvrRelicRules.PickupVerdict.NoRealm;

            eRealm takerRealm = ServerRules.PvpCombatant.AllianceRealm(taker);
            GameRelicPad pad = relic.CurrentRelicPad;
            bool mounted = relic.IsMounted && pad != null;
            bool templeOpen = mounted && IsTempleOpen(pad, keepHolder);
            bool holdsOwn = false;

            foreach (GameRelic other in _relicsArray)
            {
                if (other.Realm == takerRealm && other.OriginalRealm == takerRealm && other.RelicType == relic.RelicType && other.IsMounted)
                {
                    holdsOwn = true;
                    break;
                }
            }

            return RvrRelicRules.Pickup(takerRealm, mounted, mounted ? pad.Realm : relic.Realm, relic.OriginalRealm, templeOpen, holdsOwn);
        }

        /// <summary>The refusal a player or bot hears for a relic it may not lift in the RvR world, or null.</summary>
        public static string GetRvrPickupRefusal(GameLiving taker, GameRelic relic)
        {
            RvrRelicRules.PickupVerdict verdict = GetRvrPickupVerdict(taker, relic);
            if (verdict == RvrRelicRules.PickupVerdict.Allowed)
                return null;

            List<string> sealing = [];
            if (verdict == RvrRelicRules.PickupVerdict.TempleSealed && relic.CurrentRelicPad is GameRelicPad pad)
            {
                foreach (int keepId in RvrRelicRules.SealingKeeps(pad.Realm, pad.PadType, MilegateHolder))
                {
                    string name = GameServer.KeepManager?.GetKeepByID(keepId)?.Name;
                    sealing.Add(string.IsNullOrEmpty(name) ? $"keep {keepId}" : name);
                }
            }

            return RvrRelicRules.RefusalText(verdict, relic.GetName(0, false), relic.RelicType, sealing);
        }

        /// <summary>The realm temple pad of a relic keep (none for another keep or a missing pad).</summary>
        public static GameRelicPad GetTemplePad(AbstractGameKeep keep)
        {
            if (keep == null || !keep.IsRelic)
                return null;

            RvrRelicRules.TempleChain chain = RvrRelicRules.ChainForRelicKeep(keep.KeepID);
            GameRelicPad nearest = null;
            long nearestDistance = long.MaxValue;

            foreach (GameRelicPad pad in GetPadsSnapshot())
            {
                if (pad is GameKeepRelicPad)
                    continue;

                if (chain != null)
                {
                    if (pad.Realm == chain.Realm && pad.PadType == chain.Type)
                        return pad;

                    continue;
                }

                if (pad.CurrentRegionID != keep.Region)
                    continue;

                long dx = pad.X - keep.X;
                long dy = pad.Y - keep.Y;
                long distance = dx * dx + dy * dy;

                if (distance < nearestDistance && distance <= 3000L * 3000L)
                {
                    nearest = pad;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        /// <summary>
        /// RvR world: whether a world-bot army of <paramref name="raider"/>'s realm should raid this relic keep: an
        /// enemy realm's temple whose milegate is open and that holds a relic the raider's realm may lift. Never in
        /// Camlann (relic raids stay off there, CAMLANN.md decision 10).
        /// </summary>
        public static bool IsRelicRaidTarget(AbstractGameKeep keep, GameLiving raider)
        {
            if (!WorldMode.IsRvr || raider == null)
                return false;

            GameRelicPad pad = GetTemplePad(keep);
            if (pad == null || pad.Realm == ServerRules.PvpCombatant.AllianceRealm(raider))
                return false;

            foreach (GameRelic relic in pad.MountedRelics)
            {
                if (GetRvrPickupVerdict(raider, relic) == RvrRelicRules.PickupVerdict.Allowed)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Where a carrier brings a relic. RvR world: its realm's temple pad of the relic's type (strength to
        /// strength, magic to magic), the upstream realm pad. Camlann: the carrier guild's claimed keep mount.
        /// </summary>
        public static GameRelicPad FindDeliveryPad(GameLiving carrier, Guild carrierGuild, GameRelic relic)
        {
            if (carrier == null || relic == null)
                return null;

            if (WorldMode.IsRvr)
            {
                eRealm realm = ServerRules.PvpCombatant.AllianceRealm(carrier);
                return GetPadsSnapshot().FirstOrDefault(pad =>
                    pad is not GameKeepRelicPad && pad.Realm == realm && pad.AcceptsRelicType(relic.RelicType) &&
                    pad.MountedRelics.Count < 3 && pad.CanReceiveRelic(carrier, relic));
            }

            return GetPadsSnapshot().FirstOrDefault(pad =>
                pad is GameKeepRelicPad keepPad && carrierGuild != null && keepPad.Guild == carrierGuild &&
                pad.AcceptsRelicType(relic.RelicType) && pad.MountedRelics.Count < 3 &&
                pad.CanReceiveRelic(carrier, relic));
        }

        public static GameRelicPad GetHomePad(GameRelic relic)
        {
            if (relic == null)
                return null;

            foreach (GameRelicPad pad in GetPadsSnapshot())
            {
                if (pad is not GameKeepRelicPad && pad.Realm == relic.OriginalRealm && pad.PadType == relic.RelicType)
                    return pad;
            }

            return null;
        }

        public static void HomeRelicsFromKeep(AbstractGameKeep keep)
        {
            if (keep?.RelicPad == null)
                return;

            foreach (GameRelic relic in keep.RelicPad.MountedRelics)
            {
                GameRelicPad homePad = GetHomePad(relic);
                if (homePad != null)
                    relic.ReturnToShrine(homePad);
            }
        }

        [ScriptLoadedEvent]
        private static void ScriptLoaded(DOLEvent e, object sender, EventArgs args)
        {
            Init();
        }
    }
}
