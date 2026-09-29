using System;
using System.Linq;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Wave 4 of livelier RvR bots (P1 roles are fluid by spec, P8 support jobs
    /// before the assist train). Autonomous RvR world bots only: companions,
    /// player-led groups and PvE parties never enter these paths.
    /// </summary>
    public partial class BotBrain
    {
        private static readonly DOL.Logging.Logger SupportLog = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrSupportOffense));
        private static readonly RvrSupportCounters SupportCounters = new();

        private long _supportLastTurn = long.MinValue;
        private double _supportRoll;
        private int _supportOffenseHealthGate = AutonomousRvrSupportOffense.OffenseGroupMinHealthPercent;
        private RvrSupportAction? _supportLastAction;
        private int _supportOffenseSpellId;
        private long _supportOffenseCastUntil;

        private bool SupportOffenseInFlight => Body.IsCasting && GameLoop.GameLoopTime < _supportOffenseCastUntil &&
            Body.castingComponent?.SpellHandler?.Spell?.ID == _supportOffenseSpellId;

        /// <summary>D5113: a group Bard gives up melee for its songs; autonomous RvR Bards never join the melee.</summary>
        private bool IsAutonomousRvrGroupBard() =>
            BotBody?.CharacterClass?.ID == (int)eCharacterClass.Bard && BotBody.Group?.MemberCount > 1 &&
            AutonomousRvrDoctrineRuntime.Applies(BotBody);

        /// <summary>
        /// Called by <see cref="PerformGroupSupport"/> (Cleric, Druid, Healer).
        /// Returns true when this turn's action was taken; false keeps today's
        /// support order (heals, area stun, CC sweep, buffs).
        /// </summary>
        private bool TryAutonomousRvrSupportTurn()
        {
            if (!TryDecideRvrSupport(out eCharacterClass characterClass, out RvrSupportAction action, out GameLiving callerTarget))
                return false;
            switch (action)
            {
                case RvrSupportAction.Control:
                    return NoteSupport(characterClass, action, TryRvrSupportControl(characterClass));
                case RvrSupportAction.Offense:
                    // Heals still win for anyone the ordinary heal check wants to top up.
                    return CheckHeals() || NoteSupport(characterClass, action, TryRvrSupportOffense(characterClass, callerTarget));
                default:
                    NoteSupport(characterClass, action, true);
                    return false;
            }
        }

        /// <summary>
        /// Counts an episode only when the behaviour changed: an offense or
        /// control episode needs a real cast or pet order, a heal-only episode
        /// starts when the decision falls back to heals. Returns <paramref name="acted"/>.
        /// </summary>
        private bool NoteSupport(eCharacterClass characterClass, RvrSupportAction action, bool acted)
        {
            if (acted && _supportLastAction != action)
            {
                _supportLastAction = action;
                SupportCounters.Record(characterClass, action);
            }
            FlushSupportCounters(GameLoop.GameLoopTime);
            return acted;
        }

        /// <summary>
        /// The fight turn of the healing hybrids that are not exclusive support
        /// (Shaman, Friar, Warden, Paladin). Only the cave Shaman's CC-first
        /// changes behaviour; staff Friar and battle Warden already melee the
        /// assist target when nobody needs a heal.
        /// </summary>
        private void PerformHybridHealerTurn()
        {
            if (TryDecideRvrSupport(out eCharacterClass characterClass, out RvrSupportAction action, out _) &&
                action == RvrSupportAction.Control &&
                NoteSupport(characterClass, action, TryRvrSupportControl(characterClass)))
                return;
            if (!CheckHeals())
                AttackMostWanted();
        }

        private bool TryDecideRvrSupport(out eCharacterClass characterClass, out RvrSupportAction action, out GameLiving callerTarget)
        {
            characterClass = default;
            action = RvrSupportAction.HealOnly;
            callerTarget = null;
            if (BotBody?.CharacterClass == null || Body.Group == null || !AutonomousRvrDoctrineRuntime.Applies(BotBody))
                return false;
            characterClass = (eCharacterClass)BotBody.CharacterClass.ID;
            if (Array.IndexOf(AutonomousRvrSupportOffense.CountedClasses, characterClass) < 0)
                return false;

            long now = GameLoop.GameLoopTime;
            if (now - _supportLastTurn > AutonomousRvrSupportOffense.FightGapMilliseconds)
            {
                // One "stays on heals anyway" roll per fight, not per tick.
                _supportRoll = Util.RandomDouble();
                // Per-bot jitter so eligible smiters do not all switch on the same tick.
                _supportOffenseHealthGate = 70 + Util.Random(10);
                _supportLastAction = null;
            }
            _supportLastTurn = now;

            GameLiving[] members = Body.Group.GetMembersInTheGroup()
                .Where(member => member != null && member.IsAlive && member.ObjectState == GameObject.eObjectState.Active &&
                    member.CurrentRegionID == Body.CurrentRegionID)
                .ToArray();
            int groupMin = members.Length == 0 ? 100 : members.Min(member => (int)member.HealthPercent);
            bool secondHealer = members.Any(member => member != Body && CanCoverHealsNow(member));
            bool needsCure = members.Any(member => member.IsMezzed && member != Body || member.IsDiseased || member.IsPoisoned);
            RvrSupportStyle style = AutonomousRvrSupportOffense.StyleOf(characterClass, BotBody.BotSpec?.SpecType ?? eSpecType.None);
            callerTarget = AutonomousRvrDoctrineRuntime.CallerTarget(BotBody);
            bool mateAttacked = style == RvrSupportStyle.ControlFirst && BotBody.CanCastCrowdControlSpells &&
                MateAttackedByControllable(members);

            action = AutonomousRvrSupportOffense.Decide(style, secondHealer, groupMin, Body.ManaPercent, needsCure,
                callerTarget != null, _supportRoll, mateAttacked, _supportOffenseHealthGate);
            return true;
        }

        /// <summary>A second healer who can actually cast a heal now: near, free to act, with power.</summary>
        private bool CanCoverHealsNow(GameLiving member) =>
            Body.IsWithinRadius(member, 2_000) && !member.IsStunned && !member.IsMezzed && !member.IsSilenced &&
            member.ManaPercent > 20 && CoversHeals(member);

        private static bool CoversHeals(GameLiving member) => member switch
        {
            GameBot bot when bot.CharacterClass != null =>
                AutonomousRvrSupportOffense.CoversHeals((eCharacterClass)bot.CharacterClass.ID, bot.BotSpec?.SpecType ?? eSpecType.None),
            GamePlayer player when player.CharacterClass != null =>
                AutonomousRvrSupportOffense.CoversHeals((eCharacterClass)player.CharacterClass.ID, eSpecType.None),
            _ => false,
        };

        /// <summary>An enemy in this fight is hitting or casting at a group mate and can still be mezzed or stunned.</summary>
        private bool MateAttackedByControllable(GameLiving[] members) =>
            Body.GetNPCsInRadius(1800).Where(BotPvpCrowdControl.PlayerLike).Cast<GameLiving>()
                .Concat(Body.GetPlayersInRadius(1800))
                .Any(enemy => BotSiegeRuntime.LegalEnemy(Body, enemy) && !enemy.IsMezzed && !enemy.IsStunned &&
                    !enemy.IsStealthed && (enemy.IsAttacking || enemy.IsCasting) &&
                    enemy.TargetObject is GameLiving victim && members.Contains(victim) &&
                    !(enemy.effectListComponent.ContainsEffectForEffectType(eEffect.MezImmunity) &&
                      enemy.effectListComponent.ContainsEffectForEffectType(eEffect.StunImmunity)));

        private bool TryRvrSupportControl(eCharacterClass characterClass) =>
            characterClass == eCharacterClass.Healer && TryHealerAreaStun() || TryPvpCrowdControl();

        /// <summary>
        /// Damage from the bot's own lines on the caller's target: the smite
        /// Cleric smites, the nature Druid sends its pet and nukes. Single-target
        /// damage, DoT and disease only; CC stays with the control path.
        /// </summary>
        private bool TryRvrSupportOffense(eCharacterClass characterClass, GameLiving target)
        {
            if (target?.IsAlive != true || !CanAggroTarget(target))
                return false;
            bool acted = false;
            if (characterClass == eCharacterClass.Druid && Body.ControlledBrain is { Body.IsAlive: true } pet &&
                Body.IsWithinRadius(target, GROUP_DEFENSE_ASSIST_RADIUS))
            {
                if (pet is not ControlledMobBrain controlled || controlled.OrderedAttackTarget != target)
                {
                    pet.Attack(target);
                    acted = true;
                }
            }
            if (Body.IsCasting || Body.IsIncapacitated || Body.castingComponent?.HasPendingSkillRequests == true ||
                !BotSiegeRuntime.Visible(Body, target))
                return acted;

            GameObject previous = Body.TargetObject;
            Body.TargetObject = target;
            foreach (Spell spell in (Body.InstantHarmfulSpells ?? []).Where(IsSupportOffenseSpell)
                         .OrderByDescending(spell => spell.Level))
            {
                if (spell.Level > Body.Level || Body.GetSkillDisabledDuration(spell) > 0 ||
                    !Body.IsWithinRadius(target, Math.Max(1, spell.CalculateEffectiveRange(Body))) ||
                    !CheckInstantOffensiveSpells(spell))
                    continue;
                return true;
            }
            foreach (Spell spell in (Body.HarmfulSpells ?? []).Where(IsSupportOffenseSpell)
                         .OrderByDescending(spell => spell.Level))
            {
                if (!CanCastOffensiveSpell(spell) || Body.IsBeingInterrupted && !spell.Uninterruptible)
                    continue;
                Body.StopMovingOnPath();
                Body.StopMoving();
                if (!CheckOffensiveSpells(spell))
                    continue;
                _supportOffenseSpellId = spell.ID;
                _supportOffenseCastUntil = GameLoop.GameLoopTime + spell.CastTime + 2_000;
                return true;
            }
            Body.TargetObject = previous;
            return acted;
        }

        private bool IsSupportOffenseSpell(Spell spell) => spell is { IsHarmful: true, Radius: 0 } &&
            (BotCasterPriority.IsDamage(spell) || spell.SpellType is eSpellType.Disease or eSpellType.DamageOverTime) &&
            BotBody.CrowdControlSpells?.Any(control => control.ID == spell.ID) != true;

        private void RecordRvrHealerAreaStun()
        {
            if (!AutonomousRvrDoctrineRuntime.Applies(BotBody))
                return;
            SupportCounters.RecordAreaStun();
            FlushSupportCounters(GameLoop.GameLoopTime);
        }

        private static void FlushSupportCounters(long now)
        {
            if (!SupportCounters.Due(now))
                return;
            foreach (string line in SupportCounters.Flush(now))
                if (SupportLog.IsInfoEnabled) SupportLog.Info(line);
        }
    }
}
