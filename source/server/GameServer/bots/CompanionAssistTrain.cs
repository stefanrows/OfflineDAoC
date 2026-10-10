using System;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>Session-only human damage calls, shared by companions and their controlled pets.</summary>
    public static class CompanionAssistTrain
    {
        private sealed class Focus { public GameLiving Target; }
        private static readonly ConditionalWeakTable<GamePlayer, Focus> Targets = new();

        public static bool Active(GameLiving actor) => actor is GamePlayer player
            ? CompanionEngagementMode.TryGetGroupOrder(player, out var order) && order == eCompanionEngagementMode.AssistTrain
            : actor != null && CompanionEngagementMode.Effective(actor) == eCompanionEngagementMode.AssistTrain;

        public static void Reset(GamePlayer player) => Targets.Remove(player);

        public static GameLiving Target(GamePlayer player)
        {
            if (player == null || !Active(player) || !Targets.TryGetValue(player, out Focus focus)) return null;
            GameLiving target = focus.Target;
            if (!player.IsAlive || target?.IsAlive != true || target.ObjectState != GameObject.eObjectState.Active ||
                target.CurrentRegionID != player.CurrentRegionID ||
                !player.IsWithinRadius(target, CompanionEngagementMode.RecallDistance) ||
                !GameServer.ServerRules.IsAllowedToAttack(player, target, true))
            {
                focus.Target = null;
                return null;
            }
            return target;
        }

        public static void Call(GamePlayer player, GameLiving target)
        {
            if (!Active(player) || player?.IsAlive != true || target?.IsAlive != true ||
                target.ObjectState != GameObject.eObjectState.Active || target.CurrentRegionID != player.CurrentRegionID ||
                !player.IsWithinRadius(target, CompanionEngagementMode.RecallDistance) ||
                !GameServer.ServerRules.IsAllowedToAttack(player, target, true)) return;
            Focus focus = Targets.GetOrCreateValue(player);
            bool changed = focus.Target != target;
            focus.Target = target;
            foreach (GameBot bot in CompanionSquads.OwnerForceBots(player))
            {
                if (!PlayerLedPullCoordinator.Available(bot, player) || bot.Brain is not BotBrain brain) continue;
                // Drop the previous damage cast immediately, preserving healing and add control.
                if (changed) brain.EnforceCompanionEngagementRange();
                brain.AssistPlayerAttack(target);
            }
        }

        public static bool IsControl(Spell spell) => spell != null && spell.Damage == 0 &&
            spell.SpellType is eSpellType.Mesmerize or eSpellType.Mez or eSpellType.Stun or eSpellType.SpeedDecrease;

        public static bool IsDamage(Spell spell) => spell?.IsHarmful == true && spell.SpellType is not (eSpellType.Mesmerize or eSpellType.Mez) && !IsControl(spell) &&
            (BotCasterPriority.IsDamage(spell) || spell.SpellType is eSpellType.DamageOverTime or eSpellType.DamageOverTimeNoVariance);

        public static double DamagePriority(Spell spell) => !IsDamage(spell) ? 0 :
            Math.Abs(spell.Damage) / Math.Max(1.5, spell.CastTime / 1000.0);

        public static bool AllowsSpell(GameLiving actor, Spell spell, GameLiving target)
        {
            // Human spells and autonomous bots keep their own decisions.
            if (actor is GamePlayer || !Active(actor) || spell == null) return true;
            // PetSpell wrappers must obey the actual servant spell, not the wrapper metadata.
            if (spell.SubSpellID > 0 && SkillBase.GetSpellByID(spell.SubSpellID) is Spell child &&
                (child.Radius > 0 || child.Target is eSpellTarget.AREA or eSpellTarget.CONE) && child.IsHarmful && !IsControl(child))
                return false;
            bool offensive = spell.IsHarmful || spell.SpellType is eSpellType.DirectDamage or eSpellType.DirectDamageNoVariance or
                eSpellType.Lifedrain or eSpellType.LifedrainNoVariance or eSpellType.DamageOverTime or
                eSpellType.DamageOverTimeNoVariance or eSpellType.DirectDamageWithDebuff or
                eSpellType.DirectDamageWithDebuffNoVariance or eSpellType.DamageSpeedDecrease;
            if (!offensive) return true;
            if (IsControl(spell))
                return target != null && (spell.SpellType == eSpellType.Stun || !CompanionEngagementMode.Allows(actor, target)) &&
                    !target.IsMezzed && CompanionEngagementMode.Allows(actor, target, crowdControl: true);
            return spell.Radius == 0 && spell.Target is not (eSpellTarget.AREA or eSpellTarget.CONE) &&
                CompanionEngagementMode.Allows(actor, target);
        }
    }
}
