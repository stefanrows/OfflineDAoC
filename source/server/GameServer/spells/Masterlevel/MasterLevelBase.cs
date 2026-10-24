using System.Collections.Generic;
using DOL.AI;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Effects;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    #region MasterlevelBase
    /// <summary>
    /// Contains all common code for Banelord Spells
    /// </summary>
    public class MasterlevelHandling : SpellHandler
    {
        public override bool HasPositiveEffect
        {
            get { return false; }
        }

        public override bool IsUnPurgeAble
        {
            get { return true; }
        }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        /// <summary>
        /// Calculates the effect duration in milliseconds
        /// </summary>
        protected override int CalculateEffectDuration(GameLiving target)
        {
            return Spell.Duration;
        }

        #region Targets

        public static List<GameLiving> SelectTargets(SpellHandler spellHandler, GameLiving target)
        {
            var list = GameLoop.GetListForTick<GameLiving>();
            Spell spell = spellHandler.Spell;
            GameLiving caster = spellHandler.Caster;

            switch (spell.Target)
            {
                //GTAoE
                case eSpellTarget.AREA:
                {
                    if (spell.Radius > 0)
                    {
                        foreach (GamePlayer player in WorldMgr.GetPlayersCloseToSpot(caster.CurrentRegionID, caster.GroundTarget, (ushort) spell.Radius))
                        {
                            if (GameServer.ServerRules.IsAllowedToAttack(caster, player, true))
                                list.Add(player);
                        }

                        foreach (GameNPC npc in WorldMgr.GetNPCsCloseToSpot(caster.CurrentRegionID, caster.GroundTarget, (ushort) spell.Radius))
                        {
                            if (npc is GameBot bot && GameServer.ServerRules.IsAllowedToAttack(caster, bot, true))
                                list.Add(bot);
                        }
                    }

                    break;
                }
                case eSpellTarget.PET:
                {
                    if (caster is GamePlayer player)
                    {
                        IControlledBrain npc = player.ControlledBrain;

                        if (npc != null)
                            list.Add(npc.Body);
                    }

                    break;
                }
                case eSpellTarget.ENEMY:
                {
                    if (spell.Radius > 0)
                    {
                        target = caster;
                        foreach (GameLiving player in PlayersAndBotsAround(target, (ushort) spell.Radius))
                        {
                            if (GameServer.ServerRules.IsAllowedToAttack(caster, player, true))
                                list.Add(player);
                        }
                    }
                    else
                    {
                        if (target != null && GameServer.ServerRules.IsAllowedToAttack(caster, target, true))
                            list.Add(target);
                    }

                    break;
                }
                case eSpellTarget.REALM:
                {
                    if (spell.Radius > 0)
                    {
                        if (target == null || spell.Range == 0)
                            target = caster;

                        foreach (GameLiving player in PlayersAndBotsAround(target, (ushort) spell.Radius))
                        {
                            if (GameServer.ServerRules.IsAllowedToAttack(caster, player, true) == false)
                                list.Add(player);
                        }
                    }
                    else
                    {
                        if (target != null && GameServer.ServerRules.IsAllowedToAttack(caster, target, true) == false)
                            list.Add(target);
                    }

                    break;
                }
                case eSpellTarget.SELF:
                {
                    if (spell.Radius > 0)
                    {
                        if (target == null || spell.Range == 0)
                            target = caster;

                        foreach (GameLiving player in PlayersAndBotsAround(target, (ushort) spell.Radius))
                        {
                            if (GameServer.ServerRules.IsAllowedToAttack(caster, player, true) == false)
                                list.Add(player);
                        }
                    }
                    else
                        list.Add(caster);

                    break;
                }
                case eSpellTarget.GROUP:
                {
                    Group group = caster.Group;
                    int spellRange = spell.CalculateEffectiveRange(caster);

                    if (spellRange == 0)
                        spellRange = spell.Radius;

                    if (group == null)
                    {
                        list.Add(caster);
                        IControlledBrain npc = caster.ControlledBrain;

                        if (npc != null)
                        {
                            if (caster.IsWithinRadius(npc.Body, spellRange))
                                list.Add(npc.Body);
                        }
                    }
                    else
                    {
                        foreach (GameLiving living in group.GetMembersInTheGroup())
                        {
                            // only players in range
                            if (caster.IsWithinRadius(living, spellRange))
                                list.Add(living);

                            IControlledBrain npc = living.ControlledBrain;

                            if (npc != null)
                            {
                                if (living.IsWithinRadius(npc.Body, spellRange))
                                    list.Add(npc.Body);
                            }
                        }
                    }

                    break;
                }
            }

            return list;
        }

        /// <summary>
        /// Players and bots (companions, autonomous world bots) around a point. Bots stand in
        /// for players everywhere else in this fork (realm points, kill credit, Blanket of
        /// Camouflage), so Master Level areas take them too (TOA_PLAN WP2.4): a Warlord's battle
        /// cry reaches the companions beside him, a Banelord's pulse the enemy realm's bots.
        /// Pets and ordinary NPCs stay out, as before.
        /// </summary>
        public static IEnumerable<GameLiving> PlayersAndBotsAround(GameObject center, ushort radius)
        {
            foreach (GamePlayer player in center.GetPlayersInRadius(radius))
                yield return player;

            foreach (GameNPC npc in center.GetNPCsInRadius(radius))
            {
                if (npc is GameBot bot)
                    yield return bot;
            }
        }

        /// <summary>
        /// Select all targets for this spell
        /// </summary>
        /// <param name="castTarget"></param>
        /// <returns></returns>
        public override List<GameLiving> SelectTargets(GameObject castTarget)
        {
            return SelectTargets(this, castTarget as GameLiving);
        }
        #endregion

        /// <summary>
        /// Current depth of delve info
        /// </summary>
        public override IList<string> DelveInfo
        {
            get
            {
                var list = new List<string>();
                list.Add(ShortDescription);
                return list;
            }
        }

        public MasterlevelHandling(GameLiving caster, Spell spell, SpellLine spellLine) : base(caster, spell, spellLine) { }
    }
    #endregion

    #region Stylhandler
    [SpellHandler(eSpellType.MLStyleHandler)]
    public class MLStyleHandler : MasterlevelHandling
    {
        public MLStyleHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    #endregion

    #region MasterlevelDebuff
    /// <summary>
    /// Contains all common code for Banelord Spells
    /// </summary>
    public abstract class MasterlevelDebuffHandling : SingleStatDebuff
    {
        // bonus category
        public override eBuffBonusCategory BonusCategory1 { get { return eBuffBonusCategory.Debuff; } }

        public override bool HasPositiveEffect
        {
            get { return false; }
        }

        public override bool IsUnPurgeAble
        {
            get { return true; }
        }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        protected override int CalculateEffectDuration(GameLiving target)
        {
            return Spell.Duration;
        }

        #region Targets
        /// <summary>
        /// Select all targets for this spell
        /// </summary>
        /// <param name="castTarget"></param>
        /// <returns></returns>
        public override List<GameLiving> SelectTargets(GameObject castTarget)
        {
            return MasterlevelHandling.SelectTargets(this, castTarget as GameLiving);
        }
        #endregion

        /// <summary>
        /// Current depth of delve info
        /// </summary>
        public override IList<string> DelveInfo
        {
            get
            {
                var list = new List<string>();
                list.Add(ShortDescription);
                return list;
            }
        }

        public MasterlevelDebuffHandling(GameLiving caster, Spell spell, SpellLine spellLine) : base(caster, spell, spellLine) { }
    }
    #endregion

    #region MasterlevelBuff
    /// <summary>
    /// Contains all common code for Banelord Spells
    /// </summary>
    public abstract class MasterlevelBuffHandling : SingleStatBuff
    {
        // bonus category
        public override eBuffBonusCategory BonusCategory1 { get { return eBuffBonusCategory.BaseBuff; } }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        protected override int CalculateEffectDuration(GameLiving target)
        {
            return Spell.Duration;
        }

        #region Targets
        /// <summary>
        /// Select all targets for this spell
        /// </summary>
        /// <param name="castTarget"></param>
        /// <returns></returns>
        public override List<GameLiving> SelectTargets(GameObject castTarget)
        {
            return MasterlevelHandling.SelectTargets(this, castTarget as GameLiving);
        }
        #endregion

        /// <summary>
        /// Current depth of delve info
        /// </summary>
        public override IList<string> DelveInfo
        {
            get
            {
                var list = new List<string>();
                list.Add(ShortDescription);
                return list;
            }
        }

        protected MasterlevelBuffHandling(GameLiving caster, Spell spell, SpellLine spellLine) : base(caster, spell, spellLine) { }
    }
    #endregion

    #region MasterlevelDualBuff
    /// <summary>
    /// Contains all common code for Banelord Spells
    /// </summary>
    public abstract class MasterlevelDualBuffHandling : DualStatBuff
    {
        // bonus category
        public override eBuffBonusCategory BonusCategory1 { get { return eBuffBonusCategory.BaseBuff; } }
        public override eBuffBonusCategory BonusCategory2 { get { return eBuffBonusCategory.SpecBuff; } }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        protected override int CalculateEffectDuration(GameLiving target)
        {
            return Spell.Duration;
        }

        #region Targets
        /// <summary>
        /// Select all targets for this spell
        /// </summary>
        /// <param name="castTarget"></param>
        /// <returns></returns>
        public override List<GameLiving> SelectTargets(GameObject castTarget)
        {
            return MasterlevelHandling.SelectTargets(this, castTarget as GameLiving);
        }
        #endregion

        /// <summary>
        /// Current depth of delve info
        /// </summary>
        public override IList<string> DelveInfo
        {
            get
            {
                var list = new List<string>();
                list.Add(ShortDescription);
                return list;
            }
        }

        protected MasterlevelDualBuffHandling(GameLiving caster, Spell spell, SpellLine spellLine) : base(caster, spell, spellLine) { }
    }
    #endregion

    #region BanelordSnare
    /// <summary>
    /// Contains all common code for Banelord Spells
    /// </summary>
    public class BanelordSnare : UnbreakableSpeedDecreaseSpellHandler
    {
        private const string EFFECT_PROPERTY = "BanelordSnareProperty";

        public override bool HasPositiveEffect
        {
            get { return false; }
        }

        public override bool IsUnPurgeAble
        {
            get { return true; }
        }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
        {
            ECSGameTimer timer = effect.Owner.TempProperties.GetProperty<ECSGameTimer>(EFFECT_PROPERTY);
            effect.Owner.TempProperties.RemoveProperty(EFFECT_PROPERTY);
            timer.Stop();

            effect.Owner.BuffBonusMultCategory1.Remove((int)eProperty.MaxSpeed, effect);

            effect.Owner.OnMaxSpeedChange();
            MessageToLiving(effect.Owner, Spell.Message3, eChatType.CT_SpellExpires);
            Message.SystemToArea(effect.Owner, Util.MakeSentence(Spell.Message4, effect.Owner.GetName(0, true)), eChatType.CT_SpellExpires, effect.Owner);

            return 0;
        }

        protected override int CalculateEffectDuration(GameLiving target)
        {
            return Spell.Duration;
        }

        #region Targets
        /// <summary>
        /// Select all targets for this spell
        /// </summary>
        /// <param name="castTarget"></param>
        /// <returns></returns>
        public override List<GameLiving> SelectTargets(GameObject castTarget)
        {
            return MasterlevelHandling.SelectTargets(this, castTarget as GameLiving);
        }
        #endregion

        /// <summary>
        /// Current depth of delve info
        /// </summary>
        public override IList<string> DelveInfo
        {
            get
            {
                var list = new List<string>();
                list.Add(ShortDescription);
                return list;
            }
        }

        public BanelordSnare(GameLiving caster, Spell spell, SpellLine spellLine) : base(caster, spell, spellLine) { }
    }
    #endregion

    #region Fontbase
    /// <summary>
    /// A font: an object at the caster's feet that pulses its spell on the livings around it,
    /// friends for a friendly font, enemies otherwise (TOA_PLAN WP2.6: an ECS life, any caster).
    /// </summary>
    public class FontSpellHandler : DoTSpellHandler, IMasterLevelPlacement
    {
        protected GameFont font;
        protected DbSpell dbs;
        protected Spell s;
        protected SpellLine sl;
        protected ISpellHandler heal;
        protected bool ApplyOnNPC = false;
        protected bool ApplyOnCombat = false;
        protected bool Friendly = true;
        protected ushort sRadius = 350;

        public GameNPC PlacedObject => font;

        public override bool HasConflictingEffectWith(ISpellHandler compare)
        {
            return false;
        }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            MasterLevelPlacement.Place(this, font, CasterEffectiveness);
        }

        public virtual void PulsePlaced(MasterLevelPlacedECSEffect effect)
        {
            if (heal == null || s == null)
                return;

            foreach (GameLiving living in Around(font, sRadius, ApplyOnNPC))
            {
                if (!living.IsAlive || (living.InCombat && !ApplyOnCombat))
                    continue;

                // A friendly font helps the caster's side only, a hostile one hits its enemies.
                if (IsEnemy(living) == Friendly)
                    continue;

                // A fresh sub-handler for every living: SpellHandler.StartSpell keeps the first
                // target it was ever given, so one shared handler served the first living forever.
                Apply(NewPulseHandler() ?? heal, living);
            }
        }

        /// <summary>The livings a pulse considers; tests replace the world lookup.</summary>
        protected virtual IEnumerable<GameLiving> Around(GameObject center, ushort radius, bool withNpcs) =>
            MasterLevelPlacement.LivingsAround(center, radius, withNpcs);

        protected virtual bool IsEnemy(GameLiving living) => GameServer.ServerRules.IsAllowedToAttack(Caster, living, true);

        protected virtual ISpellHandler NewPulseHandler() => ScriptMgr.CreateSpellHandler(m_caster, s, sl);

        protected virtual void Apply(ISpellHandler handler, GameLiving living) => handler.StartSpell(living);

        public virtual void RemovePlaced() => MasterLevelPlacement.Remove(font);

        // constructor
        public FontSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    #endregion

    #region Trapbase
    /// <summary>
    /// A trap (mine): an object at the caster's feet, hidden from its enemies, that goes off on
    /// the first enemy player or bot that comes within its radius.
    /// </summary>
    public class MineSpellHandler : DoTSpellHandler, IMasterLevelPlacement
    {
        protected GameMine mine;
        protected ISpellHandler trap;
        protected DbSpell dbs;
        protected Spell s;
        protected SpellLine sl;
        protected bool Unstealth = true;
        protected bool DestroyOnEffect = true;
        protected ushort sRadius = 350;

        public GameNPC PlacedObject => mine;

        public override bool HasConflictingEffectWith(ISpellHandler compare)
        {
            return false;
        }

        public virtual void PulsePlaced(MasterLevelPlacedECSEffect effect)
        {
            if (trap == null || s == null)
                return;

            foreach (GameLiving living in MasterLevelPlacement.LivingsAround(mine, sRadius, false))
            {
                if (!living.IsAlive || !GameServer.ServerRules.IsAllowedToAttack(Caster, living, true))
                    continue;

                Spring(effect, living);
                return;
            }
        }

        /// <summary>The trap goes off on <paramref name="living"/>.</summary>
        protected void Spring(MasterLevelPlacedECSEffect effect, GameLiving living)
        {
            // A fresh sub-handler, as for fonts and storms: StartSpell keeps its first target.
            (ScriptMgr.CreateSpellHandler(m_caster, s, sl) ?? trap).StartSpell(living);

            if (Unstealth)
                living.Stealth(false);

            if (DestroyOnEffect)
                effect.End();
        }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            MasterLevelPlacement.Place(this, mine, CasterEffectiveness);
        }

        public virtual void RemovePlaced() => MasterLevelPlacement.Remove(mine);

        // constructor
        public MineSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    #endregion

    #region Stormbase
    /// <summary>
    /// A storm: an object at the caster's feet that pulses its spell on everyone inside, allies
    /// for Dazzling Array, enemies for the others, while the caster stays within 3,000 of it.
    /// Vacuum Vortex moves it, Focusing Winds holds it.
    /// </summary>
    public class StormSpellHandler : SpellHandler, IMasterLevelPlacement
    {
        /// <summary>Beyond this distance from its caster a storm pauses.</summary>
        public const int CasterReach = 3000;

        protected GameStorm storm;
        protected DbSpell dbs;
        protected Spell s;
        protected SpellLine sl;
        protected ISpellHandler tempest;
        protected ushort sRadius = 350;

        public GameNPC PlacedObject => storm;

        /// <summary>Dazzling Array helps the caster's side; every other storm hurts its enemies.</summary>
        public virtual bool Friendly => false;

        public virtual void PulsePlaced(MasterLevelPlacedECSEffect effect)
        {
            if (tempest == null || s == null)
                return;

            if (!storm.IsWithinRadius(Caster, CasterReach))
                return;

            foreach (GameLiving living in MasterLevelPlacement.LivingsAround(storm, sRadius, false))
            {
                if (!living.IsAlive || GameServer.ServerRules.IsAllowedToAttack(Caster, living, true) == Friendly)
                    continue;

                tempest = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
                tempest.StartSpell(living);
            }
        }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            // The storm carries its spell's name (hidden in game), so a storm of each kind is told apart.
            if (storm != null)
            {
                storm.Name = Spell.Name;
                storm.Friendly = Friendly;
            }

            MasterLevelPlacement.Place(this, storm, CasterEffectiveness);
        }

        public virtual void RemovePlaced() => MasterLevelPlacement.Remove(storm);

        // constructor
        public StormSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    #endregion

    #region SummonItemBase
    public class SummonItemSpellHandler : MasterlevelHandling
    {
        protected IList<DbInventoryItem> items;

        /// <summary>
        /// Execute create item spell
        /// </summary>
        /// <param name="target"></param>
        /// 
        public override void FinishSpellCast(GameLiving target)
        {
            m_caster.Mana -= PowerCost(target);
            base.FinishSpellCast(target);
        }

        public override bool HasPositiveEffect
        {
            get { return true; }
        }

        public override void OnDirectEffect(GameLiving target)
        {
            base.OnDirectEffect(target);
            if (target == null || !target.IsAlive)
                return;

            if (target is GamePlayer && items != null)
            {
                GamePlayer targetPlayer = target as GamePlayer;

                foreach (DbInventoryItem item in items)
                {
                    if (targetPlayer.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, item))
                    {

                        InventoryLogging.LogInventoryAction(Caster, targetPlayer, eInventoryActionType.Other, item.Template, item.Count);
                        targetPlayer.Out.SendMessage("Item created: " + item.GetName(0, false), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    }
                }
            }

        }

        public SummonItemSpellHandler(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
            items = new List<DbInventoryItem>();
        }
    }
    #endregion

    #region TargetModifier
    [SpellHandler(eSpellType.TargetModifier)]
    public class TargetModifierSpellHandler : MasterlevelHandling
    {
        public override bool HasPositiveEffect
        {
            get { return true; }
        }
        public TargetModifierSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    #endregion

    #region Passive
    [SpellHandler(eSpellType.PassiveSpell)]
    public class PassiveSpellHandler : MasterlevelHandling
    {
        public override bool CheckBeginCast(GameLiving selectedTarget)
        {
            return false;
        }

        public PassiveSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    #endregion
}

namespace DOL.GS
{
    #region Decoy
    public class GameDecoy : GameNPC
    {
        public GameDecoy() : base(new BlankBrain())
        {
            this.MaxSpeedBase = 0;
        }
        public override void Die(GameObject killer)
        {
            DeleteFromDatabase();
            Delete();
        }
        private GamePlayer m_owner;
        public GamePlayer Owner
        {
            get { return m_owner; }
            set { m_owner = value; }
        }
        public override int MaxHealth
        {
            get { return 1; }
        }
    }
    #endregion

    #region Gamefont
    public class GameFont : GameMovingObject
    {
        public GameFont() : base(new BlankBrain())
        {
            this.Realm = 0;
            this.Level = 1;
            this.MaxSpeedBase = 0;
            this.Flags |= GameNPC.eFlags.DONTSHOWNAME;
            this.Health = this.MaxHealth;
        }

        private GameLiving m_owner;
        /// <summary>The caster: a player or a bot (TOA_PLAN WP2.6).</summary>
        public GameLiving Owner
        {
            get { return m_owner; }
            set { m_owner = value; }
        }
        public override int MaxHealth
        {
            get { int hp=500; if(Name.ToLower().IndexOf("speed")>=0) hp=100; return hp; }
        }
        public virtual int CalculateToHitChance(GameLiving target)
        {
            int spellLevel = m_owner?.Level ?? Level;
            int spellbonus = m_owner?.GetModified(eProperty.SpellLevel) ?? 0;
            spellLevel += spellbonus;
            if (spellLevel > 50)
                spellLevel = 50;
            int hitchance = 85 + ((spellLevel - target.Level) / 2);
            return hitchance;
        }
        public override void TakeDamage(GameObject source, eDamageType damageType, int damageAmount, int criticalAmount)
        {
            if (damageType == eDamageType.Slash || damageType == eDamageType.Crush || damageType == eDamageType.Thrust)
            {
                damageAmount /= 10;
                criticalAmount /= 10;
            }
            else
            {
                damageAmount /= 25;
                criticalAmount /= 25;
            }
            base.TakeDamage(source, damageType, damageAmount, criticalAmount);
        }

        public override void Die(GameObject killer)
        {
            DeleteFromDatabase();
            Delete();
        }
    }
    #endregion

    #region Gametrap
    public class GameMine : GameMovingObject
    {
        public GameLiving Owner { get; set; }

        public GameMine(ABrain defaultBrain) : base(defaultBrain)
        {
            Realm = 0;
            Level = 1;
            Health = MaxHealth;
            MaxSpeedBase = 0;
        }

        public override void TakeDamage(GameObject source, eDamageType damageType, int damageAmount, int criticalAmount) { }
    }
    #endregion

    #region GameStorm
    public class GameStorm : GameMovingObject
    {
        public GameStorm() : base(new BlankBrain())
        {
            this.Realm = 0;
            this.Level = 60;
            this.MaxSpeedBase = 191;
            this.Model = 3457;
            this.Name = "Storm";
            this.Flags |= GameNPC.eFlags.DONTSHOWNAME;
            this.Flags |= GameNPC.eFlags.CANTTARGET;
            this.Movable = true;
        }

        private GameLiving m_owner;
        /// <summary>The caster: a player or a bot (TOA_PLAN WP2.6).</summary>
        public GameLiving Owner
        {
            get { return m_owner; }
            set { m_owner = value; }
        }

        /// <summary>A storm that helps its caster's side (Dazzling Array) rather than hurting enemies.</summary>
        public bool Friendly { get; set; }

        private bool m_movable;
        public bool Movable
        {
            get { return m_movable; }
            set { m_movable = value; }
        }

        public override int MaxHealth
        { 
            get { return 10000; } 
        }

        public override void Die(GameObject killer)
        {
            DeleteFromDatabase();
            Delete();
        }
        public virtual int CalculateToHitChance(GameLiving target)
        {
            int spellLevel = m_owner?.Level ?? Level;
            int spellbonus = m_owner?.GetModified(eProperty.SpellLevel) ?? 0;
            spellLevel += spellbonus;
            if (spellLevel > 50)
                spellLevel = 50;
            int hitchance = 85 + ((spellLevel - target.Level) / 2);
            return hitchance;
        }
        public override void TakeDamage(GameObject source, eDamageType damageType, int damageAmount, int criticalAmount)
        {
        }
    }
    #endregion
}
