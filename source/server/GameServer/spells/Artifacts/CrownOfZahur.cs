using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.Effects;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS.Spells
{
    //handled in HealSpellHandler
    [SpellHandler(eSpellType.EfficientHealing)]
    public class EfficientHealing : SpellHandler
    {
        #region Devle Info
        public override IList<string> DelveInfo
        {
            get
            {
                var list = new List<string>(16);

                //Name
                list.Add("Name: " + Spell.Name);
                list.Add("");

                //Description
                list.Add("Description: " + ShortDescription);
                list.Add("");

                //SpellType
                list.Add("Type: " + Spell.SpellType);

                //Value
                if (Spell.Value != 0)
                    list.Add("Efficient Heal: " + Spell.Value + "%");

                //Target
                list.Add("Target: " + Spell.Target);

                //Duration
                if (Spell.Duration >= ushort.MaxValue * 1000)
                    list.Add("Duration: Permanent.");
                else if (Spell.Duration > 60000)
                    list.Add(string.Format("Duration: {0}:{1} min", Spell.Duration / 60000, (Spell.Duration % 60000 / 1000).ToString("00")));
                //Cost
                list.Add("Power cost: " + Spell.Power.ToString("0;0'%'"));

                //Cast
                list.Add(LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.CastingTime", (Spell.CastTime * 0.001).ToString("0.0## sec;-0.0## sec;'instant'")));
                return list;
            }
        }
        #endregion
        public EfficientHealing(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
        }
    }
    //handled in HealSpellHandler
    [SpellHandler(eSpellType.EfficientEndurance)]
    public class EfficientEndurance : SpellHandler
    {
        #region Devle Info
        public override IList<string> DelveInfo
        {
            get
            {
                var list = new List<string>(16);

                //Name
                list.Add("Name: " + Spell.Name);
                list.Add("");

                //Description
                list.Add("Description: " + ShortDescription);
                list.Add("");

                //SpellType
                list.Add("Type: " + Spell.SpellType);

                //Value
                if (Spell.Value != 0)
                    list.Add("Efficient Endurance: " + Spell.Value + "%");

                //Target
                list.Add("Target: " + Spell.Target);

                //Duration
                if (Spell.Duration >= ushort.MaxValue * 1000)
                    list.Add("Duration: Permanent.");
                else if (Spell.Duration > 60000)
                    list.Add(string.Format("Duration: {0}:{1} min", Spell.Duration / 60000, (Spell.Duration % 60000 / 1000).ToString("00")));
                //Cost
                list.Add("Power cost: " + Spell.Power.ToString("0;0'%'"));
                
                //Cast
                list.Add(LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.CastingTime", (Spell.CastTime * 0.001).ToString("0.0## sec;-0.0## sec;'instant'")));
                return list;
            }
        }
        #endregion
        public EfficientEndurance(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
        }
    }
    /// <summary>
    /// Crown of Zahur power shield: while it lasts, a hit that finds the wearer at half
    /// health or less converts power into health (Spell.Value percent of max power,
    /// 25 % when the row leaves it at 0), as far as the wearer has power left.
    /// </summary>
    [SpellHandler(eSpellType.Powershield)]
    public class Powershield : SpellHandler, IArtifactEffectHandler
    {
        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ArtifactSpellEffect.Create(initParams);
        }

        public void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            GameEventMgr.AddHandler(effect.Owner, GameLivingEvent.AttackedByEnemy, new DOLEventHandler(AttackedEvent));
        }

        public void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            GameEventMgr.RemoveHandler(effect.Owner, GameLivingEvent.AttackedByEnemy, new DOLEventHandler(AttackedEvent));
        }

        public void AttackedEvent(DOLEvent e, object sender, EventArgs arguments)
        {
            if (sender is not GameLiving living || !living.IsAlive)
                return;

            if (arguments is not AttackedByEnemyEventArgs args || args.AttackData == null)
                return;

            if (living.Health > living.MaxHealth / 2)
                return;

            double percent = Spell.Value > 0 ? Spell.Value : 25;
            int power = Math.Min(living.Mana, (int) (living.MaxMana * percent * 0.01));

            if (power <= 0)
                return;

            living.Mana -= power;
            int healed = living.ChangeHealth(living, eHealthChangeType.Spell, power);
            (living as GamePlayer)?.Out.SendMessage("You convert " + power + " power into " + healed + " hit points!", eChatType.CT_Spell, eChatLoc.CL_ChatWindow);
        }

        #region Devle Info
        public override IList<string> DelveInfo
        {
            get
            {
                var list = new List<string>(16);

                //Name
                list.Add("Name: " + Spell.Name);
                list.Add("");

                //Description
                list.Add("Description: " + ShortDescription);
                list.Add("");

                //SpellType
                list.Add("Type: " + Spell.SpellType);

                //Value
                if (Spell.Value != 0)
                    list.Add("Power Converted: " + Spell.Value + "%");

                //Target
                list.Add("Target: " + Spell.Target);

                //Duration
                if (Spell.Duration >= ushort.MaxValue * 1000)
                    list.Add("Duration: Permanent.");
                else if (Spell.Duration > 60000)
                    list.Add(string.Format("Duration: {0}:{1} min", Spell.Duration / 60000, (Spell.Duration % 60000 / 1000).ToString("00")));
                //Cost
                list.Add("Power cost: " + Spell.Power.ToString("0;0'%'"));

                //Cast
                list.Add(LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.CastingTime", (Spell.CastTime * 0.001).ToString("0.0## sec;-0.0## sec;'instant'")));
                return list;
            }
        }
        #endregion
        public Powershield(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
        }
    }
}