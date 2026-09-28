using System;
using DOL.GS.ServerRules;

namespace DOL.GS
{
    /// <summary>
    /// A solo world bot's willingness to take on harder creatures. A real PvE
    /// defeat lowers the con ceiling at once; like a 2003 soloer's confidence it
    /// comes back one step at a time after a run of clean kills, on a new level,
    /// or when a new task starts. Without this recovery the ceiling only ever
    /// ratcheted down and levelers ended up hunting greens for a fraction of
    /// the experience of an even-con kill.
    /// </summary>
    public sealed class AutonomousSoloConfidence
    {
        /// <summary>Kills without a PvE defeat that earn back one step.</summary>
        public const int KillsPerRecoveryStep = 10;

        /// <summary>How many con steps below the natural ceiling the bot currently hunts.</summary>
        public int Steps { get; private set; }

        /// <summary>Kills counted toward the next recovery step.</summary>
        public int KillsTowardRecovery { get; private set; }

        public void Reset()
        {
            Steps = 0;
            KillsTowardRecovery = 0;
        }

        /// <summary>
        /// Applies a real PvE defeat: at least one step per new defeat and at
        /// least the step below the con that failed. Clean kills start over.
        /// </summary>
        public void RecordPveDefeat(int newDefeats, int requiredSteps, int maximumSteps)
        {
            Steps = Math.Clamp(Math.Max(Steps + Math.Max(0, newDefeats), requiredSteps), 0, Math.Max(0, maximumSteps));
            KillsTowardRecovery = 0;
        }

        /// <summary>Counts kills; returns true when they earned back one step.</summary>
        public bool RecordKills(int kills)
        {
            if (kills <= 0)
                return false;
            if (Steps == 0)
            {
                // Kills at full confidence are not banked against a later defeat.
                KillsTowardRecovery = 0;
                return false;
            }

            KillsTowardRecovery += kills;
            if (KillsTowardRecovery < KillsPerRecoveryStep)
                return false;
            Steps--;
            KillsTowardRecovery = 0;
            return true;
        }

        /// <summary>A level-up or a new task: one step back toward the default.</summary>
        public bool RecoverStep()
        {
            if (Steps == 0)
                return false;
            Steps--;
            KillsTowardRecovery = 0;
            return true;
        }
    }

    /// <summary>What killed an autonomous world bot, captured once at its death.</summary>
    public sealed record AutonomousDeathSnapshot(
        long Tick,
        ushort RegionId,
        string ZoneName,
        int X,
        int Y,
        int Z,
        string KillerName,
        int KillerLevel,
        string KillerType,
        string KillerClass,
        eRealm KillerRealm,
        bool ViaPet,
        bool AreaEffect,
        bool TargetedVictim,
        bool CountsAsPvp,
        string PvpSource,
        string RecentPvpAttackerName,
        string RecentPvpAttackerType);

    /// <summary>
    /// Decides whether a world bot's death was PvP for the purpose of its PvE
    /// difficulty. A leveler who is ganked by a player (or a bot of another
    /// guild) and finished off by the mob it was fighting learned nothing about
    /// that mob; such a death must not lower its con ceiling.
    /// </summary>
    public static class AutonomousDeathAttribution
    {
        public const long RecentPvpDamageMilliseconds = 30_000;
        private const long KillingHitMatchMilliseconds = 5_000;

        public const string Mob = "mob";
        public const string Player = "player";
        public const string Companion = "companion";
        public const string Helper = "helper";
        public const string WorldBot = "world_bot";
        public const string None = "none";

        public static bool HadRecentPvpDamage(long lastPvpDamageTick, long deathTick) =>
            lastPvpDamageTick > 0 && deathTick >= lastPvpDamageTick &&
            deathTick - lastPvpDamageTick <= RecentPvpDamageMilliseconds;

        public static bool CountsAsPvp(bool killerIsPlayerShaped, long lastPvpDamageTick, long deathTick) =>
            killerIsPlayerShaped || HadRecentPvpDamage(lastPvpDamageTick, deathTick);

