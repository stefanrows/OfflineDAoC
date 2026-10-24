using System.Collections.Generic;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    /// <summary>
    /// A Master Level spell that puts an object into the world: a font, a trap (mine) or a storm
    /// (TOA_PLAN WP2.6). The object pulses its own spell on the livings around it and goes away
    /// when the spell's duration ends.
    /// </summary>
    public interface IMasterLevelPlacement
    {
        /// <summary>The placed object, null when the handler has none.</summary>
        GameNPC PlacedObject { get; }

        /// <summary>One pulse of the placed object.</summary>
        void PulsePlaced(MasterLevelPlacedECSEffect effect);

        /// <summary>The effect ended: the object leaves the world.</summary>
        void RemovePlaced();
    }

    /// <summary>
    /// The life of a placed font, trap or storm. These handlers used the legacy GameSpellEffect,
    /// whose timers are never started in this ECS core: the object pulsed once when it appeared
    /// and then stood in the world, inert, until the server restarted. This effect is owned by
    /// the object itself, pulses every <c>Spell.Frequency</c>, ends after <c>Spell.Duration</c>
    /// and deletes the object. It also ends when the object is gone (a font destroyed by damage,
    /// a trap that went off) or the caster has left the world, and it skips its pulses while the
    /// caster is dead.
    /// </summary>
    public class MasterLevelPlacedECSEffect : ECSGameSpellEffect
    {
        public MasterLevelPlacedECSEffect(in ECSGameEffectInitParams initParams) : base(initParams)
        {
            EffectType = eEffect.Unknown;
        }

        private IMasterLevelPlacement Placement => SpellHandler as IMasterLevelPlacement;

        public override void OnEffectPulse()
        {
            IMasterLevelPlacement placement = Placement;
            GameNPC placed = placement?.PlacedObject;
            GameLiving caster = SpellHandler?.Caster;

            if (placed == null || placed.ObjectState == GameObject.eObjectState.Deleted ||
                caster == null || caster.ObjectState == GameObject.eObjectState.Deleted)
            {
                End();
                return;
            }

            // A dead caster's font, trap or storm stands but does nothing until the caster is back up.
            if (caster.IsAlive)
                placement.PulsePlaced(this);
        }

        public override void OnStopEffect()
        {
            base.OnStopEffect();
            Placement?.RemovePlaced();
        }
    }

    /// <summary>Shared helpers of the placement handlers.</summary>
    public static class MasterLevelPlacement
    {
        /// <summary>
        /// Puts <paramref name="placed"/> into the world and starts its effect. The object stands
        /// where the handler put it: the data gives every font, trap and storm the target Self
        /// and range 0, so it appears at the caster's feet, for players and bots alike.
        /// </summary>
        public static bool Place(SpellHandler handler, GameNPC placed, double effectiveness)
        {
            if (handler == null || placed == null)
                return false;

            if (!placed.AddToWorld())
                return false;

            if (placed.Health <= 0)
                placed.Health = placed.MaxHealth;

            ECSGameEffectFactory.Create(new ECSGameEffectInitParams(placed, handler.Spell.Duration, effectiveness, handler),
                static (in ECSGameEffectInitParams i) => new MasterLevelPlacedECSEffect(i));
            return true;
        }

        /// <summary>Deletes a placed object that is still in the world.</summary>
        public static void Remove(GameNPC placed)
        {
            if (placed != null && placed.ObjectState != GameObject.eObjectState.Deleted)
                placed.Delete();
        }

        /// <summary>
        /// Players and bots around a placed object, plus the other NPCs when <paramref name="withNpcs"/>
        /// is set. Bots stand in for players everywhere else in this fork (WP2.4), so a storm that
        /// hits the enemy realm's players also hits its companions and world bots. Other placed
        /// objects (fonts, traps, storms, siege) never count.
        /// </summary>
        public static IEnumerable<GameLiving> LivingsAround(GameObject center, ushort radius, bool withNpcs)
        {
            foreach (GamePlayer player in center.GetPlayersInRadius(radius))
                yield return player;

            foreach (GameNPC npc in center.GetNPCsInRadius(radius))
            {
                if (npc is GameBot bot)
                    yield return bot;
                else if (withNpcs && npc is not GameMovingObject)
                    yield return npc;
            }
        }

        /// <summary>Sends a system message to the caster when it is a player; bots read nothing.</summary>
        public static void TellCaster(GameLiving caster, string text, eChatType type = eChatType.CT_Spell) =>
            (caster as GamePlayer)?.Out?.SendMessage(text, type, eChatLoc.CL_SystemWindow);
    }

    /// <summary>
    /// The stat change of a storm pulse (Dazzling Array, Inebriating Fumes, Choking Vapors, Sense
    /// Dulling Cloud). The standard stat effects pick their properties by effect type, which these
    /// spell types do not have, so the handler's own properties are applied here. The pulse
    /// refreshes it every few seconds while the living stays in the storm; it fades a few seconds
    /// after it leaves (the sub-spell's short duration).
    /// </summary>
    public class MasterLevelStormStatECSEffect : ECSGameSpellEffect
    {
        private readonly List<(eBuffBonusCategory category, eProperty property)> _applied = new();

        public MasterLevelStormStatECSEffect(in ECSGameEffectInitParams initParams) : base(initParams)
        {
            EffectType = eEffect.Unknown;
        }

        public override void OnStartEffect()
        {
            base.OnStartEffect();

            if (SpellHandler is not PropertyChangingSpell spell)
                return;

            Add(spell.BonusCategory1, spell.Property1);
            Add(spell.BonusCategory2, spell.Property2);

            foreach ((eBuffBonusCategory category, eProperty property) in _applied)
                ApplyBonus(Owner, category, property, SpellHandler.Spell.Value, Effectiveness, SpellHandler.HasPositiveEffect == false);

            Refresh();
        }

        public override void OnStopEffect()
        {
            base.OnStopEffect();

            foreach ((eBuffBonusCategory category, eProperty property) in _applied)
                ApplyBonus(Owner, category, property, SpellHandler.Spell.Value, Effectiveness, SpellHandler.HasPositiveEffect);

            _applied.Clear();
            Refresh();
        }

        private void Add(eBuffBonusCategory category, eProperty property)
        {
            if (property != eProperty.Undefined && !_applied.Contains((category, property)))
                _applied.Add((category, property));
        }

        private void Refresh() =>
            Owner?.effectListComponent?.RequestPlayerUpdate(EffectHelper.PlayerUpdate.Stats | EffectHelper.PlayerUpdate.WeaponArmor |
                                                            EffectHelper.PlayerUpdate.Encumbrance | EffectHelper.PlayerUpdate.Status);
    }
}
