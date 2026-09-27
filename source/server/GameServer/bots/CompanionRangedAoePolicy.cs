using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>Saved target-count preference and safe ranged-area damage policy for player-led companions.</summary>
    public static class CompanionRangedAoePolicy
    {
        public const string Off = "off";
        public const int MinimumTargetCount = 2;
        public const int MaximumTargetCount = 8;
        public const int DefaultTargetCount = 3;
        public const string DefaultChoice = "3";

        public static string Choice(PlayerCompanionRecord record) =>
            TryNormalizeChoice(record?.RangedAoePreference, out string choice) ? choice : DefaultChoice;

        public static string NextChoice(string current)
        {
            if (!TryNormalizeChoice(current, out string choice))
                return (DefaultTargetCount + 1).ToString(CultureInfo.InvariantCulture);
            if (choice == Off)
                return MinimumTargetCount.ToString(CultureInfo.InvariantCulture);
            int count = int.Parse(choice, CultureInfo.InvariantCulture);
            return count >= MaximumTargetCount
                ? Off
                : (count + 1).ToString(CultureInfo.InvariantCulture);
        }

        public static string ChoiceLabel(string choice)
        {
            if (!TryNormalizeChoice(choice, out string normalized))
                normalized = DefaultTargetCount.ToString(CultureInfo.InvariantCulture);
            return normalized == Off ? "Off" : $"{normalized}+ enemies";
        }

        public static bool TryNormalizeChoice(string value, out string normalized)
        {
            normalized = string.Empty;
            string candidate = value?.Trim().ToLowerInvariant() ?? string.Empty;
            if (candidate == Off)
            {
                normalized = Off;
                return true;
            }

            if (!int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out int count) ||
                count < MinimumTargetCount || count > MaximumTargetCount)
                return false;

            normalized = count.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        public static int MinimumTargets(PlayerCompanionRecord record) =>
            Choice(record) == Off ? 0 : int.Parse(Choice(record), CultureInfo.InvariantCulture);

        public static int MinimumTargets(GameBot bot) => MinimumTargets(bot?.PlayerCompanionRecord);

        public static bool CanUse(GameBot bot) =>
            CompanionBombingPolicy.IsPlayerLedCompanion(bot) && MinimumTargets(bot) > 0;

        /// <summary>
        /// Ranged area damage is learned from the companion's actual spell list,
        /// not from a class list. PBAoE, cones, and control-only spells stay out.
        /// </summary>
        public static bool IsDamageAreaPayload(Spell spell) => spell is
            { IsHarmful: true, Radius: > 0 } &&
            (spell.Target is eSpellTarget.ENEMY or eSpellTarget.AREA) &&
            BotCasterPriority.IsDamage(spell);

        public static bool IsDamageAreaSpell(Spell spell) =>
            IsDamageAreaPayload(spell) && !spell.IsPBAoE;

        public static bool IsRangedDamageSpell(Spell spell) =>
            spell is { Range: > 0 } && IsDamageAreaSpell(spell);

        /// <summary>
        /// Range-zero ENEMY radius payloads are servant-centered in the native
        /// spell selector. IsPBAoE is true by definition, so classify this
        /// narrowly instead of treating it as an ordinary ranged companion AoE.
        /// </summary>
        public static bool IsServantDamageAreaPayload(Spell spell) => spell is
            { Range: 0, Target: eSpellTarget.ENEMY } && IsDamageAreaPayload(spell);

        /// <summary>
        /// The focus target is also the selected enemy center and, for AREA
        /// spells, the ground target set by BotBrain immediately before casting.
        /// </summary>
        public static GameNPC[] Targets(GameBot bot, GameLiving focus, Spell spell) =>
            Targets(bot, focus, focus, spell);

        /// <summary>
        /// Counts attackable, committed NPCs around the spell handler's actual
        /// area center. Necromancer ENEMY-radius payloads with Range=0 are
        /// centered on the servant, while normal ranged spells use their target.
        /// </summary>
        public static GameNPC[] Targets(GameBot bot, GameLiving focus, GameLiving areaCenter, Spell spell)
        {
            if (!CanUse(bot) || !(IsDamageAreaSpell(spell) || IsServantDamageAreaPayload(spell)) ||
                bot.Group == null ||
                focus is not GameNPC focusNpc || !IsAttackableNpc(bot, focusNpc) ||
                areaCenter is not GameNPC centerNpc || !centerNpc.IsAlive ||
                centerNpc.ObjectState != GameObject.eObjectState.Active ||
                centerNpc.CurrentRegion != bot.CurrentRegion)
                return [];

            int radius = Math.Clamp(spell.Radius, 1, ushort.MaxValue);
            if (focusNpc.CurrentRegion != centerNpc.CurrentRegion || !centerNpc.IsWithinRadius(focusNpc, radius))
                return [];

            GameNPC[] nearby = centerNpc.GetNPCsInRadius((ushort)radius)
                .Where(npc => npc != null && npc.CurrentRegion == centerNpc.CurrentRegion &&
                    centerNpc.IsWithinRadius(npc, radius) && !npc.HasAbility("DamageImmunity"))
                .Append(focusNpc)
                .Distinct()
                .ToArray();

            if (nearby.Any(npc => npc != null && npc.IsAlive && npc.ObjectState == GameObject.eObjectState.Active &&
                    npc.CurrentRegion == centerNpc.CurrentRegion && centerNpc.IsWithinRadius(npc, radius) &&
                    BotPvpCrowdControl.PlayerLike(npc) &&
                    GameServer.ServerRules.IsAllowedToAttack(bot, npc, true)) ||
                centerNpc.GetPlayersInRadius((ushort)radius).Any(player => player != null && player.IsAlive &&
                    player.CurrentRegion == centerNpc.CurrentRegion && centerNpc.IsWithinRadius(player, radius) &&
                    GameServer.ServerRules.IsAllowedToAttack(bot, player, true)))
                return [];

            GameNPC[] hostile = nearby.Where(npc => IsAttackableNpc(bot, npc)).ToArray();
            if (nearby.Any(npc => CompanionAddControl.ProtectsMezz(bot, npc)))
                return [];

            HashSet<GameNPC> engaged = CompanionAddControl.FocusTargets(bot).OfType<GameNPC>().ToHashSet();
            engaged.UnionWith(CompanionAddControl.EngagedWithGroup(bot.Group, nearby));
            engaged.Add(focusNpc); // The selected hostile is the group's committed focus.

            AbstractGameKeep focusKeep = (focusNpc as GameKeepGuard)?.Component?.Keep;
            foreach (GameNPC npc in hostile)
            {
                if (engaged.Contains(npc))
                    continue;

                // Guards that belong to the selected hostile keep are valid
                // area targets even while idle. An unrelated guard or ordinary
                // idle mob in the blast is a bystander, so decline the cast.
                if (focusKeep != null && npc is GameKeepGuard guard &&
                    guard.Component?.Keep == focusKeep && GameServer.KeepManager.IsEnemy(guard, bot))
                    continue;

                return [];
            }

            return hostile.Where(npc => engaged.Contains(npc) ||
                    focusKeep != null && npc is GameKeepGuard guard &&
                    guard.Component?.Keep == focusKeep && GameServer.KeepManager.IsEnemy(guard, bot))
                .Distinct()
                .ToArray();
        }

        public static bool CanCast(GameBot bot, GameLiving focus, Spell spell) =>
            CanCast(bot, focus, focus, spell);

        public static bool CanCast(GameBot bot, GameLiving focus, GameLiving areaCenter, Spell spell)
        {
            if (!CanUse(bot) || focus?.IsAlive != true || focus.ObjectState != GameObject.eObjectState.Active ||
                !IsRangedDamageSpell(spell) || spell.Level > bot.Level ||
                BotSpellPower.BlocksAttackerRotation(bot, spell) ||
                bot.GetSkillDisabledDuration(spell) > 0 || bot.Mana < bot.PowerCost(spell) ||
                spell.CastTime > 0 && bot.IsBeingInterrupted && !spell.Uninterruptible)
                return false;

            int range = spell.CalculateEffectiveRange(bot);
            if (range <= 0 || !bot.IsWithinRadius(focus, range))
                return false;

            return Targets(bot, focus, areaCenter, spell).Length >= MinimumTargets(bot);
        }

        /// <summary>
        /// Necromancer PetSpell wrappers dispatch their Range=0 ENEMY-radius
        /// payload from the servant. Gate that native servant-centered damage
        /// using the same saved ranged-AoE preference and bystander safeguards.
        /// </summary>
        public static bool CanCastServantAreaCommand(GameBot bot, GameLiving focus, GameNPC servant,
            Spell command, Spell payload)
        {
            if (!CanUse(bot) || focus?.IsAlive != true || focus.ObjectState != GameObject.eObjectState.Active ||
                servant?.IsAlive != true || servant.ObjectState != GameObject.eObjectState.Active ||
                command is not { SpellType: eSpellType.PetSpell, IsHarmful: true } ||
                command.Level > bot.Level || !IsServantDamageAreaPayload(payload) ||
                BotSpellPower.BlocksAttackerRotation(bot, command) ||
                bot.GetSkillDisabledDuration(command) > 0 || bot.Mana < bot.PowerCost(command) ||
                !GameServer.ServerRules.IsAllowedToAttack(bot, focus, true))
                return false;

            int range = command.CalculateEffectiveRange(bot);
            if (range <= 0 || !bot.IsWithinRadius(focus, range))
                return false;

            return Targets(bot, focus, servant, payload).Length >= MinimumTargets(bot);
        }

        private static bool IsAttackableNpc(GameBot bot, GameNPC npc) =>
            npc != null && npc.IsAlive && npc.ObjectState == GameObject.eObjectState.Active &&
            npc.CurrentRegion == bot.CurrentRegion && !BotPvpCrowdControl.PlayerLike(npc) &&
            !npc.HasAbility("DamageImmunity") &&
            GameServer.ServerRules.IsAllowedToAttack(bot, npc, true) &&
            (npc is not GameKeepGuard guard || GameServer.KeepManager.IsEnemy(guard, bot));
    }
}
