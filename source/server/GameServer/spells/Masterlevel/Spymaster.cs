using System;
using System.Collections.Generic;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.Effects;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    //http://www.camelotherald.com/masterlevels/ma.php?ml=Spymaster
    #region Spymaster-1
    //AbstractServerRules OnPlayerKilled
    #endregion

    #region Spymaster-2
    [SpellHandler(eSpellType.Decoy)]
    public class DecoySpellHandler : SpellHandler
    {
        private GameDecoy decoy;
        private GameSpellEffect m_effect;
        /// <summary>
        /// Execute Decoy summon spell
        /// </summary>
        /// <param name="target"></param>
        public override void FinishSpellCast(GameLiving target)
        {
            m_caster.Mana -= PowerCost(target);
            base.FinishSpellCast(target);
        }
        public override bool HasConflictingEffectWith(ISpellHandler compare)
        {
            return false;
        }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            GameSpellEffect neweffect = CreateSpellEffect(target, CasterEffectiveness);
            decoy.AddToWorld();
            neweffect.Start(decoy);
        }

        public override void OnEffectStart(GameSpellEffect effect)
        {
            base.OnEffectStart(effect);
            m_effect = effect;
            if (effect.Owner == null || !effect.Owner.IsAlive)
                return;
            GameEventMgr.AddHandler(decoy, GameLivingEvent.Dying, new DOLEventHandler(DecoyDied));
        }
        public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
        {
            GameEventMgr.RemoveHandler(decoy, GameLivingEvent.Dying, new DOLEventHandler(DecoyDied));
            if (decoy != null)
            {
                decoy.Health = 0;
                decoy.Delete();
            }
            return base.OnEffectExpires(effect, noMessages);
        }
        private void DecoyDied(DOLEvent e, object sender, EventArgs args)
        {
            GameNPC kDecoy = sender as GameNPC;
            if (kDecoy == null) return;
            if (e == GameLivingEvent.Dying)
            {
                MessageToCaster("Your Decoy has fallen!", eChatType.CT_SpellExpires);
                OnEffectExpires(m_effect, true);
                return;
            }
        }
        public DecoySpellHandler(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
            Random m_rnd = new Random();
            decoy = new GameDecoy();
            //Fill the object variables
            decoy.CurrentRegion = caster.CurrentRegion;
            decoy.Heading = (ushort)((caster.Heading + 2048) % 4096);
            decoy.Level = 50;
            decoy.Realm = caster.Realm;
            decoy.X = caster.X;
            decoy.Y = caster.Y;
            decoy.Z = caster.Z;
            string TemplateId = string.Empty;
            switch (caster.Realm)
            {
                case eRealm.Albion:
                    decoy.Name = "Avalonian Unicorn Knight";
                    decoy.Model = (ushort)m_rnd.Next(61, 68);
                    TemplateId = "e3ead77b-22a7-4b7d-a415-92a29295dcf7";
                    break;
                case eRealm.Midgard:
                    decoy.Name = "Kobold Elding Herra";
                    decoy.Model = (ushort)m_rnd.Next(169, 184);
                    TemplateId = "ee137bff-e83d-4423-8305-8defa2cbcd7a";
                    break;
                case eRealm.Hibernia:
                    decoy.Name = "Elf Gilded Spear";
                    decoy.Model = (ushort)m_rnd.Next(334, 349);
                    TemplateId = "a4c798a2-186a-4bda-99ff-ccef228cb745";
                    break;
            }
            GameNpcInventoryTemplate load = new GameNpcInventoryTemplate();
            if (load.LoadFromDatabase(TemplateId))
            {
                decoy.EquipmentTemplateID = TemplateId;
                decoy.Inventory = load;
                decoy.BroadcastLivingEquipmentUpdate();
            }
            decoy.MaxSpeedBase = 0;
            decoy.GuildName = string.Empty;
        }
    }
    #endregion

    #region Spymaster-3
    //Gameliving - StartWeaponMagicalEffect
    #endregion

    #region Spymaster-4
    [SpellHandler(eSpellType.Sabotage)]
    public class SabotageSpellHandler : SpellHandler
    {
        public override void OnDirectEffect(GameLiving target)
        {
            base.OnDirectEffect(target);
            if (target is GameFont)
            {
                GameFont targetFont = target as GameFont;
                targetFont.Delete();
                MessageToCaster("Selected ward has been saboted!", eChatType.CT_SpellResisted);
            }
        }

        public SabotageSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    #endregion

    //shared timer 1
    #region Spymaster-5
    [SpellHandler(eSpellType.TangleSnare)]
    public class TangleSnareSpellHandler : MineSpellHandler
    {
        // constructor
        public TangleSnareSpellHandler(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
            Unstealth = false;

            //Construct a new mine.
            mine = new(new BlankBrain());
            mine.Model = 2592;
            mine.Name = spell.Name;
            mine.Realm = caster.Realm;
            mine.X = caster.X;
            mine.Y = caster.Y;
            mine.Z = caster.Z;
            mine.CurrentRegionID = caster.CurrentRegionID;
            mine.Heading = caster.Heading;
            mine.Owner = caster;

            // Construct the mine spell
            dbs = new DbSpell();
            dbs.Name = spell.Name;
            dbs.Icon = 7220;
            dbs.ClientEffect = 7220;
            dbs.Damage = spell.Damage;
            dbs.DamageType = (int)spell.DamageType;
            dbs.Target = eSpellTarget.ENEMY.ToString();
            dbs.Radius = 0;
            dbs.Type = eSpellType.SpeedDecrease.ToString();
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
            trap = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
        }
    }
    #endregion

    //shared timer 1
    #region Spymaster-6
    [SpellHandler(eSpellType.PoisonSpike)]
    public class PoisonSpikeSpellHandler : MineSpellHandler
    {
        // constructor
        public PoisonSpikeSpellHandler(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
            Unstealth = false;

            //Construct a new font.
            mine = new(new BlankBrain());
            mine.Model = 2589;
            mine.Name = spell.Name;
            mine.Realm = caster.Realm;
            mine.X = caster.X;
            mine.Y = caster.Y;
            mine.Z = caster.Z;
            mine.CurrentRegionID = caster.CurrentRegionID;
            mine.Heading = caster.Heading;
            mine.Owner = caster;

            // Construct the mine spell
            dbs = new DbSpell();
            dbs.Name = spell.Name;
            dbs.Icon = 7281;
            dbs.ClientEffect = 7281;
            dbs.Damage = spell.Damage;
            dbs.DamageType = (int)spell.DamageType;
            dbs.Target = eSpellTarget.ENEMY.ToString();
            dbs.Radius = 350;
            dbs.Type = eSpellType.PoisonspikeDot.ToString();
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
            trap = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
        }
    }
    #region Subspell
    [SpellHandler(eSpellType.PoisonspikeDot)]
    public class Spymaster6DotHandler : DoTSpellHandler
    {
        // constructor
        public Spymaster6DotHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override double CalculateSpellResistChance(GameLiving target) { return 0; }
        protected override GameSpellEffect CreateSpellEffect(GameLiving target, double effectiveness)
        {
            return new GameSpellEffect(this, m_spell.Duration, m_spellLine.IsBaseLine ? 5000 : 4000, effectiveness);
        }
    }
    #endregion
    #endregion

    #region Spymaster-7
    [SpellHandler(eSpellType.Loockout)]
    public class LoockoutSpellHandler : SpellHandler
    {
        /// <summary>How close the seated partner must be: beside the Spymaster.</summary>
        public const int PartnerRange = 200;

        /// <summary>The seated player chosen when the cast began.</summary>
        public GamePlayer Partner { get; private set; }

        /// <summary>Why <paramref name="partner"/> cannot be the seated partner, or null.</summary>
        public static string PartnerProblem(GameLiving scout, GameLiving partner)
        {
            if (partner is not GamePlayer || partner == scout || scout == null)
                return "You must target a seated member of your realm!";
            if (!partner.IsAlive || partner.ObjectState != GameObject.eObjectState.Active)
                return "Your target is dead!";
            if (partner.Realm != scout.Realm)
                return "You must target a seated member of your realm!";
            if (!partner.IsSitting)
                return "Target must be sitting!";
            if (partner.CurrentRegionID != scout.CurrentRegionID || !scout.IsWithinRadius(partner, PartnerRange))
                return "Your target must be sitting beside you!";
            return null;
        }

        public override bool CheckBeginCast(GameLiving selectedTarget)
        {
            string problem = PartnerProblem(Caster, selectedTarget);
            if (problem != null) { MessageToCaster(problem, eChatType.CT_System); return false; }
            Partner = (GamePlayer)selectedTarget;
            return base.CheckBeginCast(selectedTarget);
        }

        // The legacy OnEffectStart(GameSpellEffect) is never reached: SpellHandler
        // builds an ECS effect. The work and both watches live in LoockoutECSEffect.
        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ECSGameEffectFactory.Create(initParams, static (in i) => new LoockoutECSEffect(i));
        }

        public LoockoutSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }

    /// <summary>
    /// Lookout: the Spymaster keeps watch beside a seated player and gains 100
    /// stealth skill. Either of the two moving ends it. Moving was announced
    /// through an event nothing raises, so both are sampled by a MovementWatch.
    /// </summary>
    public class LoockoutECSEffect : ECSGameSpellEffect
    {
        public const int StealthBonus = 100;

        private MovementWatch _sitterWatch;
        private MovementWatch _scoutWatch;
        private GameLiving _scout;
        private bool _bonusGiven;

        public LoockoutECSEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

        public GamePlayer Sitter { get; private set; }
        public MovementWatch SitterWatch => _sitterWatch;
        public MovementWatch ScoutWatch => _scoutWatch;
        public bool BonusGiven => _bonusGiven;

        /// <summary>Why the hold asked to end (a MovementWatch reason), null while held.</summary>
        public string EndReason { get; private set; }

        public override void OnStartEffect()
        {
            base.OnStartEffect();

            if (SpellHandler?.Caster is not GamePlayer scout)
                return;

            // The sitter is the effect owner; a self-targeted row falls back to the
            // seated partner picked at cast start.
            GamePlayer sitter = Owner as GamePlayer;

            if (sitter == null || sitter == scout)
                sitter = (SpellHandler as LoockoutSpellHandler)?.Partner ?? sitter;

            if (LoockoutSpellHandler.PartnerProblem(scout, sitter) != null)
            {
                EndReason = MovementWatch.Gone;
                End();
                return;
            }

            Sitter = sitter;
            _scout = scout;
            scout.BaseBuffBonusCategory[eProperty.Skill_Stealth] += StealthBonus;
            _bonusGiven = true;
            new LoockoutOwner().Start(scout);

            // The partner standing up ends the watch as surely as walking away.
            _sitterWatch = new MovementWatch(sitter, why => Broken(sitter, why), onStand: true);
            _sitterWatch.Start();
            _scoutWatch = new MovementWatch(scout, why => Broken(scout, why));
            _scoutWatch.Start();
        }

        public override void OnStopEffect()
        {
            base.OnStopEffect();

            _sitterWatch?.Stop();
            _scoutWatch?.Stop();
            _sitterWatch = null;
            _scoutWatch = null;

            if (_bonusGiven && _scout != null)
            {
                _scout.BaseBuffBonusCategory[eProperty.Skill_Stealth] -= StealthBonus;
                _bonusGiven = false;
                DOL.GS.Spells.SpellHandler.FindStaticEffectOnTarget(_scout, typeof(LoockoutOwner))?.Cancel(false);
            }
        }

        private void Broken(GameLiving who, string why)
        {
            if (why == MovementWatch.Moving && who is GamePlayer mover)
                mover.Out.SendMessage("You are moving. Your concentration fades!", eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
            else if (why == MovementWatch.Standing && _scout is GamePlayer scout)
                scout.Out.SendMessage(who.Name + " stands up. Your concentration fades!", eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);

            EndReason ??= why;
            End();
        }
    }
    #endregion

    //shared timer 1
    #region Spymaster-8
    [SpellHandler(eSpellType.SiegeWrecker)]
    public class SiegeWreckerSpellHandler : MineSpellHandler
    {
        public override void OnEffectPulse(GameSpellEffect effect)
        {
            if (mine == null || mine.ObjectState == GameObject.eObjectState.Deleted)
            {
                effect.Cancel(false);
                return;
            }

            if (trap == null)
                return;

            foreach (GameNPC npc in mine.GetNPCsInRadius((ushort)s.Range))
            {
                if (npc is GameSiegeWeapon && npc.IsAlive && GameServer.ServerRules.IsAllowedToAttack(Caster, npc, true))
                {
                    trap.StartSpell(npc);

                    if (Unstealth)
                        npc.Stealth(false);

                    return;
                }
            }
        }
        // constructor
        public SiegeWreckerSpellHandler(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
            Unstealth = false;

            //Construct a new mine.
            mine = new(new BlankBrain());
            mine.Model = 2591;
            mine.Name = spell.Name;
            mine.Realm = caster.Realm;
            mine.X = caster.X;
            mine.Y = caster.Y;
            mine.Z = caster.Z;
            mine.MaxSpeedBase = 0;
            mine.CurrentRegionID = caster.CurrentRegionID;
            mine.Heading = caster.Heading;
            mine.Owner = caster;

            // Construct the mine spell
            dbs = new DbSpell();
            dbs.Name = spell.Name;
            dbs.Icon = 7301;
            dbs.ClientEffect = 7301;
            dbs.Damage = spell.Damage;
            dbs.DamageType = (int)spell.DamageType;
            dbs.Target = eSpellTarget.ENEMY.ToString();
            dbs.Radius = 0;
            dbs.Type = eSpellType.DirectDamage.ToString();
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
            trap = ScriptMgr.CreateSpellHandler(m_caster, s, sl);
        }
    }
    #endregion

    #region Spymaster-9
    [SpellHandler(eSpellType.EssenceFlare)]
    public class EssenceFlareSpellHandler : SummonItemSpellHandler
    {
        public EssenceFlareSpellHandler(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
            DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>("Meschgift");
            if (template != null)
            {
                items.Add(GameInventoryItem.Create(template));
                foreach (DbInventoryItem item in items)
                {
                    if (item.IsStackable)
                    {
                        item.Count = 1;
                        item.Weight = item.Count * item.Weight;
                    }
                }
            }
            }
        }
    #endregion

        #region Spymaster-10
        [SpellHandler(eSpellType.BlanketOfCamouflage)]
        public class GroupstealthHandler : MasterlevelHandling
        {
            public GroupstealthHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

            /// <summary>
            /// Group stealth is a benefit for the group. MasterlevelHandling marks every
            /// ML spell harmful, which made each cast "attack" every member it hid
            /// (combat timers, speed buff cancels, companion assist).
            /// </summary>
            public override bool HasPositiveEffect => true;

            public override bool CheckBeginCast(GameLiving selectedTarget)
            {
                if (selectedTarget == Caster) return false;
                return base.CheckBeginCast(selectedTarget);
            }

            // The legacy OnEffectStart(GameSpellEffect) is never reached: SpellHandler
            // builds one ECS effect per group member. Each member's effect carries its
            // own watch, which also ends the old shared-m_effect bug where any member
            // breaking stealth expired the effect of whoever was processed last.
            public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
            {
                return ECSGameEffectFactory.Create(initParams, static (in i) => new GroupstealthECSEffect(i));
            }
        }

        /// <summary>
        /// Blanket of Camouflage on one group member. A member without the Stealth
        /// ability loses it on moving, attacking or casting; a stealther follows the
        /// normal stealth rules ("unless they are a stealther", spell description).
        /// Companions and other bots in the group are hidden exactly like players
        /// (TOA_PLAN WP2.4); pets stay visible, as on live.
        /// </summary>
        public class GroupstealthECSEffect : ECSGameSpellEffect
        {
            private MovementWatch _watch;
            private GameLiving _hidden;

            public GroupstealthECSEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

            /// <summary>The member is held by the Blanket's rules (not a stealther).</summary>
            public bool Held => _hidden != null;
            public MovementWatch Watch => _watch;

            /// <summary>Why the hold asked to end (a MovementWatch reason), null while held.</summary>
            public string EndReason { get; private set; }

            /// <summary>The members a Blanket hides: players and bots (companions, world bots), not pets.</summary>
            public static bool CanHide(GameLiving member) => member is GamePlayer or GameBot;

            public static bool FollowsNormalStealthRules(GameLiving member)
            {
                Specialization stealth = member switch
                {
                    GamePlayer player => player.GetSpecializationByName(Specs.Stealth),
                    GameBot bot => bot.GetSpecializationByName(Specs.Stealth),
                    _ => null
                };

                return (stealth?.Level ?? 0) > 0;
            }

            /// <summary>
            /// True while a stealthed living carries a Blanket, held or not. The bot brain asks
            /// before it drops an idle bot's stealth, so a hidden companion stays hidden like a
            /// player: a non-stealther until it moves or acts, a stealther under its own rules.
            /// </summary>
            public static bool Hides(GameLiving living) =>
                living?.effectListComponent != null && Hides(living.IsStealthed, living.effectListComponent.GetSpellEffects());

            public static bool Hides(bool stealthed, IEnumerable<ECSGameSpellEffect> effects)
            {
                if (!stealthed || effects == null)
                    return false;

                foreach (ECSGameSpellEffect effect in effects)
                {
                    if (effect is GroupstealthECSEffect)
                        return true;
                }

                return false;
            }

            public override void OnStartEffect()
            {
                base.OnStartEffect();

                if (!CanHide(Owner))
                    return;

                GameLiving member = Owner;

                member.Stealth(true);

                if (FollowsNormalStealthRules(member))
                    return;

                _hidden = member;
                _watch = new MovementWatch(member, Broken,
                    () =>
                    {
                        // Stealth lost some other way (refused, revealed, hit): end quietly.
                        if (member.IsStealthed)
                            return true;

                        End();
                        return false;
                    },
                    onMove: true, onAttack: true, onCast: true, ownCast: SpellHandler);
                _watch.Start();
            }

            public override void OnStopEffect()
            {
                base.OnStopEffect();

                _watch?.Stop();
                _watch = null;

                if (_hidden != null && _hidden.IsStealthed)
                    _hidden.Stealth(false);

                _hidden = null;
            }

            private void Broken(string why)
            {
                string text = why switch
                {
                    MovementWatch.Moving => "You are moving. Your camouflage fades!",
                    MovementWatch.Attacking => "You are attacking. Your camouflage fades!",
                    MovementWatch.Casting => "You are casting a spell. Your camouflage fades!",
                    _ => null
                };

                if (text != null)
                    (_hidden as GamePlayer)?.Out?.SendMessage(text, eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);

                EndReason ??= why;
                End();
            }
        }
        #endregion
    }

//to show an Icon & informations to the caster
namespace DOL.GS.Effects
{
    public class LoockoutOwner : StaticEffect, IGameEffect
    {
        public LoockoutOwner() : base() { }
        public void Start(GamePlayer player) { base.Start(player); }
        public override void Stop() { base.Stop(); }
        public override ushort Icon { get { return 2616; } }
        public override string Name { get { return "Loockout"; } }
        public override IList<string> DelveInfo
        {
            get
            {
                var delveInfoList = new List<string>();
                delveInfoList.Add("Your stealth range is increased.");
                return delveInfoList;
            }
        }
    }
}
