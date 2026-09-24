using System.Collections.Generic;
using System.Linq;
using DOL.GS;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        private long _nextPveControl;
        private int _pveControlSpellId;
        private long _pveControlCastUntil;
        private bool PveControlInFlight => Body.IsCasting && GameLoop.GameLoopTime < _pveControlCastUntil &&
            Body.castingComponent?.SpellHandler?.Spell?.ID == _pveControlSpellId;

        /// <summary>
        /// Mesmerizes one add for a companion with crowd control duty. Nothing
        /// happens before the group has a focus target, so a pull is never
        /// mezzed. Native levels, mana, range, immunity, and interruption
        /// still decide whether the cast lands.
        /// </summary>
        private bool TryPveAddControl()
        {
            if (!BotPartyRoles.HasCrowdControlDuty(BotBody) || BotBody.Stance == eBotStance.Passive ||
                BotBody.CrowdControlSpells == null || Body.IsCasting || Body.IsIncapacitated ||
                Body.castingComponent?.HasPendingSkillRequests == true || GameLoop.GameLoopTime < _nextPveControl)
                return false;
            _nextPveControl = GameLoop.GameLoopTime + 1000;

            // A pulsing song must be toggled and maintained; the add policy
            // uses only ordinary single casts.
            Spell[] mezzes = BotBody.CrowdControlSpells
                .Where(spell => spell.SpellType == eSpellType.Mesmerize && !spell.IsPulsing && spell.Level <= Body.Level &&
                                Body.GetSkillDisabledDuration(spell) <= 0 && Body.Mana >= BotBody.PowerCost(spell) &&
                                (spell.CastTime <= 0 || spell.Uninterruptible || !Body.IsBeingInterrupted))
                .OrderBy(spell => spell.Radius > 0)
                .ThenByDescending(spell => spell.Level)
                .ToArray();
            if (mezzes.Length == 0)
                return false;

            HashSet<GameLiving> focus = CompanionAddControl.FocusTargets(BotBody);
            if (focus.Count == 0)
                return false;

            Group group = Body.Group;
            GameLiving[] adds = Body.GetNPCsInRadius(CompanionAddControl.ScanRadius).Cast<GameLiving>()
                .Where(npc => npc.IsAlive && !focus.Contains(npc) && !npc.IsMezzed && !npc.IsStealthed &&
                              !BotPvpCrowdControl.PlayerLike(npc) &&
                              GameServer.ServerRules.IsAllowedToAttack(Body, npc, true) &&
                              CompanionEngagementMode.Allows(Body, npc) &&
                              (CompanionAddControl.OnGroupSide(group, npc.TargetObject as GameLiving) || AggroList.ContainsKey(npc)))
                .ToArray();
            if (adds.Length == 0)
                return false;

            foreach (Spell spell in mezzes)
            {
                int range = spell.Target == eSpellTarget.SELF || spell.Range <= 0 ? spell.Radius : spell.CalculateEffectiveRange(Body);
                GameLiving[] eligible = adds.Where(add => !LivingHasEffect(add, spell) && BotSiegeRuntime.Visible(Body, add)).ToArray();
                GameLiving candidate = CompanionAddControl.ChooseAdd(BotBody, spell, eligible, focus, range);
                if (candidate == null || !BotPvpCrowdControl.Reserve(BotBody, candidate, spell.CastTime + 2000))
                    continue;
                GameObject previous = Body.TargetObject;
                Body.TargetObject = candidate;
                if (spell.CastTime > 0)
                {
                    Body.StopMovingOnPath();
                    Body.StopMoving();
                }
                if (!CheckOffensiveSpells(spell))
                {
                    BotPvpCrowdControl.Release(BotBody, candidate);
                    Body.TargetObject = previous;
                    continue;
                }
                _pveControlSpellId = spell.ID;
                _pveControlCastUntil = GameLoop.GameLoopTime + spell.CastTime + 2000;
                return true;
            }
            return false;
        }
    }
}
