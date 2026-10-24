using System;
using DOL.Database;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Armor and health buff or aura ("Boon of Kings": Spear of Kings and Scepter of the
    /// Meritorious, spell 30115). A negative Spell.Value is a percentage: the target's armor factor
    /// rises by that share of its armor (worn armor pieces plus bonus armor factor) and its maximum
    /// health by that share of its maximum health. A positive Value adds flat points to both.
    ///
    /// Offline DAoC: this handler hung its bonuses on the legacy GameSpellEffect hooks, which the
    /// ECS never calls, and it was registered as AfHitsBuff, a type no Spell row could reach: the
    /// case-insensitive enum parse resolves "AfHitsBuff" to the earlier AFHitsBuff. It now runs
    /// through the artifact ECS bridge (<see cref="IArtifactEffectHandler"/>) and answers
    /// AFHitsBuff, the type the mob, pet and bot brains already treat as a buff. The armor bonus
    /// goes to OtherBonus, which the player armor factor does not cap at the item limit. The
    /// amounts are kept on the target per spell with a count of the live effects that hold
    /// them: the refresh of a pulsing aura swaps the effect instance without stopping or
    /// starting it, and a refresh of an ending effect starts the new instance before the old
    /// one stops. The bonus is added by the first start and removed by the last stop.
    /// </summary>
    [SpellHandler(eSpellType.AFHitsBuff)]
    public class AfHitsBuffSpellHandler : SpellHandler, IArtifactEffectHandler
    {
        public override string ShortDescription => Spell.Value < 0
            ? $"The target's armor and health are improved by {Math.Abs(Spell.Value)}%."
            : $"The target's armor factor and health are improved by {Spell.Value}.";

        public AfHitsBuffSpellHandler(GameLiving caster, Spell spell, SpellLine spellLine) : base(caster, spell, spellLine) { }

        public override bool HasPositiveEffect => true;

        private string AppliedKey => "AfHitsBuff.Applied." + Spell.ID;

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ArtifactSpellEffect.Create(initParams);
        }

        public void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            GameLiving owner = effect.Owner;

            if (owner == null)
                return;

            // { armor factor, health, effects holding them }
            int[] applied = owner.TempProperties.GetProperty<int[]>(AppliedKey);

            if (applied != null)
            {
                applied[2]++;
                return;
            }

            int bonusAF;
            int bonusHP;

            if (Spell.Value < 0)
            {
                double percent = -Spell.Value * 0.01;
                bonusAF = (int) (ArmorOf(owner) * percent);
                bonusHP = (int) (owner.MaxHealth * percent);
            }
            else
            {
                bonusAF = (int) Spell.Value;
                bonusHP = (int) Spell.Value;
            }

            owner.OtherBonus[eProperty.ArmorFactor] += bonusAF;
            owner.AbilityBonus[eProperty.MaxHealth] += bonusHP;
            owner.TempProperties.SetProperty(AppliedKey, new[] { bonusAF, bonusHP, 1 });
            SendUpdates(owner);
        }

        public void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            GameLiving owner = effect.Owner;
            int[] applied = owner?.TempProperties.GetProperty<int[]>(AppliedKey);

            if (applied == null || --applied[2] > 0)
                return;

            owner.TempProperties.RemoveProperty(AppliedKey);
            owner.OtherBonus[eProperty.ArmorFactor] -= applied[0];
            owner.AbilityBonus[eProperty.MaxHealth] -= applied[1];

            if (owner.Health > owner.MaxHealth)
                owner.Health = owner.MaxHealth;

            SendUpdates(owner);
        }

        /// <summary>The armor the percentage applies to: worn armor pieces plus the modified armor factor.</summary>
        private static int ArmorOf(GameLiving owner)
        {
            int armor = 0;

            if (owner.Inventory != null)
            {
                foreach (DbInventoryItem item in owner.Inventory.EquippedItems)
                {
                    if (item.Object_Type >= (int) eObjectType._FirstArmor && item.Object_Type <= (int) eObjectType._LastArmor)
                        armor += item.DPS_AF;
                }
            }

            return armor + Math.Max(0, owner.GetModified(eProperty.ArmorFactor));
        }

        public static void SendUpdates(GameLiving target)
        {
            if (target is GamePlayer player)
            {
                player.Out.SendUpdatePlayer();
                player.Out.SendCharStatsUpdate();
                player.Out.SendUpdateWeaponAndArmorStats();
                player.UpdatePlayerStatus();
            }
        }
    }
}
