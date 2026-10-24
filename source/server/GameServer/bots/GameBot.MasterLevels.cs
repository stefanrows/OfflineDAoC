using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// Master Level state of a persistent companion or an autonomous world bot (TOA_PLAN WP2.4).
    ///
    /// <para>A bot is on the path while it holds a discipline. Companions get one when the Arbiter
    /// enrols their owner (BotMasterLevels.EnrolCompanions); world bots roll one when they load at
    /// level 40 or higher. <see cref="MLLevel"/> is the rank, <see cref="MLExperience"/> the bar
    /// toward the next one (32,000 a level, as for players).</para>
    ///
    /// <para>The discipline is kept by name, not by the 0-7 MLLine index: the schema reconciler
    /// fills a new integer column with 0 on old rows, and 0 is Banelord.</para>
    ///
    /// <para>Temporary /spawn helpers and battleground encounter bots never hold Master Levels.</para>
    /// </summary>
    public partial class GameBot
    {
        private string _masterLevelPath;
        private List<Spell> _masterLevelSpells;
        private List<Spell> _masterLevelCombatSpells;

        /// <summary>Master Level experience toward the next Master Level.</summary>
        public long MLExperience { get; set; }

        /// <summary>The discipline (one of MasterLevelProgression.Paths), or null off the path.</summary>
        public string MasterLevelPath => _masterLevelPath;

        public bool IsOnMasterLevelPath => _masterLevelPath != null;

        /// <summary>The discipline's index in MasterLevelProgression.Paths, NO_PATH off the path.</summary>
        public byte MLLine
        {
            get
            {
                int index = MasterLevelProgression.PathIndex(_masterLevelPath);
                return index < 0 ? MasterLevelProgression.NO_PATH : (byte)index;
            }
        }

        /// <summary>Companions and saved world bots; never /spawn helpers or encounter bots.</summary>
        public bool CanHoldMasterLevels =>
            !IsTemporaryGroupHelper && !IsBattlegroundEncounterBot && (IsPersistentPlayerCompanion || IsAutonomousWorldBot);

        /// <summary>The Master Level spells held now: the chosen line, rank up to MLLevel, at character level 50.</summary>
        public IReadOnlyList<Spell> MasterLevelSpells => (IReadOnlyList<Spell>)_masterLevelSpells ?? Array.Empty<Spell>();

        /// <summary>The held Master Level spells the bot casts on its own (see BotMasterLevels.UseOf).</summary>
        public IReadOnlyList<Spell> MasterLevelCombatSpells => (IReadOnlyList<Spell>)_masterLevelCombatSpells ?? Array.Empty<Spell>();

        /// <summary>
        /// Sets the Master Level state. An unknown discipline name puts the bot off the path; ranks
        /// are clamped to 0-10 and Master Level 10 holds no experience.
        /// </summary>
        public void SetMasterLevelState(int level, long experience, string path)
        {
            int index = MasterLevelProgression.PathIndex(path);
            _masterLevelPath = index >= 0 ? MasterLevelProgression.Paths[index].Key : null;
            MLLevel = _masterLevelPath == null ? 0 : Math.Clamp(level, 0, GamePlayer.ML_MAX_LEVEL);
            MLExperience = _masterLevelPath == null || MLLevel >= GamePlayer.ML_MAX_LEVEL ? 0 : Math.Max(0, experience);
        }

        /// <summary>
        /// True if this Master Level line belongs in the bot's specs: on the path, Master Level 1
        /// or higher, and the chosen discipline. The career rows still gate it at level 50.
        /// </summary>
        public bool HoldsActiveMasterLevelLine(string specKey) =>
            _masterLevelPath != null && MLLevel >= 1 &&
            string.Equals(specKey?.Trim(), _masterLevelPath, StringComparison.OrdinalIgnoreCase);

        internal static bool IsMasterLevelLine(SpellLine line) =>
            line != null && (MasterLevelProgression.PathIndex(line.KeyName) >= 0 ||
                             MasterLevelProgression.PathIndex(line.Spec) >= 0);

        public void CopyMasterLevelStateTo(PlayerCompanionRecord record)
        {
            if (record == null)
                return;

            record.MLLevel = MLLevel;
            record.MLExperience = MLExperience;
            record.MLLine = _masterLevelPath ?? string.Empty;
        }

        public void CopyMasterLevelStateTo(OfflineWorldBotRecord record)
        {
            if (record == null)
                return;

            record.MLLevel = MLLevel;
            record.MLExperience = MLExperience;
            record.MLLine = _masterLevelPath ?? string.Empty;
        }

        /// <summary>Loads the saved state of a persistent companion; runs before its specs load.</summary>
        public void RestoreMasterLevelState(PlayerCompanionRecord record)
        {
            if (record != null)
                SetMasterLevelState(record.MLLevel, record.MLExperience, record.MLLine);
        }

        /// <summary>
        /// Loads the saved state of a world bot, or rolls one when a bot of level 40 or higher
        /// has never been on the path. A roll is saved with the next state snapshot, so it is
        /// rolled once per bot, never on every load. Runs before the bot's specs load.
        /// </summary>
        public void RestoreMasterLevelState(OfflineWorldBotRecord record)
        {
            if (record == null)
                return;

            SetMasterLevelState(record.MLLevel, record.MLExperience, record.MLLine);

            if (!IsOnMasterLevelPath && BotMasterLevels.TryRollWorldBot(this, Random.Shared.NextDouble))
            {
                CopyMasterLevelStateTo(record);
                MarkAutonomousStateDirty();
            }
        }

        /// <summary>
        /// Rebuilds the held Master Level spells. Runs with every spell refresh (SetBotSpells), so
        /// a level-up, a new character level or a reload all pick up the chosen line.
        /// </summary>
        private void RefreshMasterLevelSpellList()
        {
            string path = _masterLevelPath;

            if (path == null || MLLevel < 1 || !HasSpecialization(path))
            {
                _masterLevelSpells = null;
                _masterLevelCombatSpells = null;
                return;
            }

            List<Spell> held = SkillBase.GetSpellList(path)
                .Where(spell => spell != null && spell.Level <= MLLevel)
                .OrderBy(spell => spell.Level).ThenBy(spell => spell.ID)
                .ToList();
            _masterLevelSpells = held;
            _masterLevelCombatSpells = held.Where(spell => BotMasterLevels.UseOf(spell) != BotMasterLevelUse.None).ToList();
        }

        /// <summary>
        /// After a Master Level change: the line spec and its spell line follow (LoadClassSpecializations
        /// through RefreshSpecDependantSkills), then the held spells. The class spells are untouched.
        /// </summary>
        public void RefreshMasterLevelSkills()
        {
            RefreshSpecDependantSkills(false);
            RefreshMasterLevelSpellList();
        }

        /// <summary>Removes the spell line of a Master Level line the bot no longer holds.</summary>
        protected void RemoveMasterLevelSpellLines(string specKey)
        {
            lock (m_spellLines)
            {
                m_spellLines.RemoveAll(line => line != null &&
                    (string.Equals(line.KeyName, specKey, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(line.Spec, specKey, StringComparison.OrdinalIgnoreCase)));
            }
        }

        /// <summary>
        /// Saves Master Level progress the way each kind saves its other progress: a companion
        /// through the coalesced companion progress queue, a world bot with its state snapshot.
        /// </summary>
        protected virtual void QueueMasterLevelSave()
        {
            if (IsPersistentPlayerCompanion)
                PlayerCompanionProgressPersistence.Queue(this);
            else if (IsAutonomousWorldBot)
            {
                MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(this);
            }
        }

        public void SaveMasterLevelProgress() => QueueMasterLevelSave();
    }
}
