using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>How a discipline choice at the Arbiter ended.</summary>
    public enum MasterLevelChoice
    {
        /// <summary>The whisper was not one of the eight discipline names.</summary>
        NotAPath,
        /// <summary>The player has not begun the trials.</summary>
        NotEnrolled,
        /// <summary>The player's class career has no such Master Level specialisation.</summary>
        NotInCareer,
        /// <summary>The player already walks this discipline.</summary>
        AlreadyWalked,
        /// <summary>The player walks another discipline; a Star of Destiny frees the choice.</summary>
        OtherPathTaken,
        /// <summary>The discipline was taken up.</summary>
        Chosen,
    }

    /// <summary>
    /// Master Level progression (Trials of Atlantis, TOA_PLAN WP2.2), adapted from gaheris
    /// MasterLevels.cs. The live trials exist nowhere, so Master Level experience comes from
    /// killing any non-pet NPC of level 45 or higher, anywhere (owner rule 2026-10-24): every
    /// player who earned the kill gets mob level x <c>ml_xp_per_level</c>. A Master Level costs
    /// 32,000 and is granted in the field as soon as the bar is full.
    ///
    /// <para><see cref="GamePlayer.MLLine"/> is the discipline's position in <see cref="Paths"/>,
    /// not its position in the class career: the career is a dictionary built from database row
    /// order, so a career index could point at another discipline after a data reload.
    /// <see cref="NO_PATH"/> means "none chosen".</para>
    ///
    /// Companions (GameBot) never earn Master Levels: the Dying event's PlayerKillers holds real
    /// players only, and GameBot.LoadClassSpecializations skips every Master Level line.
    /// </summary>
    public static class MasterLevelProgression
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(typeof(MasterLevelProgression));

        /// <summary>MLLine value for "no discipline chosen". Past the end of <see cref="Paths"/>.</summary>
        public const byte NO_PATH = 255;

        /// <summary>The lowest NPC level whose death pays Master Level experience.</summary>
        public const int WORTHY_LEVEL = 45;

        /// <summary>The lowest character level the Arbiter enrols (live: level 40 to begin the trials).</summary>
        public const int ENROL_MIN_LEVEL = 40;

        /// <summary>How far a group member may stand from the Arbiter and still be enrolled.</summary>
        public const int ENROL_RADIUS = 2000;

        [ServerProperty("progression", "ml_xp_per_level",
            "Master Level experience per level of a killed NPC of level 45 or higher, paid to every player who earned the kill. " +
            "A Master Level costs 32,000, so 10 means about 53 kills of a level 60 creature per Master Level. 0 turns kill experience off.", 10)]
        public static int ML_XP_PER_LEVEL = 10;

        /// <summary>The eight disciplines in MLLine order, with the Arbiter's one-line description.</summary>
        public static readonly (string Key, string What)[] Paths =
        {
            ("Banelord",     "curses, snares and the unmaking of a foe's defences"),
            ("Battlemaster", "the close fight: footwork, guard and the killing blow"),
            ("Convoker",     "summoning: wards, weapons and servants called from nothing"),
            ("Perfecter",    "mending: healing, cures and calling the fallen back"),
            ("Sojourner",    "movement, reach and passage others cannot take"),
            ("Spymaster",    "stealth, poison and the knife nobody sees"),
            ("Stormlord",    "the sky turned against them: storms, roots and ruin"),
            ("Warlord",      "holding a line, and keeping those behind it alive"),
        };

        private static readonly object ExperienceLock = new();

        #region Paths

        /// <summary>MLLine index of a discipline name, or -1 if it is none of the eight.</summary>
        public static int PathIndex(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return -1;

            for (int i = 0; i < Paths.Length; i++)
            {
                if (Paths[i].Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        /// <summary>True for a Master Level specialisation, by type or by one of the eight names.</summary>
        public static bool IsMasterLevelSpec(Specialization spec)
        {
            return spec != null && (spec is IMasterLevelsSpecialization || PathIndex(spec.KeyName) >= 0);
        }

        /// <summary>
        /// True if this Master Level line belongs in the player's skills now: on the path, Master
        /// Level 1 or higher, and the chosen discipline. Every other Master Level line stays out
        /// of the spec list, the skill window and the trainer.
        /// </summary>
        public static bool IsActiveLine(GamePlayer player, string specKey)
        {
            if (player == null || !player.MLGranted || player.MLLevel < 1)
                return false;

            int index = PathIndex(specKey);
            return index >= 0 && index == player.MLLine;
        }

        /// <summary>The discipline the player walks, or null if none is chosen.</summary>
        public static string PathOf(GamePlayer player)
        {
            if (player == null || !player.MLGranted || player.MLLevel < 1)
                return null;

            return player.MLLine < Paths.Length ? Paths[player.MLLine].Key : null;
        }

        /// <summary>The Master Level lines in this class's career (gaheris file 39 gives every class all eight).</summary>
        public static HashSet<string> CareerPaths(int classId)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Specialization spec in SkillBase.GetSpecializationCareer(classId).Keys)
            {
                if (IsMasterLevelSpec(spec))
                    result.Add(spec.KeyName);
            }

            return result;
        }

        #endregion

        #region Enrolment, choice, respec

        /// <summary>
        /// Puts one player on the path: MLGranted, Master Level 1, no discipline yet. False if the
        /// player is already enrolled, below <see cref="ENROL_MIN_LEVEL"/> (unless forced) or missing.
        /// </summary>
        public static bool Enrol(GamePlayer player, bool ignoreLevel = false)
        {
            if (player == null || player.MLGranted)
                return false;

            if (!ignoreLevel && player.Level < ENROL_MIN_LEVEL)
                return false;

            player.MLGranted = true;

            if (player.MLLevel < 1)
                player.MLLevel = 1;

            player.MLLine = NO_PATH;

            if (player.MLExperience < 0)
                player.MLExperience = 0;

            player.SaveIntoDatabase();
            player.Out?.SendMessage("You have begun the Trials of Atlantis. Choose your discipline at the Arbiter.",
                eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            Announce(player);
            return true;
        }

        /// <summary>
        /// Enrols the speaker and every group member within <see cref="ENROL_RADIUS"/> of the
        /// Arbiter. Returns the players this changed. Companions are not players and are skipped.
        /// </summary>
        public static List<GamePlayer> EnrolCompany(GamePlayer speaker, GameObject arbiter)
        {
            var enrolled = new List<GamePlayer>();

            if (speaker == null)
                return enrolled;

            if (Enrol(speaker))
                enrolled.Add(speaker);

            if (speaker.Group == null)
                return enrolled;

            foreach (GamePlayer mate in speaker.Group.GetPlayersInTheGroup().ToList())
            {
                if (mate == null || mate == speaker || mate.ObjectState != GameObject.eObjectState.Active)
                    continue;

                if (arbiter != null && !mate.IsWithinRadius(arbiter, ENROL_RADIUS))
                    continue;

                if (Enrol(mate))
                    enrolled.Add(mate);
            }

            return enrolled;
        }

        /// <summary>
        /// Takes up a discipline. Only while none is chosen: once a discipline is walked, a Star
        /// of Destiny from Demyphon has to free the choice first.
        /// </summary>
        public static MasterLevelChoice Choose(GamePlayer player, string said)
        {
            int index = PathIndex(said);

            if (index < 0)
                return MasterLevelChoice.NotAPath;

            if (player == null || !player.MLGranted)
                return MasterLevelChoice.NotEnrolled;

            string wanted = Paths[index].Key;
            string current = PathOf(player);

            if (current != null)
                return current == wanted ? MasterLevelChoice.AlreadyWalked : MasterLevelChoice.OtherPathTaken;

            if (!CareerPaths(player.CharacterClass.ID).Contains(wanted))
                return MasterLevelChoice.NotInCareer;

            player.MLLine = (byte)index;

            if (player.MLLevel < 1)
                player.MLLevel = 1;

            player.SaveIntoDatabase();
            player.Out?.SendMessage($"You have taken up the path of the {wanted}.",
                eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            Announce(player);
            return MasterLevelChoice.Chosen;
        }

        /// <summary>Sets the discipline aside (Star of Destiny). Master Levels and experience stay.</summary>
        public static bool Respec(GamePlayer player)
        {
            if (PathOf(player) == null)
                return false;

            player.MLLine = NO_PATH;
            player.SaveIntoDatabase();
            Announce(player);
            player.Out?.SendMessage("Your Master Level discipline has been set aside. Speak to the Arbiter to choose again.",
                eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            return true;
        }

        /// <summary>
        /// Raises the player one Master Level (credit token). Experience toward the next level
        /// is kept. False when not enrolled or already at the maximum.
        /// </summary>
        public static bool GrantLevel(GamePlayer player)
        {
            if (player == null || !player.MLGranted || player.MLLevel >= GamePlayer.ML_MAX_LEVEL)
                return false;

            lock (ExperienceLock)
            {
                player.MLLevel++;

                if (player.MLLevel >= GamePlayer.ML_MAX_LEVEL)
                    player.MLExperience = 0;
            }

            LevelReached(player, player.MLLevel);
            AddExperience(player, 0);
            return true;
        }

        #endregion

        #region Experience

        /// <summary>
        /// Adds Master Level experience and grants every Master Level it pays for, with a message
        /// and a skill refresh. Negative amounts take experience away but never a level. Returns
        /// the number of levels gained.
        /// </summary>
        public static int AddExperience(GamePlayer player, long amount)
        {
            if (player == null || !player.MLGranted)
                return 0;

            int from;
            int gained = 0;

            lock (ExperienceLock)
            {
                from = player.MLLevel;

                if (player.MLLevel >= GamePlayer.ML_MAX_LEVEL)
                {
                    player.MLExperience = 0;
                    return 0;
                }

                long xp = Math.Max(0, player.MLExperience + amount);

                while (player.MLLevel < GamePlayer.ML_MAX_LEVEL)
                {
                    long needed = player.GetMLExperienceForLevel(player.MLLevel + 1);

                    if (needed <= 0 || xp < needed)
                        break;

                    xp -= needed;
                    player.MLLevel++;
                    gained++;
                }

                if (player.MLLevel >= GamePlayer.ML_MAX_LEVEL)
                    xp = 0;

                player.MLExperience = xp;
            }

            for (int level = from + 1; level <= from + gained; level++)
                LevelReached(player, level, refresh: level == from + gained);

            return gained;
        }

        private static void LevelReached(GamePlayer player, int level, bool refresh = true)
        {
            player.Out?.SendMessage($"You have reached Master Level {level}!",
                eChatType.CT_Important, eChatLoc.CL_SystemWindow);

            if (!refresh)
                return;

            if (PathOf(player) == null)
                player.Out?.SendMessage("Choose a discipline at the Arbiter to use what you have learned.",
                    eChatType.CT_Important, eChatLoc.CL_SystemWindow);

            player.SaveIntoDatabase();
            Announce(player);
        }

        /// <summary>
        /// Master Level experience a kill of this NPC pays, or 0: anything below level 45,
        /// GameBot victims, and every NPC the normal kill rewards refuse (RewardStatus: controlled
        /// pets and companions, recently charmed pets, Battle Warders, moving objects) pay nothing.
        /// </summary>
        public static long KillValue(GameNPC dead)
        {
            if (dead == null || ML_XP_PER_LEVEL <= 0 || dead.Level < WORTHY_LEVEL)
                return 0;

            if (dead is GameBot || dead.Brain is IControlledBrain)
                return 0;

            // Same gate as AbstractServerRules.OnNpcKilled.
            if (dead.RewardStatus != GameNPC.RewardEligibility.Eligible)
                return 0;

            return (long)dead.Level * ML_XP_PER_LEVEL;
        }

        /// <summary>Every enrolled player who earned this kill: alive, active, in the region and in range.</summary>
        public static IEnumerable<GamePlayer> Earners(GameNPC dead, DyingEventArgs death)
        {
            var seen = new HashSet<GamePlayer>();
            IEnumerable<GamePlayer> candidates = death?.PlayerKillers is { Count: > 0 } killers
                ? killers
                : death?.Killer is GamePlayer killer ? new[] { killer } : Array.Empty<GamePlayer>();

            foreach (GamePlayer player in candidates)
            {
                if (player == null || !seen.Add(player) || !player.MLGranted)
                    continue;

                if (player.ObjectState != GameObject.eObjectState.Active || !player.IsAlive)
                    continue;

                if (!player.IsWithinRadius(dead, WorldMgr.MAX_EXPFORKILL_DISTANCE))
                    continue;

                yield return player;
            }
        }

        /// <summary>Pays the kill to every earner. Returns the players paid.</summary>
        public static List<GamePlayer> AwardKill(GameNPC dead, DyingEventArgs death)
        {
            var paid = new List<GamePlayer>();
            long earned = KillValue(dead);

            if (earned <= 0)
                return paid;

            foreach (GamePlayer player in Earners(dead, death).ToList())
            {
                if (player.MLLevel >= GamePlayer.ML_MAX_LEVEL)
                    continue;

                player.Out?.SendMessage($"You gain {earned} Master Level experience.",
                    eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                AddExperience(player, earned);
                paid.Add(player);
            }

            return paid;
        }

        #endregion

        #region Client refresh and events

        /// <summary>
        /// Recomputes the Master Level line and pushes skills, points and the Master Level window
        /// to the client. RefreshSpecDependantSkills alone changes server state only.
        /// </summary>
        public static void Announce(GamePlayer player)
        {
            if (player == null)
                return;

            player.RefreshSpecDependantSkills(false);

            // MasterlevelTitle adds or drops the discipline title on this event, so a
            // choice, a respec or a GM change shows without a relog.
            player.Notify(GamePlayerEvent.BecomeML, player);

            if (player.Out == null)
                return;

            player.Out.SendUpdatePlayerSkills(true);
            player.Out.SendUpdatePlayer();
            player.Out.SendUpdatePoints();

            if (player.MLGranted)
                player.Out.SendMasterLevelWindow((byte)player.MLLevel);
        }

        /// <summary>The Master Level spells the player holds now, from the chosen line.</summary>
        public static List<Spell> Learned(GamePlayer player)
        {
            string path = PathOf(player);

            if (path == null)
                return new List<Spell>();

            return SkillBase.GetSpellList(path).Where(spell => spell != null && spell.Level <= player.MLLevel)
                .OrderBy(spell => spell.Level).ToList();
        }

        /// <summary>One status block for the Arbiter and /ml.</summary>
        public static string Describe(GamePlayer player)
        {
            if (player == null || !player.MLGranted)
                return "You have not begun the Trials of Atlantis. The Arbiter enrols adventurers of level " +
                       ENROL_MIN_LEVEL + " and higher, together with their group.";

            string path = PathOf(player);
            string text = "Master Level " + player.MLLevel + " of " + GamePlayer.ML_MAX_LEVEL + ".";

            if (player.MLLevel < GamePlayer.ML_MAX_LEVEL)
            {
                long needed = player.GetMLExperienceForLevel(player.MLLevel + 1);
                long have = player.MLExperience;
                int percent = needed > 0 ? (int)Math.Min(100, 100L * have / needed) : 0;
                text += $"\nExperience toward Master Level {player.MLLevel + 1}: {have:N0} of {needed:N0} ({percent}%).";

                long perKill = ML_XP_PER_LEVEL > 0 ? (long)WORTHY_LEVEL * ML_XP_PER_LEVEL : 0;
                text += perKill > 0
                    ? $"\nEvery creature of level {WORTHY_LEVEL} or higher you help to kill pays its level x {ML_XP_PER_LEVEL} ({perKill} at level {WORTHY_LEVEL})."
                    : "\nKills pay no Master Level experience on this server; Demyphon sells credits for bounty points.";
            }

            if (path == null)
            {
                text += "\nDiscipline: none chosen. Name one to the Arbiter.";
                return text;
            }

            text += "\nDiscipline: " + path + ".";

            if (player.Level < 50)
                text += " Its arts open to you at level 50.";

            List<Spell> known = Learned(player);
            text += known.Count == 0
                ? "\nYou hold none of its arts yet."
                : "\nYou hold: " + string.Join(", ", known.Select(spell => $"{spell.Name} (ML {spell.Level})")) + ".";

            return text;
        }

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs arguments)
        {
            GameEventMgr.AddHandler(GameLivingEvent.Dying, new DOLEventHandler(OnDying));
            GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnGameEntered));
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs arguments)
        {
            GameEventMgr.RemoveHandler(GameLivingEvent.Dying, new DOLEventHandler(OnDying));
            GameEventMgr.RemoveHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnGameEntered));
        }

        /// <summary>Global Dying listener: pays Master Level experience for worthy NPC kills.</summary>
        public static void OnDying(DOLEvent e, object sender, EventArgs arguments)
        {
            if (sender is not GameNPC dead || arguments is not DyingEventArgs death || KillValue(dead) <= 0)
                return;

            try
            {
                AwardKill(dead, death);
            }
            catch (Exception ex)
            {
                // A Master Level is never worth losing a kill over.
                if (log.IsErrorEnabled)
                    log.Error($"Master Level experience for the death of {dead.Name} failed", ex);
            }
        }

        private static void OnGameEntered(DOLEvent e, object sender, EventArgs arguments)
        {
            if (sender is GamePlayer player && player.MLGranted)
                Announce(player);
        }

        #endregion
    }
}
