using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.ServerRules;
using DOL.GS.ServerProperties;
using static DOL.GS.ServerRules.IServerRules;

namespace DOL.GS;

/// <summary>
/// Treats a persistent autonomous gamebot as a player only for RvR realm-point
/// credit. Ordinary NPCs, controlled pets, and temporary /spawn companions
/// remain ineligible victims.
/// </summary>
public static class AutonomousBotRealmPointRewards
{
    internal const string LastRealmPointDeathTickProperty = "autonomous.rvr.last.realm.point.death.tick";

    public static bool IsEligibleVictim(GameNPC npc) =>
        npc is GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false };

    public static int GetPlayerEquivalentRealmPointValue(byte level, int realmLevel)
    {
        // Keep the high-level curve, but give leveling kills a rising value.
        // Humans and persistent bots use this same formula.
        int modifiedLevel = Math.Max(0, level - 20);
        return Math.Max(level * 5, modifiedLevel * modifiedLevel) + realmLevel;
    }

    // Defeating a higher-level opponent raises both rewards and their caps.
    // The bounded bonus preserves damage sharing and repeat-kill protection.
    public static double ChallengeMultiplier(int victimLevel, int awarderLevel) =>
        1.0 + Math.Clamp(victimLevel - awarderLevel, 0, 4) * 0.25;

    public static long CalculateExperienceReward(long victimValue, long awarderValue,
        int victimLevel, int awarderLevel, int participants, double damagePercent, int capPercent)
    {
        if (participants <= 0 || damagePercent <= 0) return 0;
        double bonus = ChallengeMultiplier(victimLevel, awarderLevel);
        return (long)(Math.Min(victimValue / (double)participants,
            awarderValue * (double)capPercent / 100) * bonus * Math.Min(1, damagePercent));
    }

    public static int CalculateRealmPointReward(int victimRealmPointValue, int victimRealmLevel,
        int awarderRealmPointValue, int awarderRealmLevel, int participantCount,
        int groupContributorCount, double damagePercent, bool applyRealmRankAdjustment,
        int victimLevel = 50, int awarderLevel = 50)
    {
        if (victimRealmPointValue <= 0 || awarderRealmPointValue <= 0 ||
            participantCount <= 0 || damagePercent <= 0)
        {
            return 0;
        }

        double baseRealmPoints = victimRealmPointValue / (double)participantCount;
        baseRealmPoints = Math.Min(baseRealmPoints, awarderRealmPointValue * 2);
        double realmPoints = baseRealmPoints * Math.Min(1.0, damagePercent);

        if (applyRealmRankAdjustment)
        {
            realmPoints *= 1.0 + 2.0 * (victimRealmLevel - awarderRealmLevel) / 900.0;
        }

        if (groupContributorCount > 1)
            realmPoints *= 1.0 + (groupContributorCount - 1) * 0.125;

        return Math.Max(1, (int)(realmPoints * ChallengeMultiplier(victimLevel, awarderLevel)));
    }

    public static void Award(GameBot killedBot, GameObject killer)
    {
        if (!IsEligibleVictim(killedBot))
            return;

        long now = GameLoop.GameLoopTime;
        long previousDeath = killedBot.TempProperties.GetProperty<long>(
            LastRealmPointDeathTickProperty, -1);
        long worthInterval = Math.Max(0, Properties.RP_WORTH_SECONDS) * 1000L;
        bool isWorthRealmPoints = previousDeath < 0 || now - previousDeath >= worthInterval;

        // Every death resets the same repeat-kill window used for a real player,
        // including a death caused by an NPC or by somebody who receives no RP.
        killedBot.TempProperties.SetProperty(LastRealmPointDeathTickProperty, now);

        KeyValuePair<GameLiving, double>[] rawContributors;
        lock (killedBot.XpGainersLock)
            rawContributors = killedBot.XPGainers.ToArray();

        Dictionary<GameLiving, double> hostileContributors = new();
        foreach (KeyValuePair<GameLiving, double> pair in rawContributors)
        {
            GameLiving credited = ResolveRootRewardOwner(pair.Key);
            if (credited == null || credited.Realm == eRealm.None || PvpCombatant.AreAllied(credited, killedBot))
                continue;

            hostileContributors[credited] = hostileContributors.TryGetValue(credited, out double existing)
                ? existing + pair.Value
                : pair.Value;
        }

        double totalDamage = hostileContributors.Sum(pair => pair.Value);
        if (totalDamage <= 0)
            return;

        if (Properties.PVP_DEATH_CON_LOSS)
            killedBot.TotalConstitutionLostAtDeath += 3;

        Dictionary<GamePlayer, EntityCountTotalDamagePair> playerContributions = new();
        Dictionary<GameBot, EntityCountTotalDamagePair> botContributions = new();
        Dictionary<Group, EntityCountTotalDamagePair> groupContributions = new();

        foreach (KeyValuePair<GameLiving, double> pair in hostileContributors)
        {
            // Persistent gamebots remain part of the damage denominator, just
            // like another real participant, but this path only pays connected
            // players. Temporary companions have already resolved to the owner.
            if (pair.Key is GameBot bot)
            {
                if (!bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper ||
                    bot.ObjectState is not GameObject.eObjectState.Active ||
                    !bot.IsWithinRadius(killedBot, WorldMgr.MAX_EXPFORKILL_DISTANCE) ||
                    bot.IsObjectGreyCon(killedBot))
                    continue;

                AddContribution(bot, pair.Value, bot, botContributions);
                if (bot.Group != null)
                    AddContribution(bot, pair.Value, bot.Group, groupContributions);
                continue;
            }

            if (pair.Key is not GamePlayer player ||
                player.ObjectState is not GameObject.eObjectState.Active ||
                !player.IsWithinRadius(killedBot, WorldMgr.MAX_EXPFORKILL_DISTANCE) ||
                player.IsObjectGreyCon(killedBot))
            {
                continue;
            }

            AddContribution(player, pair.Value, player, playerContributions);
            if (player.Group != null)
                AddContribution(player, pair.Value, player.Group, groupContributions);
        }

        if (playerContributions.Count == 0 && botContributions.Count == 0)
            return;

        GameLiving creditedKiller = ResolveRootRewardOwner(killer as GameLiving);
        int victimValue = GetPlayerEquivalentRealmPointValue(killedBot.Level, killedBot.RealmLevel);

        foreach (KeyValuePair<GamePlayer, EntityCountTotalDamagePair> pair in playerContributions)
        {
            GamePlayer player = pair.Key;
            lock (player.AwardLock)
            {
                EntityCountTotalDamagePair contribution = pair.Value;
                if (player.Group != null && groupContributions.TryGetValue(player.Group, out EntityCountTotalDamagePair group))
                    contribution = group;

                double damagePercent = Math.Min(1.0, contribution.Damage / totalDamage);
                int contributorCount = Math.Max(1, contribution.Count);
                int groupContributorCount = player.Group == null ? 1 : contributorCount;
                int realmPointsEarned = 0;

                if (isWorthRealmPoints)
                {
                    DbBattleground battleground = GameServer.KeepManager.GetBattleground(player.CurrentRegionID);
                    bool applyRankAdjustment = battleground == null || player.RealmLevel < battleground.MaxRealmLevel;
                    realmPointsEarned = CalculateRealmPointReward(victimValue, killedBot.RealmLevel,
                        player.RealmPointsValue, player.RealmLevel, contributorCount,
                        groupContributorCount, damagePercent, applyRankAdjustment, killedBot.Level, player.Level);

                    if (realmPointsEarned > 0)
                        player.GainRealmPoints(realmPointsEarned, true);

                    long experience = CalculatePlayerKillExperience(killedBot, player, contribution,
                        totalDamage, damagePercent);
                    if (experience > 0)
                        player.GainExperience(eXPSource.Player, experience);
                }
                else
                {
                    player.Out.SendMessage($"{killedBot.Name} has been killed recently and is worth no realm points!",
                        eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                }

                bool deathBlow = ReferenceEquals(player, creditedKiller);
                bool soloKill = damagePercent >= 1.0 && contributorCount == 1;
                player.UpdateKillStatsOnPlayerKill(killedBot.Realm, deathBlow, soloKill, realmPointsEarned);
            }
        }

        foreach (KeyValuePair<GameBot, EntityCountTotalDamagePair> pair in botContributions)
        {
            GameBot bot = pair.Key;
            EntityCountTotalDamagePair contribution = pair.Value;
            if (bot.Group != null && groupContributions.TryGetValue(bot.Group, out EntityCountTotalDamagePair group))
                contribution = group;

            double damagePercent = Math.Min(1.0, contribution.Damage / totalDamage);
            if (!isWorthRealmPoints)
                continue;

            int contributorCount = Math.Max(1, contribution.Count);
            int botVictimValue = GetPlayerEquivalentRealmPointValue(killedBot.Level, killedBot.RealmLevel);
            int botValue = GetPlayerEquivalentRealmPointValue(bot.Level, bot.RealmLevel);
            int realmPoints = CalculateRealmPointReward(botVictimValue, killedBot.RealmLevel,
                botValue, bot.RealmLevel, contributorCount, contributorCount, damagePercent, true,
                killedBot.Level, bot.Level);
            if (realmPoints > 0)
                bot.GainRealmPoints(realmPoints, true);

            long experience = CalculatePlayerKillExperience(killedBot, bot, contribution,
                totalDamage, damagePercent);
            if (experience > 0)
                bot.GainExperience(eXPSource.Player, experience);
        }
    }

    public static GameLiving ResolveRootRewardOwner(GameLiving source)
    {
        GameLiving current = source;
        for (int depth = 0; depth < 16 && current is GameNPC npc &&
             npc.Brain is IControlledBrain controlled &&
             controlled.GetLivingOwner() is GameLiving owner; depth++)
        {
            current = owner;
        }

        return current;
    }

    private static void AddContribution<T>(GameLiving participant, double damage, T entity,
        Dictionary<T, EntityCountTotalDamagePair> contributions) where T : class, IGameStaticItemOwner
    {
        if (contributions.TryGetValue(entity, out EntityCountTotalDamagePair value))
        {
            value.Count++;
            value.Damage += damage;
            if (value.HighestLevelPlayer.Level < participant.Level)
                value.HighestLevelPlayer = participant;
        }
        else
        {
            contributions[entity] = new EntityCountTotalDamagePair(1, damage, participant);
        }
    }

    private static long CalculatePlayerKillExperience(GameBot killedBot, GameLiving awarder,
        EntityCountTotalDamagePair contribution, double totalDamage, double damagePercent)
    {
        if (contribution == null || totalDamage <= 0 || damagePercent <= 0)
            return 0;

        int contributorCount = Math.Max(1, contribution.Count);
        return CalculateExperienceReward(killedBot.GetExperienceValueForLevel(killedBot.Level) * 4,
            awarder.GetExperienceValueForLevel(awarder.Level) * 4, killedBot.Level, awarder.Level,
            contributorCount, damagePercent, Properties.XP_PVP_CAP_PERCENT);
    }
}
