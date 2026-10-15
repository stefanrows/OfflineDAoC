using System;
using System.Collections.Generic;
using DOL.GS.ServerRules;

namespace DOL.GS
{
    /// <summary>
    /// PvP kill and death counters for autonomous gamebots and persistent companions.
    /// The names match the DOLCharacters columns that the Herald leaderboard reads for
    /// players, and the rules are the player rules: every credited contributor gets a
    /// kill for the victim's realm, the resolved killer gets the death blow, and a lone
    /// contributor who dealt all of the damage gets a solo kill. A death is a PvP death
    /// when a credited player, autonomous bot or companion did damage.
    /// The owning GameBot serializes every access through its PvP stats gate.
    /// </summary>
    public sealed class BotPvpKillStats
    {
        public int KillsAlbionPlayers { get; private set; }
        public int KillsMidgardPlayers { get; private set; }
        public int KillsHiberniaPlayers { get; private set; }
        public int KillsAlbionDeathBlows { get; private set; }
        public int KillsMidgardDeathBlows { get; private set; }
        public int KillsHiberniaDeathBlows { get; private set; }
        public int KillsAlbionSolo { get; private set; }
        public int KillsMidgardSolo { get; private set; }
        public int KillsHiberniaSolo { get; private set; }
        public int DeathsPvP { get; private set; }

        /// <summary>The player solo rule: one contributor dealt all of the damage.</summary>
        public static bool IsSoloKill(double damagePercent, int contributorCount) =>
            damagePercent >= 1.0 && contributorCount == 1;

        /// <summary>
        /// Mirrors GamePlayer.UpdateKillStatsOnPlayerKill. A victim from a realm other than
        /// the three playable realms leaves every counter unchanged, as it does for players.
        /// </summary>
        public void RecordKill(eRealm victimRealm, bool deathBlow, bool soloKill)
        {
            switch (victimRealm)
            {
                case eRealm.Albion:
                    KillsAlbionPlayers++;
                    if (deathBlow)
                        KillsAlbionDeathBlows++;
                    if (soloKill)
                        KillsAlbionSolo++;
                    break;
                case eRealm.Midgard:
                    KillsMidgardPlayers++;
                    if (deathBlow)
                        KillsMidgardDeathBlows++;
                    if (soloKill)
                        KillsMidgardSolo++;
                    break;
                case eRealm.Hibernia:
                    KillsHiberniaPlayers++;
                    if (deathBlow)
                        KillsHiberniaDeathBlows++;
                    if (soloKill)
                        KillsHiberniaSolo++;
                    break;
            }
        }

        public void RecordDeath() => DeathsPvP++;

        public void CopyTo(OfflineWorldBotRecord record)
        {
            record.KillsAlbionPlayers = KillsAlbionPlayers;
            record.KillsMidgardPlayers = KillsMidgardPlayers;
            record.KillsHiberniaPlayers = KillsHiberniaPlayers;
            record.KillsAlbionDeathBlows = KillsAlbionDeathBlows;
            record.KillsMidgardDeathBlows = KillsMidgardDeathBlows;
            record.KillsHiberniaDeathBlows = KillsHiberniaDeathBlows;
            record.KillsAlbionSolo = KillsAlbionSolo;
            record.KillsMidgardSolo = KillsMidgardSolo;
            record.KillsHiberniaSolo = KillsHiberniaSolo;
            record.DeathsPvP = DeathsPvP;
        }

        public void CopyTo(PlayerCompanionRecord record)
        {
            record.KillsAlbionPlayers = KillsAlbionPlayers;
            record.KillsMidgardPlayers = KillsMidgardPlayers;
            record.KillsHiberniaPlayers = KillsHiberniaPlayers;
            record.KillsAlbionDeathBlows = KillsAlbionDeathBlows;
            record.KillsMidgardDeathBlows = KillsMidgardDeathBlows;
            record.KillsHiberniaDeathBlows = KillsHiberniaDeathBlows;
            record.KillsAlbionSolo = KillsAlbionSolo;
            record.KillsMidgardSolo = KillsMidgardSolo;
            record.KillsHiberniaSolo = KillsHiberniaSolo;
            record.DeathsPvP = DeathsPvP;
        }

        // Saved values are clamped at zero, as realm points are. A record written
        // before these columns existed reads as zero because the schema reconciler
        // gives every new column a NOT NULL DEFAULT 0.
        public void LoadFrom(OfflineWorldBotRecord record)
        {
            KillsAlbionPlayers = Math.Max(0, record.KillsAlbionPlayers);
            KillsMidgardPlayers = Math.Max(0, record.KillsMidgardPlayers);
            KillsHiberniaPlayers = Math.Max(0, record.KillsHiberniaPlayers);
            KillsAlbionDeathBlows = Math.Max(0, record.KillsAlbionDeathBlows);
            KillsMidgardDeathBlows = Math.Max(0, record.KillsMidgardDeathBlows);
            KillsHiberniaDeathBlows = Math.Max(0, record.KillsHiberniaDeathBlows);
            KillsAlbionSolo = Math.Max(0, record.KillsAlbionSolo);
            KillsMidgardSolo = Math.Max(0, record.KillsMidgardSolo);
            KillsHiberniaSolo = Math.Max(0, record.KillsHiberniaSolo);
            DeathsPvP = Math.Max(0, record.DeathsPvP);
        }

        public void LoadFrom(PlayerCompanionRecord record)
        {
            KillsAlbionPlayers = Math.Max(0, record.KillsAlbionPlayers);
            KillsMidgardPlayers = Math.Max(0, record.KillsMidgardPlayers);
            KillsHiberniaPlayers = Math.Max(0, record.KillsHiberniaPlayers);
            KillsAlbionDeathBlows = Math.Max(0, record.KillsAlbionDeathBlows);
            KillsMidgardDeathBlows = Math.Max(0, record.KillsMidgardDeathBlows);
            KillsHiberniaDeathBlows = Math.Max(0, record.KillsHiberniaDeathBlows);
            KillsAlbionSolo = Math.Max(0, record.KillsAlbionSolo);
            KillsMidgardSolo = Math.Max(0, record.KillsMidgardSolo);
            KillsHiberniaSolo = Math.Max(0, record.KillsHiberniaSolo);
            DeathsPvP = Math.Max(0, record.DeathsPvP);
        }

        /// <summary>
        /// The attacker filter of the player PvP kill path (AbstractServerRules.ProcessXpGainers),
        /// applied to a victim's XP gainers: a credited combatant (player, autonomous bot or
        /// companion, with pets resolved to their owner) that is active, within kill range and
        /// not allied with the victim. Temporary /spawn helpers and encounter bots never count.
        /// </summary>
        public static bool HasCreditedPvpAttacker(GameLiving victim, IEnumerable<GameLiving> attackers)
        {
            if (victim == null || attackers == null)
                return false;

            foreach (GameLiving attacker in attackers)
            {
                GameLiving combatant = PvpCombatant.Resolve(attacker);
                if (!IsCreditedCombatant(combatant))
                    continue;

                if (combatant.ObjectState is not GameObject.eObjectState.Active ||
                    !combatant.IsWithinRadius(victim, WorldMgr.MAX_EXPFORKILL_DISTANCE) ||
                    PvpCombatant.AreAllied(combatant, victim))
                    continue;

                return true;
            }

            return false;
        }

        private static bool IsCreditedCombatant(GameLiving combatant) => combatant switch
        {
            GamePlayer => true,
            GameBot { IsPersistentPlayerCompanion: true } => true,
            GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false } => true,
            _ => false,
        };
    }
}
