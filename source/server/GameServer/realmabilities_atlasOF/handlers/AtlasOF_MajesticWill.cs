using System.Collections.Generic;
using DOL.Database;
using DOL.GS.Effects;
using DOL.GS.Spells;

namespace DOL.GS.RealmAbilities
{
    public class AtlasOF_MajesticWill : TimedRealmAbility
    {
        public AtlasOF_MajesticWill(DbAbility dba, int level) : base(dba, level) { }
        
        int m_duration = 60000; // 60s

        // Patch 1.68 rework (24 Feb 2004 hot fix and follow-up): five levels at 1 / 3 / 6 / 10 / 14
        // points, 5 % per level (25 % at level V), 10 minute recast instead of 30.
        public const int Levels = 5;
        public const int ReUseSeconds = 600;
        public const int PercentPerLevel = 5;

        public override int MaxLevel { get { return Levels; } }
        public override int CostForUpgrade(int currentLevel) { return AtlasRAHelpers.GetCommonUpgradeCostFor5LevelsRA(currentLevel); }
        public override int GetReUseDelay(int level) { return ReUseSeconds; } // 10 mins

        /// <summary>
        /// Percentage points added to the caster's spell hit chance (the target's resist chance falls by the same amount).
        /// SpellHandler applies the same value through the effect's effectiveness (RA level times 5).
        /// </summary>
        public int GetAmountForLevel(int level) { return level < 1 ? 0 : AtlasRAHelpers.GetPropertyEnhancer5AmountForLevel(level); }

        public override void Execute(GameLiving living)
        {
            GamePlayer player = living as GamePlayer;
            if (CheckPreconditions(living, DEAD | SITTING | MEZZED | STUNNED)) return;

            DisableSkill(living);

            ECSGameEffectFactory.Create(new(player, m_duration, Level, CreateSpell(living)), static (in i) => new AtlasOF_MajesticWillECSEffect(i));
        }
        
        private SpellHandler CreateSpell(GameLiving owner)
        {
            DbSpell tmpSpell = new DbSpell();
            tmpSpell.Name = "Majestic Will";
            tmpSpell.Icon = 4239;
            tmpSpell.ClientEffect = 7065;
            tmpSpell.Damage = 0;
            tmpSpell.DamageType = 0;
            tmpSpell.Target = eSpellTarget.SELF.ToString();
            tmpSpell.Radius = 0;
            tmpSpell.Type = eSpellType.OffensiveProc.ToString();
            tmpSpell.Value = 0;
            tmpSpell.Duration = 60;
            tmpSpell.Pulse = 0;
            tmpSpell.PulsePower = 0;
            tmpSpell.Power = 0;
            tmpSpell.CastTime = 0;
            tmpSpell.EffectGroup = 0; // stacks with other damage adds
            tmpSpell.Range = 0;
            tmpSpell.Description = "Your targets chance of resisting your spells is reduced by 5% per level of this ability for 60 seconds.";
            SpellLine spellLine = GlobalSpellsLines.RealmSpellsSpellLine;
            return ScriptMgr.CreateSpellHandler(owner, new Spell(tmpSpell, 0) , spellLine) as SpellHandler;
        }

        public override IList<string> DelveInfo
        {
            get
            {
                var delveInfoList = new List<string>();
                delveInfoList.Add("Your targets chance of resisting your spells is reduced by 5% per level of this ability for 60 seconds.");
                delveInfoList.Add("Casting time: instant");

                return delveInfoList;
            }
        }
        
        public override void AddEffectsInfo(IList<string> list)
        {
            list.Add("Your targets chance of resisting your spells is reduced by 5% per level of this ability for 60 seconds.");
            list.Add("Casting time: instant");
        }
    }
}