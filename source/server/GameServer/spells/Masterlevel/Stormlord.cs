using System;
using System.Collections.Generic;
using DOL.Database;
using DOL.Events;
using DOL.GS.Effects;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    //http://www.camelotherald.com/masterlevels/ma.php?ml=Stormlord
    //shared timer 1
    #region Stormlord-1
    [SpellHandler(eSpellType.DazzlingArray)]
    public class DazzlingArraySpellHandler : StormSpellHandler
    {
        public override bool Friendly => true;

        // constructor
        public DazzlingArraySpellHandler(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
            //Construct a new storm.
            storm = new GameStorm();
            storm.Realm = caster.Realm;
            storm.X = caster.X;
            storm.Y = caster.Y;
            storm.Z = caster.Z;
            storm.CurrentRegionID = caster.CurrentRegionID;
            storm.Heading = caster.Heading;
            storm.Owner = caster;
            storm.Movable = true;

            // Construct the storm spell
            dbs = new DbSpell();
            dbs.SpellID = spell.ID; // one identity per storm kind, so a pulse refreshes its own effect
            dbs.Name = spell.Name;
            dbs.Icon = 7210;
            dbs.ClientEffect = 7210;
            dbs.Damage = spell.Damage;
            dbs.DamageType = (int)spell.DamageType;
            dbs.Target = eSpellTarget.REALM.ToString();
            dbs.Radius = 0;
            dbs.Type = eSpellType.StormMissHit.ToString();
            dbs.Value = spell.Value;
            dbs.Duration = spell.ResurrectHealth; // should be 4
            dbs.Frequency = spell.ResurrectMana;
            dbs.Pulse = 0;
            dbs.PulsePower = 0;
            dbs.LifeDrainReturn = spell.LifeDrainReturn;
            dbs.Power = 0;
            dbs.CastTime = 0;
            dbs.Range = WorldMgr.VISIBILITY_DISTANCE;
            sRadius = 350;
            s = new Spell(dbs, 1);
            sl = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
            tempest = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
        }
    }
    [SpellHandler(eSpellType.StormMissHit)]
    public class StormMissHit : MasterlevelBuffHandling
    {
        public override eProperty Property1 { get { return eProperty.MissHit; } }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (target == null || !target.IsAlive || target.ObjectState != GameLiving.eObjectState.Active) return;
            bool fresh = !StormPulse.Holds(target, this);
            base.ApplyEffectOnTarget(target);

            if (fresh)
                (target as GamePlayer)?.Out?.SendMessage("You're harder to hit!", eChatType.CT_YouWereHit, eChatLoc.CL_SystemWindow);
        }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams) =>
            ECSGameEffectFactory.Create(initParams, static (in ECSGameEffectInitParams i) => new MasterLevelStormStatECSEffect(i));

        // A second longer than the storm's pulse: refreshed while inside, gone a few seconds after leaving.
        protected override int CalculateEffectDuration(GameLiving target) => Spell.Duration + StormPulse.Overlap;

        public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
        {

            effect.Owner.EffectList.Remove(effect);
            return base.OnEffectExpires(effect, noMessages);
        }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        // constructor
        public StormMissHit(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    #endregion

    //no shared timer
    #region Stormlord-2
    [SpellHandler(eSpellType.VacuumVortex)]
    public class VacuumVortexSpellHandler : SpellHandler
    {
        /// <summary>How far a storm is pushed: a short drift, not a teleport.</summary>
        public const int MinPush = 300;
        public const int MaxPush = 750;

        public override bool HasPositiveEffect => true;

        /// <summary>
        /// Execute direct damage spell
        /// </summary>
        /// <param name="target"></param>
        public override void FinishSpellCast(GameLiving target)
        {
            m_caster.Mana -= PowerCost(target);
            base.FinishSpellCast(target);
        }

        /// <summary>
        /// Every storm near the ground target, friendly or not: "pushes any storms near the
        /// ground target directly away from that point" (Herald). It used to take only the
        /// caster's own realm's storms around the caster.
        /// </summary>
        public override List<GameLiving> SelectTargets(GameObject CasterTarget)
        {
            var list = GameLoop.GetListForTick<GameLiving>();

            if (!Caster.GroundTarget.IsValid)
                return list;

            ushort radius = (ushort)(Spell.Radius > 0 ? Spell.Radius : 350);

            foreach (GameNPC npc in WorldMgr.GetNPCsCloseToSpot(Caster.CurrentRegionID, Caster.GroundTarget, radius))
            {
                if (npc is GameStorm)
                    list.Add(npc);
            }

            return list;
        }

        /// <summary>Where a storm at <paramref name="storm"/> goes when pushed from <paramref name="from"/>.</summary>
        public static Point3D PushedTo(Point3D storm, Point3D from, int distance, double fallbackAngle)
        {
            double dx = storm.X - from.X, dy = storm.Y - from.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double angle = length < 1 ? fallbackAngle : Math.Atan2(dy, dx);
            return new Point3D(storm.X + (int)Math.Round(distance * Math.Cos(angle)),
                               storm.Y + (int)Math.Round(distance * Math.Sin(angle)), storm.Z);
        }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        // Runs once per storm the spell selected; it used to push every storm once for each.
        public override void OnDirectEffect(GameLiving target)
        {
            if (target is not GameStorm targetStorm || !targetStorm.Movable || !Caster.GroundTarget.IsValid)
                return;

            Point3D destination = PushedTo(new Point3D(targetStorm.X, targetStorm.Y, targetStorm.Z), Caster.GroundTarget,
                Util.Random(MinPush, MaxPush), Util.RandomDouble() * 2 * Math.PI);
            targetStorm.WalkTo(BotMasterLevelCasting.OnFloor(targetStorm, destination) ?? destination, targetStorm.MaxSpeed);
        }

        public VacuumVortexSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    #endregion

    //shared timer 2
    #region Stormlord-3
    [SpellHandler(eSpellType.EnervatingGas)]
    public class EnervatingGasSpellHandler : StormSpellHandler
    {
        // constructor
        public EnervatingGasSpellHandler(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
            //Construct a new storm.
            storm = new GameStorm();
            storm.Realm = caster.Realm;
            storm.X = caster.X;
            storm.Y = caster.Y;
            storm.Z = caster.Z;
            storm.CurrentRegionID = caster.CurrentRegionID;
            storm.Heading = caster.Heading;
            storm.Owner = caster;
            storm.Movable = true;



            // Construct the storm spell
            dbs = new DbSpell();
            dbs.SpellID = spell.ID; // one identity per storm kind, so a pulse refreshes its own effect
            dbs.Name = spell.Name;
            dbs.Icon = 7273;
            dbs.ClientEffect = 7273;
            dbs.Damage = Math.Abs(spell.Damage);
            dbs.DamageType = (int)spell.DamageType;
            dbs.Target = eSpellTarget.ENEMY.ToString();
            dbs.Radius = 0;
            dbs.Type = eSpellType.StormEnduDrain.ToString();
            dbs.Value = spell.Value;
            dbs.Duration = spell.ResurrectHealth; //should be 2
            dbs.Frequency = spell.ResurrectMana;
            dbs.Pulse = 0;
            dbs.PulsePower = 0;
            dbs.LifeDrainReturn = spell.LifeDrainReturn;
            dbs.Power = 0;
            dbs.CastTime = 0;
            dbs.Range = WorldMgr.VISIBILITY_DISTANCE;
            sRadius = 350;
            s = new Spell(dbs, 1);
            sl = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
            tempest = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
        }
    }
    [SpellHandler(eSpellType.StormEnduDrain)]
    public class StormEndudrain : SpellHandler
    {

        public StormEndudrain(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (target == null) return;
            if (!target.IsAlive || target.ObjectState != GameLiving.eObjectState.Active) return;
            //spell damage should 25;
            int end = (int)(Spell.Damage);
            target.ChangeEndurance(target, eEnduranceChangeType.Spell, (-end));

            (target as GamePlayer)?.Out?.SendMessage(" You lose " + end + " endurance!", eChatType.CT_YouWereHit, eChatLoc.CL_SystemWindow);
            MasterLevelPlacement.TellCaster(m_caster, target.Name + " loses " + end + " endurance!", eChatType.CT_YouWereHit);
            StormPulse.CountAsAttack(this, target);
        }

        public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
        {

            effect.Owner.EffectList.Remove(effect);
            return base.OnEffectExpires(effect, noMessages);
        }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }
    }
    #endregion

    //shared timer 1
    #region Stormlord-4
    [SpellHandler(eSpellType.InebriatingFumes)]
    public class InebriatingFumesSpellHandler : StormSpellHandler
    {
        // constructor
        public InebriatingFumesSpellHandler(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
            //Construct a new storm.
            storm = new GameStorm();
            storm.Realm = caster.Realm;
            storm.X = caster.X;
            storm.Y = caster.Y;
            storm.Z = caster.Z;
            storm.CurrentRegionID = caster.CurrentRegionID;
            storm.Heading = caster.Heading;
            storm.Owner = caster;
            storm.Movable = true;

            // Construct the storm spell
            dbs = new DbSpell();
            dbs.SpellID = spell.ID; // one identity per storm kind, so a pulse refreshes its own effect
            dbs.Name = spell.Name;
            dbs.Icon = 7258;
            dbs.ClientEffect = 7258;
            dbs.Damage = spell.Damage;
            dbs.DamageType = (int)spell.Damage;
            dbs.Target = eSpellTarget.ENEMY.ToString();
            dbs.Radius = 0;
            dbs.Type = eSpellType.StormDexQuickDebuff.ToString();
            dbs.Value = spell.Value;
            dbs.Duration = spell.ResurrectHealth; // should be 2
            dbs.Frequency = spell.ResurrectMana;
            dbs.Pulse = 0;
            dbs.PulsePower = 0;
            dbs.LifeDrainReturn = spell.LifeDrainReturn;
            dbs.Power = 0;
            dbs.CastTime = 0;
            dbs.Range = WorldMgr.VISIBILITY_DISTANCE;
            sRadius = 350;
            s = new Spell(dbs, 1);
            sl = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
            tempest = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
        }
    }
    /// <summary>
    /// Dex/Qui stat specline debuff
    /// </summary>
    [SpellHandler(eSpellType.StormDexQuickDebuff)]
    public class StormDexQuickDebuff : DualStatDebuff
    {
        public override eProperty Property1 { get { return eProperty.Dexterity; } }
        public override eProperty Property2 { get { return eProperty.Quickness; } }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (target == null || !target.IsAlive || target.ObjectState != GameLiving.eObjectState.Active) return;
            bool fresh = !StormPulse.Holds(target, this);
            base.ApplyEffectOnTarget(target);

            if (fresh)
                (target as GamePlayer)?.Out?.SendMessage("Your dexterity and quickness decreased!", eChatType.CT_YouWereHit, eChatLoc.CL_SystemWindow);
        }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams) =>
            ECSGameEffectFactory.Create(initParams, static (in ECSGameEffectInitParams i) => new MasterLevelStormStatECSEffect(i));

        // A second longer than the storm's pulse: refreshed while inside, gone a few seconds after leaving.
        protected override int CalculateEffectDuration(GameLiving target) => Spell.Duration + StormPulse.Overlap;

        public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
        {

            effect.Owner.EffectList.Remove(effect);
            return base.OnEffectExpires(effect, noMessages);
        }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        // constructor
        public StormDexQuickDebuff(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    #endregion

    //shared timer 2
    #region Stormlord-5
    [SpellHandler(eSpellType.MentalSiphon)]
    public class MentalSiphonSpellHandler : StormSpellHandler
    {
        // constructor
        public MentalSiphonSpellHandler(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
            //Construct a new storm.
            storm = new GameStorm();
            storm.Realm = caster.Realm;
            storm.X = caster.X;
            storm.Y = caster.Y;
            storm.Z = caster.Z;
            storm.CurrentRegionID = caster.CurrentRegionID;
            storm.Heading = caster.Heading;
            storm.Owner = caster;
            storm.Movable = true;

            // Construct the storm spell
            dbs = new DbSpell();
            dbs.SpellID = spell.ID; // one identity per storm kind, so a pulse refreshes its own effect
            dbs.Name = spell.Name;
            dbs.Icon = 7303;
            dbs.ClientEffect = 7303;
            dbs.Damage = Math.Abs(spell.Damage);
            dbs.DamageType = (int)spell.DamageType;
            dbs.Target = eSpellTarget.ENEMY.ToString();
            dbs.Radius = 0;
            dbs.Type = eSpellType.PowerDrainStorm.ToString();
            dbs.Value = spell.Value;
            dbs.Duration = spell.ResurrectHealth; // should be 2
            dbs.Frequency = spell.ResurrectMana;
            dbs.Pulse = 0;
            dbs.PulsePower = 0;
            dbs.LifeDrainReturn = spell.LifeDrainReturn;
            dbs.Power = 0;
            dbs.CastTime = 0;
            dbs.Range = WorldMgr.VISIBILITY_DISTANCE;
            sRadius = 350;
            s = new Spell(dbs, 1);
            sl = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
            tempest = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
        }
    }

    [SpellHandler(eSpellType.PowerDrainStorm)]
    public class PowerDrainStormSpellHandler : SpellHandler
    {
        public PowerDrainStormSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (target == null) return;
            if (!target.IsAlive || target.ObjectState != GameLiving.eObjectState.Active) return;
            int mana = (int)(Spell.Damage);
            target.ChangeMana(target, eManaChangeType.Spell, (-mana));

            (target as GamePlayer)?.Out?.SendMessage(m_caster.Name + " steals you " + mana + " points of power!", eChatType.CT_YouWereHit, eChatLoc.CL_SystemWindow);

            StealMana(target, mana);
            StormPulse.CountAsAttack(this, target);
            // target.StartInterruptTimer(SPELL_INTERRUPT_DURATION, AttackData.eAttackType.Spell, Caster);
        }


        public virtual void StealMana(GameLiving target, int mana)
        {
            if (!m_caster.IsAlive) return;
            m_caster.ChangeMana(target, eManaChangeType.Spell, mana);
            SendCasterMessage(target, mana);

        }


        public virtual void SendCasterMessage(GameLiving target, int mana)
        {
            MessageToCaster(string.Format("You steal {0} for {1} power!", target.Name, mana), eChatType.CT_YouHit);
            if (mana > 0)
            {
                MessageToCaster("You steal " + mana + " power points" + (mana == 1 ? "." : "s."), eChatType.CT_Spell);
            }
            //else
            //{
            //   MessageToCaster("You cannot absorb any more power.", eChatType.CT_SpellResisted);
            //}
        }
        public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
        {
            effect.Owner.EffectList.Remove(effect);
            return base.OnEffectExpires(effect, noMessages);
        }
    }

    #endregion

    //no shared timer
    #region Stormlord-6
    [SpellHandler(eSpellType.FocusingWinds)]
    public class FocusingWindsSpellHandler : SpellHandler
    {
        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        // The legacy OnEffectStart(GameSpellEffect) never ran, so no storm was ever
        // locked: SpellHandler builds an ECS effect. FocusingWindsECSEffect locks the
        // storm and releases it when the caster moves.
        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ECSGameEffectFactory.Create(initParams, static (in i) => new FocusingWindsECSEffect(i));
        }

        public FocusingWindsSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }

    /// <summary>
    /// Focusing Winds on a storm: the vortex stays where it is while the caster
    /// stands still.
    /// </summary>
    public class FocusingWindsECSEffect : ECSGameSpellEffect
    {
        private MovementWatch _watch;

        public FocusingWindsECSEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

        public override void OnStartEffect()
        {
            base.OnStartEffect();

            GameLiving caster = SpellHandler?.Caster;

            if (Owner is not GameStorm storm || caster == null)
                return;

            storm.Movable = false;

            if (caster is GamePlayer player)
                player.Out.SendMessage("Now the vortex of this storm is locked!", eChatType.CT_YouWereHit, eChatLoc.CL_SystemWindow);

            _watch = new MovementWatch(caster, why =>
            {
                if (why == MovementWatch.Moving && caster is GamePlayer mover)
                    mover.Out.SendMessage("You are moving. Your concentration fades", eChatType.CT_SpellExpires, eChatLoc.CL_SystemWindow);

                End();
            });
            _watch.Start();
        }

        public override void OnStopEffect()
        {
            base.OnStopEffect();

            _watch?.Stop();
            _watch = null;

            if (Owner is GameStorm storm)
                storm.Movable = true;
        }
    }
    #endregion

    //shared timer 1
    #region Stormlord-7
    [SpellHandler(eSpellType.ChokingVapors)]
    public class ChokingVaporsSpellHandler : StormSpellHandler
    {
        // constructor
        public ChokingVaporsSpellHandler(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
            //Construct a new storm.
            storm = new GameStorm();
            storm.Realm = caster.Realm;
            storm.X = caster.X;
            storm.Y = caster.Y;
            storm.Z = caster.Z;
            storm.CurrentRegionID = caster.CurrentRegionID;
            storm.Heading = caster.Heading;
            storm.Owner = caster;
            storm.Movable = true;

            // Construct the storm spell
            dbs = new DbSpell();
            dbs.SpellID = spell.ID; // one identity per storm kind, so a pulse refreshes its own effect
            dbs.Name = spell.Name;
            dbs.Icon = 7223;
            dbs.ClientEffect = 7223;
            dbs.Damage = spell.Damage;
            dbs.DamageType = (int)spell.DamageType;
            dbs.Target = eSpellTarget.ENEMY.ToString();
            dbs.Radius = 0;
            dbs.Type = eSpellType.StormStrConstDebuff.ToString();
            dbs.Value = spell.Value;
            dbs.Duration = spell.ResurrectHealth; // should be 2
            dbs.Frequency = spell.ResurrectMana;
            dbs.Pulse = 0;
            dbs.PulsePower = 0;
            dbs.LifeDrainReturn = spell.LifeDrainReturn;
            dbs.Power = 0;
            dbs.CastTime = 0;
            dbs.Range = WorldMgr.VISIBILITY_DISTANCE;
            sRadius = 350;
            s = new Spell(dbs, 1);
            sl = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
            tempest = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
        }
    }
    /// <summary>
    /// Str/Con stat specline debuff
    /// </summary>
    [SpellHandler(eSpellType.StormStrConstDebuff)]
    public class StormStrConstDebuff : DualStatDebuff
    {
        public override eProperty Property1 { get { return eProperty.Strength; } }
        public override eProperty Property2 { get { return eProperty.Constitution; } }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (target == null || !target.IsAlive || target.ObjectState != GameLiving.eObjectState.Active) return;
            bool fresh = !StormPulse.Holds(target, this);
            base.ApplyEffectOnTarget(target);

            if (fresh)
                (target as GamePlayer)?.Out?.SendMessage("Your strenght and constitution decreased!", eChatType.CT_YouWereHit, eChatLoc.CL_SystemWindow);
        }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams) =>
            ECSGameEffectFactory.Create(initParams, static (in ECSGameEffectInitParams i) => new MasterLevelStormStatECSEffect(i));

        // A second longer than the storm's pulse: refreshed while inside, gone a few seconds after leaving.
        protected override int CalculateEffectDuration(GameLiving target) => Spell.Duration + StormPulse.Overlap;

        public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
        {

            effect.Owner.EffectList.Remove(effect);
            return base.OnEffectExpires(effect, noMessages);
        }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        // constructor
        public StormStrConstDebuff(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    #endregion

    //shared timer 1
    #region Stormlord-8
    [SpellHandler(eSpellType.SenseDullingCloud)]
    public class SenseDullingCloudSpellHandler : StormSpellHandler
    {
        // constructor
        public SenseDullingCloudSpellHandler(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
            //Construct a new storm.
            storm = new GameStorm();
            storm.Realm = caster.Realm;
            storm.X = caster.X;
            storm.Y = caster.Y;
            storm.Z = caster.Z;
            storm.CurrentRegionID = caster.CurrentRegionID;
            storm.Heading = caster.Heading;
            storm.Owner = caster;
            storm.Movable = true;

            // Construct the storm spell
            dbs = new DbSpell();
            dbs.SpellID = spell.ID; // one identity per storm kind, so a pulse refreshes its own effect
            dbs.Name = spell.Name;
            dbs.Icon = 7305;
            dbs.ClientEffect = 7305;
            dbs.Damage = spell.Damage;
            dbs.DamageType = (int)spell.DamageType;
            dbs.Target = eSpellTarget.ENEMY.ToString();
            dbs.Radius = 0;
            dbs.Type = eSpellType.StormAcuityDebuff.ToString();
            dbs.Value = spell.Value;
            dbs.Duration = spell.ResurrectHealth; // should be 2
            dbs.Frequency = spell.ResurrectMana;
            dbs.Pulse = 0;
            dbs.PulsePower = 0;
            dbs.LifeDrainReturn = spell.LifeDrainReturn;
            dbs.Power = 0;
            dbs.CastTime = 0;
            dbs.Range = WorldMgr.VISIBILITY_DISTANCE;
            sRadius = 350;
            s = new Spell(dbs, 1);
            sl = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
            tempest = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
        }
    }
    /// <summary>
    /// Acuity stat baseline debuff
    /// </summary>
    [SpellHandler(eSpellType.StormAcuityDebuff)]
    public class StormAcuityDebuff : SingleStatDebuff
    {
        public override eProperty Property1
        {

            get
            {
                eProperty temp = eProperty.Acuity;
                if (Target.Realm == eRealm.Albion) temp = eProperty.Intelligence;
                if (Target.Realm == eRealm.Midgard) temp = eProperty.Piety;
                if (Target.Realm == eRealm.Hibernia) temp = eProperty.Intelligence;

                return temp;
            }
        }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (target == null || !target.IsAlive || target.ObjectState != GameLiving.eObjectState.Active) return;
            bool fresh = !StormPulse.Holds(target, this);
            base.ApplyEffectOnTarget(target);

            if (fresh)
                (target as GamePlayer)?.Out?.SendMessage("Your acuity decreased!", eChatType.CT_YouWereHit, eChatLoc.CL_SystemWindow);
        }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams) =>
            ECSGameEffectFactory.Create(initParams, static (in ECSGameEffectInitParams i) => new MasterLevelStormStatECSEffect(i));

        // A second longer than the storm's pulse: refreshed while inside, gone a few seconds after leaving.
        protected override int CalculateEffectDuration(GameLiving target) => Spell.Duration + StormPulse.Overlap;

        public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
        {

            effect.Owner.EffectList.Remove(effect);
            return base.OnEffectExpires(effect, noMessages);
        }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        // constructor
        public StormAcuityDebuff(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    #endregion

    //no shared timer
    #region Stormlord-9
    [SpellHandler(eSpellType.EnergyTempest)]
    public class EnergyTempestSpellHandler : StormSpellHandler
    {
        // constructor
        public EnergyTempestSpellHandler(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
            //Construct a new storm.
            storm = new GameStorm();
            storm.Realm = caster.Realm;
            storm.X = caster.X;
            storm.Y = caster.Y;
            storm.Z = caster.Z;
            storm.CurrentRegionID = caster.CurrentRegionID;
            storm.Heading = caster.Heading;
            storm.Owner = caster;
            storm.Movable = true;

            // Construct the storm spell
            dbs = new DbSpell();
            dbs.SpellID = spell.ID; // one identity per storm kind, so a pulse refreshes its own effect
            dbs.Name = spell.Name;
            dbs.Icon = 7216;
            dbs.ClientEffect = 7216;
            dbs.Damage = spell.Damage;
            dbs.DamageType = (int)spell.DamageType;
            dbs.Target = eSpellTarget.ENEMY.ToString();
            dbs.Radius = 0;
            dbs.Type = eSpellType.StormEnergyTempest.ToString();
            dbs.Value = spell.Value;
            dbs.Duration = spell.ResurrectHealth;
            dbs.Frequency = spell.ResurrectMana;
            dbs.Pulse = 0;
            dbs.PulsePower = 0;
            dbs.LifeDrainReturn = spell.LifeDrainReturn;
            dbs.Power = 0;
            dbs.CastTime = 0;
            dbs.Range = WorldMgr.VISIBILITY_DISTANCE;
            sRadius = 350;
            s = new Spell(dbs, 1);
            sl = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
            tempest = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
        }
    }
    [SpellHandler(eSpellType.StormEnergyTempest)]
    public class StormEnergyTempest : SpellHandler
    {
        /// <summary>
        /// Calculates the base 100% spell damage which is then modified by damage variance factors
        /// </summary>
        /// <returns></returns>
        public override double CalculateDamageBase(GameLiving target)
        {
            GamePlayer player = Caster as GamePlayer;

            // % damage procs
            if (Spell.Damage < 0)
            {
                double spellDamage = 0;

                if (player != null)
                {
                    // This equation is used to simulate live values - Tolakram
                    spellDamage = (target.MaxHealth * -Spell.Damage * .01) / 2.5;
                }

                if (spellDamage < 0)
                    spellDamage = 0;

                return spellDamage;
            }

            return base.CalculateDamageBase(target);
        }

        public override double DamageCap(double effectiveness)
        {
            if (Spell.Damage < 0)
            {
                return (Target.MaxHealth * -Spell.Damage * .01) * 3.0 * effectiveness;
            }

            return base.DamageCap(effectiveness);
        }
        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (target == null) return;
            if (!target.IsAlive || target.ObjectState != GameLiving.eObjectState.Active) return;

            // calc damage
            AttackData ad = CalculateDamageToTarget(target);
            SendDamageMessages(ad);
            DamageTarget(ad, true);            
            target.StartInterruptTimer(target.SpellInterruptDuration, ad.AttackType, Caster);
        }

        public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
        {

            effect.Owner.EffectList.Remove(effect);
            return base.OnEffectExpires(effect, noMessages);
        }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        // constructor
        public StormEnergyTempest(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    #endregion

    /// <summary>Shared rules of the storm pulses (TOA_PLAN WP2.6).</summary>
    public static class StormPulse
    {
        /// <summary>How much longer a storm's stat effect lasts than the pause between two pulses.</summary>
        public const int Overlap = 1000;

        /// <summary>True while <paramref name="target"/> carries this storm's effect: a refresh says nothing.</summary>
        public static bool Holds(GameLiving target, ISpellHandler handler)
        {
            if (target?.effectListComponent == null)
                return false;

            foreach (ECSGameSpellEffect effect in target.effectListComponent.GetSpellEffects())
            {
                if (effect?.SpellHandler?.Spell?.ID == handler.Spell.ID && effect.SpellHandler.GetType() == handler.GetType())
                    return true;
            }

            return false;
        }

        /// <summary>A drain that skips the normal effect path still counts as an attack by the caster.</summary>
        public static void CountAsAttack(SpellHandler handler, GameLiving target)
        {
            var ad = new AttackData
            {
                Attacker = handler.Caster,
                Target = target,
                AttackType = AttackData.eAttackType.Spell,
                SpellHandler = handler,
                AttackResult = eAttackResult.HitUnstyled,
                Damage = 0,
                DamageType = handler.Spell.DamageType,
            };

            handler.Caster.OnAttackEnemy(ad);
            target.OnAttackedByEnemy(ad);
        }
    }

    //ML 10 Arcing Power - already handled in another area
}
