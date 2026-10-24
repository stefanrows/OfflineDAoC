using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DOL.AI.Brain;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>How a bot uses one Master Level ability on its own. None: held, never cast by the AI.</summary>
    public enum BotMasterLevelUse
    {
        None,
        /// <summary>A short area buff (Warlord) once the fight has started.</summary>
        FightBuff,
        /// <summary>Warlord's Bolstering Battlecry when someone near is badly hurt.</summary>
        HealBurst,
        /// <summary>Battlemaster's Sapping Strike on a player or bot with endurance left.</summary>
        EnduranceDrain,
        /// <summary>Battlemaster's Power Leak on a player or bot with power left.</summary>
        PowerDrain,
        /// <summary>Perfecter's Purging Wave when a group member is diseased.</summary>
        CureDisease,
        /// <summary>Perfecter's Purify Vision when a group member is nearsighted.</summary>
        CureNearsight,
    }

    /// <summary>
    /// Master Levels for companions and autonomous world bots (TOA_PLAN WP2.4). Real players use
    /// MasterLevelProgression; this class adds the bots to the same rules:
    ///
    /// <list type="bullet">
    /// <item>Companions are enrolled when the Arbiter enrols their owner, start at Master Level 1
    /// and get their discipline from their role (<see cref="DisciplineFor(GameBot)"/>).</item>
    /// <item>World bots of level 40 or higher roll a Master Level once on load
    /// (<c>bot_ml_distribution</c>) and get their discipline from their class.</item>
    /// <item>Both earn from the same kills as players: every NPC of level 45 or higher that
    /// MasterLevelProgression.KillValue pays for, 32,000 a Master Level.</item>
    /// </list>
    /// </summary>
    public static class BotMasterLevels
    {
        /// <summary>Master Level experience per level; the same 32,000 as GamePlayer's table.</summary>
        public const long EXPERIENCE_PER_LEVEL = 32_000;

        public const string DEFAULT_DISTRIBUTION = "28,16,13,11,9,7,5,4,3,2,2";

        [ServerProperty("autonomous", "bot_ml_distribution",
            "Weights for the Master Level a level 50 autonomous world bot rolls once when it first loads, for Master Level 0 to 10, " +
            "comma separated. The default makes about two thirds of them Master Level 0-3 and about one in nine Master Level 7-10. " +
            "Bots of level 40-49 roll from the same weights, capped at (level - 40) / 3. An invalid list falls back to the default.",
            DEFAULT_DISTRIBUTION)]
        public static string BOT_ML_DISTRIBUTION = DEFAULT_DISTRIBUTION;

        private static readonly object ExperienceLock = new();

        #region Discipline by role

        // The one table that picks a bot's discipline (owner decision 2026-10-24: tank Warlord,
        // melee Battlemaster, healer Perfecter, caster Convoker or Stormlord by class line,
        // stealther Spymaster). Pet casters walk Convoker, nukers Stormlord.
        private static readonly (string Path, eCharacterClass[] Classes)[] DisciplineTable =
        {
            ("Warlord", new[]
            {
                eCharacterClass.Armsman, eCharacterClass.Paladin, eCharacterClass.Reaver,
                eCharacterClass.Warrior, eCharacterClass.Thane, eCharacterClass.Valkyrie,
                eCharacterClass.Hero, eCharacterClass.Champion,
            }),
            ("Battlemaster", new[]
            {
                eCharacterClass.Mercenary, eCharacterClass.Heretic, eCharacterClass.MaulerAlb,
                eCharacterClass.Berserker, eCharacterClass.Savage, eCharacterClass.Skald, eCharacterClass.MaulerMid,
                eCharacterClass.Blademaster, eCharacterClass.Valewalker, eCharacterClass.Vampiir, eCharacterClass.MaulerHib,
            }),
            ("Perfecter", new[]
            {
                eCharacterClass.Cleric, eCharacterClass.Friar, eCharacterClass.Healer, eCharacterClass.Shaman,
                eCharacterClass.Druid, eCharacterClass.Warden, eCharacterClass.Bard,
            }),
            ("Spymaster", new[]
            {
                eCharacterClass.Infiltrator, eCharacterClass.Scout, eCharacterClass.Minstrel,
                eCharacterClass.Shadowblade, eCharacterClass.Hunter, eCharacterClass.Nightshade, eCharacterClass.Ranger,
            }),
            ("Convoker", new[]
            {
                eCharacterClass.Theurgist, eCharacterClass.Cabalist, eCharacterClass.Necromancer,
                eCharacterClass.Spiritmaster, eCharacterClass.Bonedancer, eCharacterClass.Enchanter, eCharacterClass.Animist,
            }),
            ("Stormlord", new[]
            {
                eCharacterClass.Wizard, eCharacterClass.Sorcerer, eCharacterClass.Runemaster, eCharacterClass.Warlock,
                eCharacterClass.Eldritch, eCharacterClass.Mentalist, eCharacterClass.Bainshee,
            }),
        };

        private static readonly Dictionary<eCharacterClass, string> DisciplineByClass =
            DisciplineTable.SelectMany(row => row.Classes.Select(cls => (cls, row.Path)))
                .ToDictionary(pair => pair.cls, pair => pair.Path);

        /// <summary>
        /// The discipline for a class, with the companion's chosen role on top: a tank class set
        /// to tank walks Warlord, a healing class set to healer walks Perfecter (a Paladin
        /// companion played as healer). Classes the table does not name walk Battlemaster.
        /// </summary>
        public static string DisciplineFor(eCharacterClass characterClass, string tacticalRole = null)
        {
            if (BotPartyRoles.TryParseRole(tacticalRole, out BotPveGroupRole role))
            {
                if (role == BotPveGroupRole.Healer && BotPartyRoles.IsHealingClass(characterClass))
                    return "Perfecter";
                if (role == BotPveGroupRole.Tank && BotPartyRoles.For(characterClass) == BotPartyRole.Tank)
                    return "Warlord";
            }

            return DisciplineByClass.TryGetValue(characterClass, out string path) ? path : "Battlemaster";
        }

        /// <summary>True if the table names this class (every advanced class should be named).</summary>
        public static bool IsInDisciplineTable(eCharacterClass characterClass) => DisciplineByClass.ContainsKey(characterClass);

        public static string DisciplineFor(GameBot bot) =>
            bot?.CharacterClass == null
                ? null
                : DisciplineFor((eCharacterClass)bot.CharacterClass.ID, bot.PlayerCompanionRecord?.TacticalRole);

        #endregion

        #region Enrolment

        /// <summary>
        /// Puts one companion on the path with its owner: Master Level 1 and the discipline of its
        /// role. Only persistent companions of level 40 or higher whose owner is enrolled; false
        /// for anything else and for a companion already on the path.
        /// </summary>
        public static bool EnrolCompanion(GameBot companion)
        {
            if (companion is not { IsPersistentPlayerCompanion: true } || !companion.CanHoldMasterLevels ||
                companion.IsOnMasterLevelPath || companion.Level < MasterLevelProgression.ENROL_MIN_LEVEL ||
                companion.Owner is not { MLGranted: true } || companion.CharacterClass == null)
                return false;

            companion.SetMasterLevelState(1, 0, DisciplineFor(companion));
            companion.RefreshMasterLevelSkills();
            companion.SaveMasterLevelProgress();
            return true;
        }

        /// <summary>
        /// The Arbiter's group enrolment for companions: every companion in the speaker's group,
        /// within <see cref="MasterLevelProgression.ENROL_RADIUS"/> of the Arbiter, whose owner is
        /// enrolled. A second visit enrols companions recruited after their owner. Each owner is
        /// told which companions took up which discipline.
        /// </summary>
        public static List<GameBot> EnrolCompanions(GamePlayer speaker, GameObject arbiter)
        {
            var enrolled = new List<GameBot>();

            if (speaker?.Group == null)
                return enrolled;

            foreach (GameLiving member in speaker.Group.GetMembersInTheGroup().ToList())
            {
                if (member is not GameBot companion || companion.ObjectState != GameObject.eObjectState.Active)
                    continue;

                if (arbiter != null && !companion.IsWithinRadius(arbiter, MasterLevelProgression.ENROL_RADIUS))
                    continue;

                if (EnrolCompanion(companion))
                    enrolled.Add(companion);
            }

            foreach (IGrouping<GamePlayer, GameBot> owned in enrolled.GroupBy(companion => companion.Owner))
            {
                string names = string.Join(", ", owned.Select(companion => $"{companion.Name} ({companion.MasterLevelPath})"));
                owned.Key?.Out?.SendMessage($"Your companions begin the trials with you: {names}.",
                    eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            }

            return enrolled;
        }

        #endregion

        #region World bots

        /// <summary>The eleven weights for Master Level 0-10, or the default ones for an invalid list.</summary>
        public static int[] Weights(string text)
        {
            int[] parsed = Parse(text);
            return parsed ?? Parse(DEFAULT_DISTRIBUTION);

            static int[] Parse(string value)
            {
                string[] parts = (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);

                if (parts.Length != GamePlayer.ML_MAX_LEVEL + 1)
                    return null;

                var weights = new int[parts.Length];

                for (int i = 0; i < parts.Length; i++)
                {
                    if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out weights[i]) || weights[i] < 0)
                        return null;
                }

                return weights.Sum() > 0 ? weights : null;
            }
        }

        /// <summary>
        /// The highest Master Level a world bot of this level may roll: none below 40, up to 3 at
        /// 40-49 ((level - 40) / 3), up to 10 at 50. The trials began at 40 on live, but most
        /// Master Levels were earned at 50.
        /// </summary>
        public static int MaxRolledLevel(int characterLevel)
        {
            if (characterLevel < MasterLevelProgression.ENROL_MIN_LEVEL)
                return -1;

            return characterLevel >= 50 ? GamePlayer.ML_MAX_LEVEL : (characterLevel - MasterLevelProgression.ENROL_MIN_LEVEL) / 3;
        }

        /// <summary>Picks a Master Level from the weights up to the level cap; sample is in [0, 1). -1 below level 40.</summary>
        public static int RollLevel(int characterLevel, double sample)
        {
            int cap = MaxRolledLevel(characterLevel);

            if (cap < 0)
                return -1;

            int[] weights = Weights(BOT_ML_DISTRIBUTION);
            int total = weights.Take(cap + 1).Sum();

            // No weight at or below the cap (a list that puts everything on high ranks): the cap.
            if (total <= 0)
                return cap;

            double pick = Math.Clamp(sample, 0, 0.999999) * total;

            for (int level = 0; level <= cap; level++)
            {
                pick -= weights[level];

                if (pick < 0)
                    return level;
            }

            return cap;
        }

        /// <summary>
        /// Puts a world bot of level 40 or higher on the path with a rolled Master Level and the
        /// discipline of its class. False for companions, helpers, bots below 40 and bots already
        /// on the path.
        /// </summary>
        public static bool TryRollWorldBot(GameBot bot, Func<double> sample)
        {
            if (bot is not { IsAutonomousWorldBot: true } || !bot.CanHoldMasterLevels || bot.IsOnMasterLevelPath ||
                bot.CharacterClass == null || sample == null)
                return false;

            int level = RollLevel(bot.Level, sample());

            if (level < 0)
                return false;

            bot.SetMasterLevelState(level, 0, DisciplineFor((eCharacterClass)bot.CharacterClass.ID));
            return true;
        }

        #endregion

        #region Experience

        /// <summary>
        /// Adds Master Level experience to a bot on the path and grants every Master Level it pays
        /// for, with the line's new spells and a save. The owner of a companion is told about each
        /// new Master Level. Returns the number of levels gained.
        /// </summary>
        public static int AddExperience(GameBot bot, long amount)
        {
            if (bot == null || !bot.IsOnMasterLevelPath || !bot.CanHoldMasterLevels)
                return 0;

            int from;
            int gained = 0;

            lock (ExperienceLock)
            {
                from = bot.MLLevel;

                if (bot.MLLevel >= GamePlayer.ML_MAX_LEVEL)
                {
                    bot.MLExperience = 0;
                    return 0;
                }

                long xp = Math.Max(0, bot.MLExperience + amount);

                while (bot.MLLevel < GamePlayer.ML_MAX_LEVEL && xp >= EXPERIENCE_PER_LEVEL)
                {
                    xp -= EXPERIENCE_PER_LEVEL;
                    bot.MLLevel++;
                    gained++;
                }

                bot.MLExperience = bot.MLLevel >= GamePlayer.ML_MAX_LEVEL ? 0 : xp;
            }

            if (gained > 0)
            {
                bot.RefreshMasterLevelSkills();

                for (int level = from + 1; level <= from + gained; level++)
                    bot.Owner?.Out?.SendMessage($"{bot.Name} has reached Master Level {level}!",
                        eChatType.CT_Group, eChatLoc.CL_SystemWindow);
            }

            bot.SaveMasterLevelProgress();
            return gained;
        }

        /// <summary>
        /// Every bot that earned this kill, alive, active and within experience range:
        /// <list type="bullet">
        /// <item>the active companions of every player who earned it by the player rule
        /// (MasterLevelProgression.Earners over the Dying event's PlayerKillers, which holds the
        /// owner of every attacking companion), found through the roster and the owner's group.
        /// A dead owner or one out of range takes no companion along; an owner at Master Level
        /// 10 still does;</item>
        /// <item>every world bot that damaged the NPC (directly or through its pet) and the world
        /// bots grouped with it. A world bot in a player's group earns only from its own damage
        /// (or its world-bot mates'), not from the players' kills.</item>
        /// </list>
        /// Only bots on the path count; helpers and encounter bots never do.
        /// </summary>
        public static List<GameBot> Earners(GameNPC dead, DyingEventArgs death)
        {
            var earners = new List<GameBot>();

            if (dead == null)
                return earners;

            var seen = new HashSet<GameBot>();

            // The owner must have earned the kill himself (MasterLevelProgression.Earners: on the
            // path, alive, active, in experience range). An owner at Master Level 10 still counts;
            // his companions keep earning although he earns nothing more.
            foreach (GamePlayer owner in MasterLevelProgression.Earners(dead, death).ToList())
            {
                IEnumerable<GameBot> companions = PlayerCompanionRoster.GetActiveCompanions(owner)
                    .Concat(owner.Group?.GetMembersInTheGroup().OfType<GameBot>() ?? Enumerable.Empty<GameBot>())
                    .Where(companion => companion.Owner == owner && companion.IsPersistentPlayerCompanion);

                foreach (GameBot companion in companions)
                    Consider(companion);
            }

            KeyValuePair<GameLiving, double>[] gainers = Array.Empty<KeyValuePair<GameLiving, double>>();

            // XPGainers is cleared only after the Dying notify (GameNPC.ProcessDeath), so the
            // world bots that fought are still listed here.
            if (dead.XpGainersLock != null && dead.XPGainers != null)
            {
                lock (dead.XpGainersLock)
                    gainers = dead.XPGainers.ToArray();
            }

            foreach (KeyValuePair<GameLiving, double> gainer in gainers)
            {
                GameBot bot = gainer.Key as GameBot ??
                              ((gainer.Key as GameNPC)?.Brain as IControlledBrain)?.GetLivingOwner() as GameBot;

                if (bot is not { IsAutonomousWorldBot: true })
                    continue;

                Consider(bot);

                foreach (GameBot mate in bot.Group?.GetMembersInTheGroup().OfType<GameBot>() ?? Enumerable.Empty<GameBot>())
                {
                    if (mate.IsAutonomousWorldBot)
                        Consider(mate);
                }
            }

            return earners;

            void Consider(GameBot bot)
            {
                if (bot == null || !seen.Add(bot) || !bot.IsOnMasterLevelPath || !bot.CanHoldMasterLevels)
                    return;

                if (bot.ObjectState != GameObject.eObjectState.Active || !bot.IsAlive)
                    return;

                if (!bot.IsWithinRadius(dead, WorldMgr.MAX_EXPFORKILL_DISTANCE))
                    return;

                earners.Add(bot);
            }
        }

        /// <summary>Pays <paramref name="earned"/> to every bot that earned the kill. Returns the bots paid.</summary>
        public static List<GameBot> AwardKill(GameNPC dead, DyingEventArgs death, long earned)
        {
            var paid = new List<GameBot>();

            if (earned <= 0)
                return paid;

            foreach (GameBot bot in Earners(dead, death))
            {
                if (bot.MLLevel >= GamePlayer.ML_MAX_LEVEL)
                    continue;

                AddExperience(bot, earned);
                paid.Add(bot);
            }

            return paid;
        }

        #endregion

        #region Abilities the AI uses

        /// <summary>
        /// Which held Master Level abilities a bot casts on its own, and how. Only abilities whose
        /// handlers work with a bot as caster and whose use the brain can judge are listed; every
        /// other ability of the line is held (skills, passive lookups) but never cast by the AI:
        /// summons, traps, storms, fonts and ports need a ground target or are written for a
        /// player caster (GameFont and GameStorm take a GamePlayer owner).
        /// </summary>
        public static BotMasterLevelUse UseOf(Spell spell)
        {
            if (spell == null)
                return BotMasterLevelUse.None;

            return spell.SpellType switch
            {
                // Warlord 5 Guided Strike, 6 Energizing Aura, 10 Warguard.
                eSpellType.Critical or eSpellType.FatigueConsumptionBuff or eSpellType.MLABSBuff => BotMasterLevelUse.FightBuff,
                // Warlord 2 Bolstering Battlecry. Warlord 8 Defending Martyr (damage type 1) costs the
                // caster 90% of its health; a bot never presses that one.
                eSpellType.PBAEHeal when (int)spell.DamageType == 0 => BotMasterLevelUse.HealBurst,
                // Battlemaster 1 Sapping Strike, 3 Power Leak.
                eSpellType.MLEndudrain => BotMasterLevelUse.EnduranceDrain,
                eSpellType.MLManadrain => BotMasterLevelUse.PowerDrain,
                // Perfecter 1 Purging Wave, 2 Purify Vision.
                eSpellType.CureDisease when spell.Target == eSpellTarget.GROUP => BotMasterLevelUse.CureDisease,
                eSpellType.CureNearsight => BotMasterLevelUse.CureNearsight,
                _ => BotMasterLevelUse.None,
            };
        }

        /// <summary>
        /// Chance per think that a bot notices a ready ability whose moment has come. A decent
        /// player pressed these buttons, but not on the first frame and not every time.
        /// </summary>
        public static int ReactionChance(BotMasterLevelUse use) => use switch
        {
            BotMasterLevelUse.FightBuff => 25,
            BotMasterLevelUse.HealBurst => 50,
            BotMasterLevelUse.EnduranceDrain or BotMasterLevelUse.PowerDrain => 20,
            BotMasterLevelUse.CureDisease or BotMasterLevelUse.CureNearsight => 60,
            _ => 0,
        };

        /// <summary>Health below which Bolstering Battlecry is worth it for a nearby group member.</summary>
        public const int HEAL_BURST_HEALTH_PERCENT = 60;

        /// <summary>A drain is wasted on a target with less than this much endurance or power left.</summary>
        public const int DRAIN_MIN_PERCENT = 30;

        /// <summary>True for the targets the drains are meant for: players and bots, not monsters.</summary>
        public static bool IsPlayerLike(GameLiving living) => living is GamePlayer or GameBot;

        #endregion
    }
}
