using System;
using System.Collections.Generic;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerClass;
using DOL.GS.PropertyCalc;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Offline DAoC shim for the artifact handlers ported from OpenDAoC-Core
    /// (GameServer/spells/Artifacts). Upstream still writes most of them against the
    /// legacy GameSpellEffect hooks (OnEffectStart / OnEffectExpires / OnEffectPulse),
    /// which our ECS effect pipeline never calls, and every artifact spell type maps to
    /// eEffect.Unknown, so the stock ECS effects (StatBuff, StatDebuff, Proc, Ablative)
    /// apply none of their bonuses. A handler that implements this interface and returns
    /// an <see cref="ArtifactSpellEffect"/> (or one of its siblings below) from
    /// CreateECSEffect gets its start, stop and pulse code run by the ECS instead.
    /// </summary>
    public interface IArtifactEffectHandler
    {
        void OnArtifactEffectStart(ECSGameSpellEffect effect);
        void OnArtifactEffectStop(ECSGameSpellEffect effect);
        void OnArtifactEffectPulse(ECSGameSpellEffect effect) { }
    }

    /// <summary>
    /// Generic ECS effect for artifact handlers: forwards start, stop and pulse to the
    /// handler and sends the spell's own Message1-4 like the stock buff effects.
    /// </summary>
    public class ArtifactSpellEffect : ECSGameSpellEffect
    {
        public ArtifactSpellEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

        public static ECSGameSpellEffect Create(in ECSGameEffectInitParams initParams)
        {
            return ECSGameEffectFactory.Create(initParams, static (in i) => new ArtifactSpellEffect(i));
        }

        public override void OnStartEffect()
        {
            (SpellHandler as IArtifactEffectHandler)?.OnArtifactEffectStart(this);
            OnEffectStartsMsg(true, false, true);
        }

        public override void OnStopEffect()
        {
            (SpellHandler as IArtifactEffectHandler)?.OnArtifactEffectStop(this);
            OnEffectExpiresMsg(true, false, true);
        }

        public override void OnEffectPulse()
        {
            (SpellHandler as IArtifactEffectHandler)?.OnArtifactEffectPulse(this);
        }

        /// <summary>
        /// Applies or removes Property1-3 of a property-changing handler in its own bonus
        /// categories. Debuff categories follow <see cref="ArtifactSpells.DebuffSign"/>,
        /// so artifact and stock debuffs on one target add up.
        /// </summary>
        public static void ApplyProperties(PropertyChangingSpell handler, GameLiving owner, int value, bool remove)
        {
            Apply(owner, handler.BonusCategory1, handler.Property1, value, remove);
            Apply(owner, handler.BonusCategory2, handler.Property2, value, remove);
            Apply(owner, handler.BonusCategory3, handler.Property3, value, remove);
        }

        public static void Apply(GameLiving owner, eBuffBonusCategory category, eProperty property, int value, bool remove)
        {
            if (owner == null || property is eProperty.Undefined)
                return;

            IPropertyIndexer indexer = GetPropertyIndexer(owner, category);

            if (indexer == null)
                return;

            if (category is eBuffBonusCategory.Debuff or eBuffBonusCategory.SpecDebuff)
                value *= ArtifactSpells.DebuffSign(property);

            indexer[property] += remove ? -value : value;
        }
    }

    /// <summary>
    /// Offensive proc effect for artifact proc handlers (morph forms with a weapon proc).
    /// Registers as eEffect.OffensiveProc so GameLiving.OnAttackEnemy fires the proc,
    /// and forwards start/stop to the handler for the shape change.
    /// </summary>
    public class ArtifactProcEffect : ProcECSGameEffect
    {
        public ArtifactProcEffect(in ECSGameEffectInitParams initParams) : base(initParams)
        {
            EffectType = eEffect.OffensiveProc;
        }

        public override void OnStartEffect()
        {
            base.OnStartEffect();
            (SpellHandler as IArtifactEffectHandler)?.OnArtifactEffectStart(this);
        }

        public override void OnStopEffect()
        {
            (SpellHandler as IArtifactEffectHandler)?.OnArtifactEffectStop(this);
            base.OnStopEffect();
        }
    }

    /// <summary>
    /// Ablative effect for artifact ablatives; registers as eEffect.AblativeArmor so the
    /// melee absorb code in GameLiving finds it.
    /// </summary>
    public class ArtifactAblativeEffect : AblativeArmorECSGameEffect
    {
        public ArtifactAblativeEffect(in ECSGameEffectInitParams initParams) : base(initParams)
        {
            EffectType = eEffect.AblativeArmor;
        }
    }

    /// <summary>
    /// Base for the artifact forms that combine a shape change with an offensive proc
    /// (Atlantis Tablet, Dream Sphere, Maddening Scalars, Traitor's Dagger).
    /// </summary>
    public abstract class ArtifactMorphProcSpellHandler : OffensiveProcSpellHandler, IArtifactEffectHandler
    {
        protected ArtifactMorphProcSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            // Same proc spell line rule as BaseProcSpellHandler.CreateECSEffect.
            _procSpellLine = initParams.Target == Caster ? _buffSpellLine : SkillBase.GetSpellLine(GlobalSpellsLines.Item_Effects);
            return ECSGameEffectFactory.Create(initParams, static (in i) => new ArtifactProcEffect(i));
        }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (ArtifactSpells.HasOtherMorph(target, Spell))
            {
                (target as GamePlayer)?.Out.SendMessage("You already have an active morph!", eChatType.CT_SpellResisted, eChatLoc.CL_ChatWindow);
                return;
            }

            base.ApplyEffectOnTarget(target);
        }

        public virtual void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            if (effect.Owner is GamePlayer player)
                ArtifactSpells.SetMorphModel(player, Spell.LifeDrainReturn);
        }

        public virtual void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            if (effect.Owner is GamePlayer player)
                ArtifactSpells.RestoreModel(player);
        }
    }

    /// <summary>
    /// Shared helpers for the artifact handlers.
    /// </summary>
    public static class ArtifactSpells
    {
        /// <summary>
        /// The artifact shape changes that exclude each other and that Shatter Illusions
        /// (Eternal Plant) breaks. Same list as upstream.
        /// </summary>
        public static readonly IReadOnlySet<eSpellType> MorphTypes = new HashSet<eSpellType>
        {
            eSpellType.ShadesOfMist,
            eSpellType.TraitorsDaggerProc,
            eSpellType.DreamMorph,
            eSpellType.DreamGroupMorph,
            eSpellType.MaddeningScalars,
            eSpellType.AtlantisTabletMorph,
            eSpellType.AlvarusMorph,
        };

        /// <summary>
        /// Sign under which a debuff of <paramref name="property"/> is stored in
        /// DebuffCategory / SpecDebuffCategory. Stock ECS debuffs store negative values
        /// (ECSGameSpellEffect.ApplyBonus) and the stat, resist and spell damage
        /// calculators read Math.Abs of the sum, so artifact debuffs must be negative too
        /// or they would cancel a stock debuff on the same target. The other calculators
        /// this package feeds (to-hit, fumble, spell fumble, spell range, physical and
        /// magic absorption) subtract the raw value and need it positive.
        /// </summary>
        public static int DebuffSign(eProperty property)
        {
            if (property is >= eProperty.Stat_First and <= eProperty.Stat_Last)
                return -1;

            if (property is >= eProperty.Resist_First and <= eProperty.Resist_Last)
                return -1;

            return property is eProperty.Resist_Natural or eProperty.SpellDamage or eProperty.Acuity ? -1 : 1;
        }

        /// <summary>
        /// Adds a debuff of <paramref name="amount"/> (negative removes it) to the owner's
        /// DebuffCategory under the sign from <see cref="DebuffSign"/>.
        /// </summary>
        public static void AddDebuff(GameLiving owner, eProperty property, int amount)
        {
            owner.DebuffCategory[property] += amount * DebuffSign(property);
        }

        public static void AddDebuffToAll(GameLiving owner, IEnumerable<eProperty> properties, int amount)
        {
            foreach (eProperty property in properties)
                AddDebuff(owner, property, amount);
        }

        /// <summary>
        /// Conflict rule for bridged artifact effects. They all share the eEffect.Unknown
        /// bucket, and EffectListComponent asks the existing effect's handler; bases such
        /// as SingleStatDebuff and SpeedDecreaseSpellHandler answer "always conflicts",
        /// which would let one artifact effect block or wipe every other one.
        /// </summary>
        public static bool ConflictsWith(ISpellHandler self, ISpellHandler compare)
        {
            Spell spell = self.Spell;
            Spell other = compare?.Spell;

            if (other == null)
                return false;

            if (spell.EffectGroup != 0 || other.EffectGroup != 0)
                return spell.EffectGroup == other.EffectGroup;

            return spell.SpellType == other.SpellType;
        }

        /// <summary>
        /// The ECS effects on <paramref name="owner"/> that belong to <paramref name="handler"/>.
        /// </summary>
        public static ECSGameSpellEffect FindEffect(GameLiving owner, ISpellHandler handler)
        {
            if (owner?.effectListComponent == null)
                return null;

            foreach (ECSGameSpellEffect effect in owner.effectListComponent.GetSpellEffects())
            {
                if (effect.SpellHandler == handler && !effect.IsEnding)
                    return effect;
            }

            return null;
        }

        /// <summary>
        /// The first live ECS effect of <paramref name="spellType"/> on <paramref name="owner"/>.
        /// Replacement for the legacy SpellHandler.FindEffectOnTarget(owner, "TypeName"),
        /// which only sees GameSpellEffects and so never finds an artifact effect.
        /// </summary>
        public static ECSGameSpellEffect FindEffect(GameLiving owner, eSpellType spellType)
        {
            if (owner?.effectListComponent == null)
                return null;

            foreach (ECSGameSpellEffect effect in owner.effectListComponent.GetSpellEffects())
            {
                if (effect.SpellHandler?.Spell?.SpellType == spellType && !effect.IsEnding)
                    return effect;
            }

            return null;
        }

        /// <summary>
        /// True when <paramref name="target"/> already wears another artifact shape change
        /// (a different spell; the same spell is a refresh).
        /// </summary>
        public static bool HasOtherMorph(GameLiving target, Spell spell)
        {
            if (target?.effectListComponent == null)
                return false;

            foreach (ECSGameSpellEffect effect in target.effectListComponent.GetSpellEffects())
            {
                Spell other = effect.SpellHandler?.Spell;

                if (other == null || effect.IsEnding || other.ID == spell.ID)
                    continue;

                if (MorphTypes.Contains(other.SpellType))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Ends every artifact shape change on <paramref name="target"/>; returns how many.
        /// </summary>
        public static int EndMorphs(GameLiving target)
        {
            if (target?.effectListComponent == null)
                return 0;

            int ended = 0;

            foreach (ECSGameSpellEffect effect in target.effectListComponent.GetSpellEffects())
            {
                Spell spell = effect.SpellHandler?.Spell;

                if (spell != null && MorphTypes.Contains(spell.SpellType) && effect.End())
                    ended++;
            }

            return ended;
        }

        /// <summary>
        /// Sets a morph model unless the player is a Necromancer (Disciple classes), whose
        /// model belongs to the shade mechanics. <paramref name="model"/> 0 leaves it alone.
        /// </summary>
        public static void SetMorphModel(GamePlayer player, int model)
        {
            if (player == null || model <= 0 || player.CharacterClass is ClassDisciple)
                return;

            player.Model = (ushort) model;
            player.Out.SendUpdatePlayer();
        }

        public static void RestoreModel(GamePlayer player)
        {
            if (player == null || player.CharacterClass is ClassDisciple)
                return;

            player.Model = player.CreationModel;
            player.Out.SendUpdatePlayer();
        }

        /// <summary>
        /// Stat-window refresh after a direct bonus change (the ECS only refreshes for
        /// known effect types).
        /// </summary>
        public static void RefreshStats(GameLiving living, bool resists = false)
        {
            if (living is not GamePlayer player)
                return;

            player.Out.SendCharStatsUpdate();

            if (resists)
                player.Out.SendCharResistsUpdate();

            player.UpdateEncumbrance();
            player.Out.SendUpdatePlayer();
        }

        /// <summary>
        /// Marks caster and target as in combat for a hostile artifact effect, as upstream
        /// does in its debuff handlers.
        /// </summary>
        public static void MarkHostile(GameLiving caster, GameLiving target)
        {
            if (caster == null || target == null)
                return;

            if (target.Realm == 0 || caster.Realm == 0)
            {
                target.LastAttackedByEnemyTickPvE = GameLoop.GameLoopTime;
                caster.LastAttackTickPvE = GameLoop.GameLoopTime;
            }
            else
            {
                target.LastAttackedByEnemyTickPvP = GameLoop.GameLoopTime;
                caster.LastAttackTickPvP = GameLoop.GameLoopTime;
            }
        }

        /// <summary>
        /// Removes summoned artifact items (ids compared case-insensitively) from the
        /// equipment and backpack; sends <paramref name="message"/> once when anything went.
        /// </summary>
        public static int RemoveSummonedItems(GamePlayer player, IReadOnlySet<string> itemIds, string message = null)
        {
            if (player?.Inventory == null)
                return 0;

            int removed = 0;

            lock (player.Inventory.Lock)
            {
                var items = new List<DOL.Database.DbInventoryItem>(player.Inventory.GetItemRange(eInventorySlot.MinEquipable, eInventorySlot.LastBackpack));

                foreach (DOL.Database.DbInventoryItem item in items)
                {
                    if (item?.Id_nb != null && itemIds.Contains(item.Id_nb) && player.Inventory.RemoveItem(item))
                        removed++;
                }
            }

            if (removed > 0 && !string.IsNullOrEmpty(message))
                player.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);

            return removed;
        }

        public static IReadOnlySet<string> ItemIds(params string[] ids)
        {
            return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The all-stats set used by AllStatsDebuff, Arrogance and Baleful Shape Change.
        /// </summary>
        public static readonly eProperty[] AllStats =
        [
            eProperty.Dexterity,
            eProperty.Strength,
            eProperty.Constitution,
            eProperty.Acuity,
            eProperty.Piety,
            eProperty.Empathy,
            eProperty.Quickness,
            eProperty.Intelligence,
            eProperty.Charisma,
        ];

        public static void AddToAll(IPropertyIndexer indexer, IEnumerable<eProperty> properties, int value)
        {
            foreach (eProperty property in properties)
                indexer[property] += value;
        }
    }
}
