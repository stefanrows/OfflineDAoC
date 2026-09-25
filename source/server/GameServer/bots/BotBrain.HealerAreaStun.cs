using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.Effects;
using DOL.GS.Keeps;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        private readonly record struct Bomber(GameLiving Caster, int Radius);
        private long _nextHealerAreaStun;
        private int _healerAreaStunSpellId;
        private long _healerAreaStunCastUntil;

        private bool HealerAreaStunInFlight => Body.IsCasting &&
            GameLoop.GameLoopTime < _healerAreaStunCastUntil &&
            Body.castingComponent?.SpellHandler?.Spell?.ID == _healerAreaStunSpellId;

        private bool TryHealerAreaStun()
        {
            if (BotBody?.IsPlayerLedGroup != true ||
                BotBody.CharacterClass?.ID != (int)eCharacterClass.Healer ||
                Body.Group == null || Body.IsCasting || Body.IsIncapacitated ||
                Body.castingComponent?.HasPendingSkillRequests == true ||
                GameLoop.GameLoopTime < _nextHealerAreaStun)
                return false;

            _nextHealerAreaStun = GameLoop.GameLoopTime + 2_000;
            Spell[] spells = (Body.Spells ?? []).Where(spell =>
                    spell?.SpellType == eSpellType.Stun && spell.Radius > 0 &&
                    spell.Target is eSpellTarget.ENEMY or eSpellTarget.AREA &&
                    spell.Level <= Body.Level && Body.Mana >= BotBody.PowerCost(spell) &&
                    Body.GetSkillDisabledDuration(spell) <= 0 &&
                    (spell.CastTime <= 0 || spell.Uninterruptible || !Body.IsBeingInterrupted))
                .OrderBy(spell => spell.CastTime > 0)
                .ThenByDescending(spell => spell.Level)
                .ToArray();
            if (spells.Length == 0) return false;

            Bomber[] bombers = GroupBombers();

            int scanRadius = Math.Clamp(spells.Max(spell => spell.CalculateEffectiveRange(Body) + spell.Radius),
                1_800, 3_000);
            GameLiving[] nearby = Body.GetNPCsInRadius((ushort)scanRadius).Cast<GameLiving>()
                .Concat(Body.GetPlayersInRadius((ushort)scanRadius))
                .Where(target => target.IsAlive && target.ObjectState == GameObject.eObjectState.Active &&
                    target is not GameKeepDoor && target is not GameKeepComponent &&
                    target is not GameRelicDoor && target is not GameSiegeWeapon &&
                    GameServer.ServerRules.IsAllowedToAttack(Body, target, true))
                .ToArray();

            foreach (Spell spell in spells)
            {
                int range = spell.CalculateEffectiveRange(Body);
                foreach (GameLiving center in nearby.Where(target => FightingHealerGroup(target) &&
                             !target.IsStunned && !target.IsMezzed &&
                             CompanionEngagementMode.Allows(Body, target) &&
                             Body.IsWithinRadius(target, range) && BotSiegeRuntime.Visible(Body, target))
                         .OrderByDescending(target => BombSetup(bombers, nearby, target))
                         .ThenByDescending(target => nearby.Count(other => other.IsWithinRadius(target, spell.Radius)))
                         .ThenBy(Body.GetDistanceTo))
                {
                    GameLiving[] affected = nearby.Where(target => target.IsWithinRadius(center, spell.Radius)).ToArray();
                    if (affected.Length < 2 || affected.Any(target => !FightingHealerGroup(target) || target.IsMezzed) ||
                        affected.Count(target => CanStun(target, spell)) < 2 ||
                        CompanionAddControl.BreaksProtectedMezz(BotBody, spell, Body, center))
                        continue;

                    GameObject previous = Body.TargetObject;
                    Body.TargetObject = center;
                    if (spell.CastTime > 0)
                    {
                        Body.StopMovingOnPath();
                        Body.StopMoving();
                    }
                    if (!CheckOffensiveSpells(spell))
                    {
                        Body.TargetObject = previous;
                        continue;
                    }
                    _healerAreaStunSpellId = spell.ID;
                    _healerAreaStunCastUntil = GameLoop.GameLoopTime + spell.CastTime + 2_000;
                    return true;
                }
            }
            return false;
        }

        private bool FightingHealerGroup(GameLiving target)
        {
            if (target == null) return false;
            if (BotPvpCrowdControl.PlayerLike(target))
                return BotPvpCrowdControl.IsInFightWith(Body, target, AggroList.Keys);
            if (AggroList.ContainsKey(target)) return true;
            if (CompanionAddControl.OnGroupSide(Body.Group, target.TargetObject as GameLiving) &&
                (target.InCombat || target.IsAttacking || target.IsCasting)) return true;
            return Body.Group.GetMembersInTheGroup().Any(member =>
                member?.IsAlive == true && (member.IsAttacking || member.IsCasting) &&
                member.TargetObject == target);
        }

        private Bomber[] GroupBombers() => Body.Group.GetMembersInTheGroup()
            .Where(member => member?.IsAlive == true && member.CurrentRegion == Body.CurrentRegion &&
                (member is GameBot bot && CompanionBombingPolicy.CanUseBombs(bot) ||
                 member is GamePlayer player && player.CharacterClass != null &&
                 CompanionBombingPolicy.SupportsClass((eCharacterClass)player.CharacterClass.ID)))
            .Select(member => new Bomber(member, KnownBombSpells(member).Where(spell =>
                spell != null && spell.IsHarmful && spell.IsPBAoE && BotCasterPriority.IsDamage(spell) &&
                spell.Level <= member.Level).Select(spell => spell.Radius).DefaultIfEmpty(0).Max()))
            .Where(bomber => bomber.Radius > 0)
            .ToArray();

        private static IEnumerable<Spell> KnownBombSpells(GameLiving member) => member switch
        {
            GameBot bot => bot.Spells ?? [],
            GamePlayer player => player.GetAllUsableListSpells()
                .SelectMany(entry => entry.Item2.OfType<Spell>()),
            _ => []
        };

        private bool BombSetup(Bomber[] bombers, GameLiving[] enemies, GameLiving center) =>
            bombers.Any(bomber => bomber.Caster.IsWithinRadius(center, bomber.Radius + 100) &&
                enemies.Count(enemy => FightingHealerGroup(enemy) && !enemy.IsMezzed &&
                    bomber.Caster.IsWithinRadius(enemy, bomber.Radius)) >= 2);

        private bool BombGroupReadyForStun() => BotBody?.CharacterClass?.ID == (int)eCharacterClass.Healer &&
            Body.Group != null && GroupBombers().Length > 0 &&
            Body.Group.GetMembersInTheGroup().Where(member => member?.IsAlive == true)
                .All(member => member.HealthPercent >= 65);

        private static bool CanStun(GameLiving target, Spell spell) =>
            !target.IsStunned && !target.HasAbility(Abilities.StunImmunity) &&
            !target.effectListComponent.ContainsEffectForEffectType(eEffect.StunImmunity) &&
            (EffectListService.GetEffectOnTarget(target, eEffect.NPCStunImmunity) is not NpcStunImmunityEffect immunity ||
             immunity.CanApplyNewEffect(spell.Duration));
    }
}
