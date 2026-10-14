using System;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Session-only human damage calls, shared by companions and their controlled pets.
    /// The owner is the main assist of a DAoC assist train: the companions hit whatever
    /// the main assist hits (weapon swing, damage cast, ordered pet or an explicit
    /// "/assisttrain call") and switch with him.
    /// </summary>
    public static class CompanionAssistTrain
    {
        private sealed class Focus { public GameLiving Target; }
        private static readonly ConditionalWeakTable<GamePlayer, Focus> Targets = new();

        public static bool Active(GameLiving actor)
        {
            if (actor is GamePlayer player)
                return CompanionEngagementMode.TryGetGroupOrder(player, out var order) && order == eCompanionEngagementMode.AssistTrain;
            // A dead main assist calls nothing: until the owner lives again the train
            // fights under the ordinary companion rules instead of standing idle.
            return actor != null && CompanionEngagementMode.Effective(actor) == eCompanionEngagementMode.AssistTrain &&
                CompanionEngagementMode.Leader(actor)?.IsAlive == true;
        }

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

        /// <summary>A living enemy the main assist may call: his region, inside recall range, attackable.</summary>
        public static bool CanCall(GamePlayer player, GameLiving target) =>
            Active(player) && player?.IsAlive == true && target?.IsAlive == true &&
            target.ObjectState == GameObject.eObjectState.Active && target.CurrentRegionID == player.CurrentRegionID &&
            player.IsWithinRadius(target, CompanionEngagementMode.RecallDistance) &&
            GameServer.ServerRules.IsAllowedToAttack(player, target, true);

        /// <summary>Calls the target for every companion; true when accepted. A new call is announced to the owner.</summary>
        public static bool Call(GamePlayer player, GameLiving target, bool announce = true)
        {
            if (!CanCall(player, target)) return false;
            Focus focus = Targets.GetOrCreateValue(player);
            bool changed = focus.Target != target;
            focus.Target = target;
            if (changed && announce)
                player.Out.SendMessage($"Assist train: calling {target.Name}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            foreach (GameBot bot in CompanionSquads.OwnerForceBots(player))
            {
                if (!PlayerLedPullCoordinator.Available(bot, player) || bot.Brain is not BotBrain brain) continue;
                // Drop the previous damage cast immediately, preserving healing and add control.
                if (changed) brain.EnforceCompanionEngagementRange();
                brain.AssistPlayerAttack(target);
            }
            return true;
        }

        /// <summary>
        /// The main assist's swings follow his selection: with weapon attack mode on,
        /// selecting another enemy switches the train at once rather than after the
        /// first contact. A cast keeps its captured target, and a bow on auto-fire
        /// keeps shooting its auto-fire target.
        /// </summary>
        public static bool CallsOnRetarget(bool attacking, bool casting, GameObject autoFireTarget, GameObject previous, GameObject next) =>
            attacking && !casting && next != null && next != previous && (autoFireTarget == null || autoFireTarget == next);

        public static void OnRetarget(GamePlayer player, GameObject previous, GameObject next)
        {
            if (player == null || next is not GameLiving target || !Active(player)) return;
            if (CallsOnRetarget(player.IsAttacking, player.IsCasting, player.rangeAttackComponent?.AutoFireTarget, previous, next))
                Call(player, target);
        }

        /// <summary>The owner's explicit pet attack order is his call; a pet's own aggro never is.</summary>
        public static void OnPetOrder(GamePlayer player, GameLiving target)
        {
            if (Active(player)) Call(player, target);
        }

        /// <summary>/assisttrain call: the selected enemy is called without attacking it (a healer or mezzer main assist).</summary>
        public static string CallSelected(GamePlayer player)
        {
            if (!Active(player)) return "Assist train is not active. Use /assisttrain first.";
            if (player.TargetObject is not GameLiving target || !CanCall(player, target))
                return "Assist train: select a living enemy within range to call it.";
            bool already = Targets.TryGetValue(player, out Focus focus) && focus.Target == target;
            Call(player, target, announce: false);
            return already ? $"Assist train: {target.Name} is already called; companions re-engage it."
                : $"Assist train: calling {target.Name}.";
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