        public static string PvpSource(bool killerIsPlayerShaped, long lastPvpDamageTick, long deathTick) =>
            killerIsPlayerShaped ? "killer" : HadRecentPvpDamage(lastPvpDamageTick, deathTick) ? "recent_damage" : None;

        /// <summary>
        /// True for damage by a real player, companion or bot (or their pet)
        /// that is not allied to the victim. Misses and resists do not count.
        /// </summary>
        public static bool IsHostilePlayerShapedDamage(GameLiving victim, AttackData ad, out GameLiving identity) =>
            IsHostilePlayerShapedDamage(victim, ad, PvpCombatant.AreAllied, out identity);

        /// <summary>Same rule with the Camlann alliance test supplied (tests).</summary>
        public static bool IsHostilePlayerShapedDamage(GameLiving victim, AttackData ad,
            Func<GameLiving, GameLiving, bool> areAllied, out GameLiving identity)
        {
            identity = null;
            if (ad?.Attacker == null || victim == null || ad.Damage + ad.CriticalDamage <= 0)
                return false;
            identity = PvpCombatant.Resolve(ad.Attacker);
            return identity != null && identity != victim && !areAllied(identity, victim);
        }

        public static bool IsAreaEffect(AttackData ad) =>
            ad?.SpellHandler?.Spell is Spell spell && spell.Radius > 0;

        public static string TypeOf(GameLiving identity, GameObject rawKiller) => identity switch
        {
            GamePlayer => Player,
            GameBot { IsTemporaryGroupHelper: true } => Helper,
            GameBot { IsAutonomousWorldBot: true } => WorldBot,
            GameBot => Companion,
            _ => rawKiller is GameLiving ? Mob : None,
        };

        public static string ClassOf(GameLiving identity) => identity switch
        {
            GamePlayer player => player.CharacterClass?.Name ?? "-",
            GameBot bot => bot.CharacterClass?.Name ?? bot.ClassName ?? "-",
            _ => "-",
        };

        /// <summary>One allocation per death; never called per hit.</summary>
        public static AutonomousDeathSnapshot Capture(GameBot victim, GameObject killer, long now,
            GameLiving lastHitAttacker, long lastHitTick, bool lastHitArea, bool lastHitTargeted,
            GameLiving recentPvpAttacker, long recentPvpTick)
        {
            GameLiving killerLiving = killer as GameLiving;
            GameLiving identity = PvpCombatant.Resolve(killerLiving);
            bool killerPlayerShaped = identity != null;
            bool killingHitKnown = killerLiving != null && lastHitAttacker == killerLiving &&
                                   now - lastHitTick <= KillingHitMatchMilliseconds;
            bool targeted = killingHitKnown
                ? lastHitTargeted
                : killerLiving != null && (killerLiving.TargetObject == victim || identity?.TargetObject == victim);
            bool recent = HadRecentPvpDamage(recentPvpTick, now);
            GameLiving named = identity ?? killerLiving;
            return new AutonomousDeathSnapshot(
                now,
                victim.CurrentRegionID,
                victim.CurrentZone?.Description ?? string.Empty,
                victim.X,
                victim.Y,
                victim.Z,
                named?.Name ?? killer?.Name ?? string.Empty,
                named?.Level ?? 0,
                TypeOf(identity, killer),
                ClassOf(identity),
                named?.Realm ?? eRealm.None,
                killerPlayerShaped && killerLiving != identity,
                killingHitKnown && lastHitArea,
                targeted,
                CountsAsPvp(killerPlayerShaped, recentPvpTick, now),
                PvpSource(killerPlayerShaped, recentPvpTick, now),
                recent ? recentPvpAttacker?.Name ?? string.Empty : string.Empty,
                recent ? TypeOf(recentPvpAttacker, recentPvpAttacker) : None);
        }
    }
}
