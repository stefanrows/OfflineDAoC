using System;
using System.Collections.Generic;
using DOL.GS;
using DOL.GS.ServerRules;
using DOL.GS.Spells;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Master Level abilities (TOA_PLAN WP2.4, WP2.6). The class spell choice (CheckSpells)
    /// selects by spell type and knows none of the Master Level types, and pure melee classes
    /// never enter it, so the held abilities get this step of their own: in the combat think
    /// after the priority taunt (for healers in the support role right after their heals), and
    /// out of combat while the bot stands (traps, storms before contact). It costs nothing for a
    /// bot without a usable Master Level ability (an empty list).
    ///
    /// <para>Human pacing (owner rule 2026-10-24): a bot notices a moment with a reaction chance
    /// per think, waits 1-3 seconds, then presses; one ability per think, one decision at a time,
    /// a pause of 8-16 seconds after each press, cooldowns through the normal DisableSkill path,
    /// and nothing when the press would be pointless (then that ability rests 4-8 seconds before
    /// it is judged again). The loop is BotMasterLevelStep; rules and numbers: BotMasterLevelCasting.
    /// Which abilities: BotMasterLevels.UseOf.</para>
    /// </summary>
    public partial class BotBrain : IBotMasterLevelActor
    {
        private readonly BotMasterLevelStep _masterLevelStep = new();
        private Point3D _masterLevelStillAt;
        private long _masterLevelStillSince;
        private long _battlewarderHoldUntil;

        /// <summary>The combat step. True if this think's action was a Master Level press or holding a Battle Warder.</summary>
        private bool TryMasterLevelAbility() => TryMasterLevelStep(combat: true);

        /// <summary>The out-of-combat step, while the bot stands. True if a press started.</summary>
        private bool TryIdleMasterLevelAbility() => !Body.InCombat && TryMasterLevelStep(combat: false);

        private bool TryMasterLevelStep(bool combat)
        {
            IReadOnlyList<Spell> spells = BotBody?.MasterLevelCombatSpells;

            if (spells == null || spells.Count == 0)
                return false;

            long now = GameLoop.GameLoopTime;
            ObserveStanding(now);

            if (combat && HoldBattlewarder(now))
                return true;

            Spell pending = _masterLevelStep.Plan.Spell;

            if (!_masterLevelStep.Think(this, combat, now))
                return false;

            if (BotMasterLevels.UseOf(pending) == BotMasterLevelUse.Battlewarder)
                _battlewarderHoldUntil = now + pending.CastTime + BotMasterLevelCasting.WarderHold(Random.Shared);

            return true;
        }

        #region IBotMasterLevelActor

        IReadOnlyList<Spell> IBotMasterLevelActor.MasterLevelSpells => BotBody?.MasterLevelCombatSpells;

        bool IBotMasterLevelActor.IsBusyForMasterLevel =>
            Body.IsCasting || Body.castingComponent?.HasPendingSkillRequests == true || Body.IsIncapacitated;

        bool IBotMasterLevelActor.IsMasterLevelReady(Spell spell) =>
            spell != null && BotBody != null && BotMasterLevelCasting.IsReady(Body, spell, BotBody.PowerCost(spell));

        bool IBotMasterLevelActor.WantsMasterLevel(BotMasterLevelUse use, Spell spell, bool combat, out Point3D spot) =>
            WantsMasterLevelAbility(use, spell, combat, out spot);

        bool IBotMasterLevelActor.AimMasterLevel(Spell spell, Point3D spot) => SetMasterLevelGroundTarget(spell, spot);

        bool IBotMasterLevelActor.CastMasterLevel(BotMasterLevelUse use, Spell spell) =>
            BotBody != null && CastMasterLevelAbility(BotBody, use, spell);

        #endregion

        /// <summary>
        /// Whether the moment for <paramref name="spell"/> is there, and for a ground-target
        /// ability where the point goes (<paramref name="spot"/>, null for the others).
        /// </summary>
        private bool WantsMasterLevelAbility(BotMasterLevelUse use, Spell spell, bool combat, out Point3D spot)
        {
            spot = null;
            GameLiving target = Body.TargetObject is GameLiving { IsAlive: true } living ? living : null;

            switch (use)
            {
                case BotMasterLevelUse.FightBuff:
                    // Once the fight is on: the target is in reach, and the buff is not running.
                    return target != null && Body.IsWithinRadius(target, Body.MeleeAttackRange + 200) &&
                           !HoldsSpellEffect(Body, spell);

                case BotMasterLevelUse.HealBurst:
                    return AnyGroupMember(spell.Radius, member => member.HealthPercent < BotMasterLevels.HEAL_BURST_HEALTH_PERCENT);

                case BotMasterLevelUse.EnduranceDrain:
                    return target != null && BotMasterLevels.IsPlayerLike(target) && Body.IsWithinRadius(target, spell.Range) &&
                           target.EndurancePercent >= BotMasterLevels.DRAIN_MIN_PERCENT;

                case BotMasterLevelUse.PowerDrain:
                    return target != null && BotMasterLevels.IsPlayerLike(target) && Body.IsWithinRadius(target, spell.Range) &&
                           target.MaxMana > 0 && target.ManaPercent >= BotMasterLevels.DRAIN_MIN_PERCENT;

                case BotMasterLevelUse.CureDisease:
                    return AnyGroupMember(CureReach(spell), member => member.effectListComponent?.ContainsEffectForEffectType(eEffect.Disease) == true);

                case BotMasterLevelUse.CureNearsight:
                    return AnyGroupMember(CureReach(spell), member => member.effectListComponent?.ContainsEffectForEffectType(eEffect.Nearsight) == true);

                case BotMasterLevelUse.Battlewarder:
                    return WantsBattlewarder(spell, out spot);

                case BotMasterLevelUse.SummonTitan:
                    return CountEnemies(BotMasterLevelCasting.TITAN_SIGHT, out _) >= BotMasterLevelCasting.TITAN_ENEMIES && !HasOwnTitan();

                case BotMasterLevelUse.HoldTrap:
                    // The cheap checks first; the scan only for a bot that holds an RvR spot.
                    return !combat && IsHoldingPosition() && IsRvrGround() &&
                           BotMasterLevelCasting.WantsTrap(true, true, OwnPlacedNear<GameMine>(BotMasterLevelCasting.TRAP_SPACING, null));

                case BotMasterLevelUse.SiegeTrap:
                    return EnemySiegeNear(BotMasterLevelCasting.STORM_RADIUS - 50) &&
                           !OwnPlacedNear<GameMine>(BotMasterLevelCasting.STORM_RADIUS, spell.Name);

                case BotMasterLevelUse.DefensiveStorm:
                {
                    // Cheap checks (attacked, standing, group mates near) before any world scan.
                    if (combat)
                        return !AttackedRecently() &&
                               BotMasterLevelCasting.WantsDefensiveStormInFight(
                                   GroupMatesWithin(BotMasterLevelCasting.STORM_RADIUS - 50, fighting: true), false,
                                   OwnPlacedNear<GameStorm>(BotMasterLevelCasting.STORM_SPACING, spell.Name, anyOwnSide: true));

                    if (!IsHoldingPosition() || !IsRvrGround() || GroupMatesWithin(BotMasterLevelCasting.STORM_RADIUS - 50, fighting: false) < 2)
                        return false;

                    int seen = CountEnemies(BotMasterLevelCasting.ENEMY_SIGHT, out int nearest);
                    return BotMasterLevelCasting.WantsStormBeforeContact(true, seen, nearest,
                        seen > 0 && OwnPlacedNear<GameStorm>(BotMasterLevelCasting.STORM_SPACING, spell.Name, anyOwnSide: true), 1);
                }

                case BotMasterLevelUse.OffensiveStorm:
                {
                    // Cheap checks first, then the enemies, and the own-storm scan last.
                    if (combat)
                    {
                        if (AttackedRecently())
                            return false;

                        BotMasterLevelCasting.Cluster? cluster = BotMasterLevelCasting.BestCluster(
                            EnemyPoints(Body, BotMasterLevelCasting.STORM_RADIUS * 2), BotMasterLevelCasting.STORM_RADIUS,
                            BotMasterLevelCasting.CLUSTER_MIN);
                        return BotMasterLevelCasting.WantsStormInFight(cluster, PointOf(Body), false, false) && !OwnStormsCrowd(spell);
                    }

                    if (!IsHoldingPosition() || !IsRvrGround())
                        return false;

                    int seen = CountEnemies(BotMasterLevelCasting.ENEMY_SIGHT, out int nearest);
                    return BotMasterLevelCasting.WantsStormBeforeContact(true, seen, nearest, false, BotMasterLevelCasting.CLUSTER_MIN) &&
                           !OwnStormsCrowd(spell);
                }

                case BotMasterLevelUse.SteerStorm:
                    return WantsSteerStorm(spell, out spot);

                default:
                    return false;
            }
        }

        #region Ground targets

        /// <summary>
        /// A group mate an enemy player or bot is hitting in melee, the bot itself left alone: the
        /// warder goes at the mate's feet, a step toward the attacker, on the floor and in sight.
        /// </summary>
        private bool WantsBattlewarder(Spell spell, out Point3D spot)
        {
            spot = null;

            if (Body.Group == null || HoldsBattlewarderEffect() || EnemyInMeleeWith(Body) != null)
                return false;

            int reach = (spell.Range > 0 ? spell.Range : 1_500) - 100;

            foreach (GameLiving mate in Body.Group.GetMembersInTheGroup())
            {
                if (mate == Body || mate is not { IsAlive: true, ObjectState: GameObject.eObjectState.Active } || !Body.IsWithinRadius(mate, reach))
                    continue;

                GameLiving attacker = EnemyInMeleeWith(mate);

                if (attacker == null)
                    continue;

                Point3D point = BotMasterLevelCasting.OnFloor(Body, BotMasterLevelCasting.WarderSpot(PointOf(mate), PointOf(attacker)));

                if (point != null && BotMasterLevelCasting.InSight(Body, point))
                {
                    spot = point;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// One of the bot's own hostile storms near an enemy cluster it does not yet cover: the
        /// Vortex point goes just behind the storm, so the push carries it into the enemies.
        /// </summary>
        private bool WantsSteerStorm(Spell spell, out Point3D spot)
        {
            spot = null;
            int range = spell.Range > 0 ? spell.Range : 1_500;

            foreach (GameNPC npc in Body.GetNPCsInRadius((ushort)Math.Min(range, ushort.MaxValue)))
            {
                if (npc is not GameStorm { Movable: true } storm || storm.Owner != Body || storm.Friendly ||
                    storm.ObjectState != GameObject.eObjectState.Active)
                    continue;

                Point3D stormAt = PointOf(storm);
                BotMasterLevelCasting.Cluster? cluster = BotMasterLevelCasting.BestCluster(
                    EnemyPoints(storm, BotMasterLevelCasting.STEER_MAX_DISTANCE + BotMasterLevelCasting.STORM_RADIUS),
                    BotMasterLevelCasting.STORM_RADIUS, BotMasterLevelCasting.CLUSTER_MIN);

                if (!BotMasterLevelCasting.WorthSteering(stormAt, cluster))
                    continue;

                Point3D point = BotMasterLevelCasting.OnFloor(Body, BotMasterLevelCasting.SteerSpot(stormAt, cluster.Value.Centre));

                if (point != null && Body.IsWithinRadius(point, range) && BotMasterLevelCasting.InSight(Body, point))
                {
                    spot = point;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Sets the ground target a player would: the chosen point, now and then a little off
        /// (the aim), back on the floor, in range and in sight. False if no such point is left.
        /// </summary>
        private bool SetMasterLevelGroundTarget(Spell spell, Point3D spot)
        {
            int range = spell.Range > 0 ? spell.Range : 1_500;
            Point3D aimed = BotMasterLevelCasting.OnFloor(Body, BotMasterLevelCasting.Aim(spot, Random.Shared));

            if (aimed == null || !Body.IsWithinRadius(aimed, range) || !BotMasterLevelCasting.InSight(Body, aimed))
                aimed = spot;

            if (!Body.IsWithinRadius(aimed, range))
                return false;

            Body.SetGroundTarget(aimed.X, aimed.Y, aimed.Z);
            Body.GroundTargetInView = true;
            return Body.GroundTarget.IsValid;
        }

        #endregion

        #region Battle Warder hold

        /// <summary>
        /// The warder stays only while its caster neither moves nor casts, so a Convoker that has
        /// planted one stands and watches for 8-15 seconds, as a player would, unless it is hit
        /// itself or badly hurt. Then the normal fight resumes and the warder goes with the next cast.
        /// </summary>
        private bool HoldBattlewarder(long now)
        {
            if (_battlewarderHoldUntil == 0)
                return false;

            if (now >= _battlewarderHoldUntil || !HoldsBattlewarderEffect() || Body.HealthPercent < 50 ||
                Body.LastAttackedByEnemyTick > now - 2_000)
            {
                _battlewarderHoldUntil = 0;
                return false;
            }

            return true;
        }

        private bool HoldsBattlewarderEffect()
        {
            if (Body.effectListComponent == null)
                return false;

            foreach (ECSGameSpellEffect effect in Body.effectListComponent.GetSpellEffects())
            {
                if (effect is BattlewarderECSEffect { Warder: not null })
                    return true;
            }

            return false;
        }

        #endregion

        #region What the bot sees

        private static Point3D PointOf(GameObject obj) => new(obj.X, obj.Y, obj.Z);

        /// <summary>Standing within a small drift of one spot since <see cref="_masterLevelStillSince"/>.</summary>
        private void ObserveStanding(long now)
        {
            Point3D here = PointOf(Body);

            if (Body.IsMoving || _masterLevelStillAt == null ||
                !BotMasterLevelCasting.Within(here, _masterLevelStillAt, BotMasterLevelCasting.HOLD_STILL_DRIFT))
            {
                _masterLevelStillAt = here;
                _masterLevelStillSince = now;
            }
        }

        /// <summary>The bot has stood still a while and its group leader is not walking off.</summary>
        private bool IsHoldingPosition()
        {
            if (Body.IsMoving || GameLoop.GameLoopTime - _masterLevelStillSince < BotMasterLevelCasting.HOLD_STILL_MS)
                return false;

            GameLiving leader = Body.Group?.LivingLeader;
            return leader == null || leader == Body || !leader.IsMoving;
        }

        private bool IsRvrGround() => Body.CurrentRegion?.IsRvR == true || Body.CurrentZone?.IsRvR == true;

        private bool AttackedRecently() =>
            Body.LastAttackedByEnemyTick > GameLoop.GameLoopTime - BotMasterLevelCasting.NOT_ATTACKED_MS;

        /// <summary>Enemy players and bots around <paramref name="center"/>: alive, attackable, not hidden.</summary>
        private IEnumerable<GameLiving> Enemies(GameObject center, int radius)
        {
            foreach (GameLiving living in MasterlevelHandling.PlayersAndBotsAround(center, (ushort)Math.Min(radius, ushort.MaxValue)))
            {
                if (living != Body && living.IsAlive && !living.IsStealthed && PvpCombatant.IsPlayerShaped(living) &&
                    GameServer.ServerRules.IsAllowedToAttack(Body, living, true))
                    yield return living;
            }
        }

        private int CountEnemies(int radius, out int nearest)
        {
            int count = 0;
            nearest = int.MaxValue;

            foreach (GameLiving enemy in Enemies(Body, radius))
            {
                count++;
                nearest = Math.Min(nearest, Body.GetDistanceTo(enemy));
            }

            return count;
        }

        private List<Point3D> EnemyPoints(GameObject center, int radius)
        {
            var points = new List<Point3D>();

            foreach (GameLiving enemy in Enemies(center, radius))
                points.Add(PointOf(enemy));

            return points;
        }

        /// <summary>An enemy player or bot hitting <paramref name="victim"/> in melee.</summary>
        private GameLiving EnemyInMeleeWith(GameLiving victim)
        {
            foreach (GameLiving enemy in Enemies(victim, BotMasterLevelCasting.MELEE_REACH))
            {
                if (enemy.TargetObject == victim && enemy.IsAttacking && enemy.ActiveWeaponSlot != eActiveWeaponSlot.Distance)
                    return enemy;
            }

            return null;
        }

        private int GroupMatesWithin(int radius, bool fighting)
        {
            int count = 0;

            if (Body.Group == null)
                return 0;

            foreach (GameLiving member in Body.Group.GetMembersInTheGroup())
            {
                if (member != Body && member.IsAlive && (!fighting || member.InCombat) && Body.IsWithinRadius(member, radius))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// One of the bot's own fonts, traps or storms within <paramref name="radius"/>, of the given
        /// name when one is given. With <paramref name="anyOwnSide"/> a friendly caster's object of that
        /// name counts too (a second Dazzling Array on the same spot is a waste).
        /// </summary>
        /// <summary>One storm of a kind on a spot, and at most two of the bot's own there at all.</summary>
        private bool OwnStormsCrowd(Spell spell) =>
            OwnPlacedCount<GameStorm>(BotMasterLevelCasting.STORM_SPACING, spell.Name) > 0 ||
            OwnPlacedCount<GameStorm>(BotMasterLevelCasting.STORM_SPACING, null) >= BotMasterLevelCasting.MAX_OWN_STORMS;

        private bool OwnPlacedNear<T>(int radius, string name, bool anyOwnSide = false) where T : GameNPC =>
            OwnPlacedCount<T>(radius, name, anyOwnSide) > 0;

        private int OwnPlacedCount<T>(int radius, string name, bool anyOwnSide = false) where T : GameNPC
        {
            int count = 0;

            foreach (GameNPC npc in Body.GetNPCsInRadius((ushort)radius))
            {
                if (npc is not T || npc.ObjectState != GameObject.eObjectState.Active || (name != null && npc.Name != name))
                    continue;

                GameLiving owner = npc switch
                {
                    GameMine mine => mine.Owner,
                    GameStorm storm => storm.Owner,
                    GameFont font => font.Owner,
                    _ => null,
                };

                if (owner == Body || (anyOwnSide && owner != null && !GameServer.ServerRules.IsAllowedToAttack(Body, owner, true)))
                    count++;
            }

            return count;
        }

        private bool HasOwnTitan()
        {
            foreach (GameNPC npc in Body.GetNPCsInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                if (npc.Brain is TitanBrain titan && titan.Owner == Body)
                    return true;
            }

            return false;
        }

        private bool EnemySiegeNear(int radius)
        {
            foreach (GameNPC npc in Body.GetNPCsInRadius((ushort)radius))
            {
                if (npc is GameSiegeWeapon { IsAlive: true } && GameServer.ServerRules.IsAllowedToAttack(Body, npc, true))
                    return true;
            }

            return false;
        }

        #endregion

        private static int CureReach(Spell spell) => spell.Range > 0 ? spell.Range : spell.Radius;

        /// <summary>The bot itself or a living group member within reach that matches.</summary>
        private bool AnyGroupMember(int reach, System.Func<GameLiving, bool> matches)
        {
            if (Body.Group == null)
                return Body.IsAlive && matches(Body);

            foreach (GameLiving member in Body.Group.GetMembersInTheGroup())
            {
                if (member is { IsAlive: true, ObjectState: GameObject.eObjectState.Active } &&
                    (member == Body || Body.IsWithinRadius(member, reach)) && matches(member))
                    return true;
            }

            return false;
        }

        private static bool HoldsSpellEffect(GameLiving living, Spell spell)
        {
            if (living?.effectListComponent == null)
                return false;

            foreach (ECSGameSpellEffect effect in living.effectListComponent.GetSpellEffects())
            {
                if (effect?.SpellHandler?.Spell?.ID == spell.ID)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Casts on the Master Level line, so effects that look the line up find it. Drains go at the
        /// current target; everything else centres on the bot (buffs, the battle cry, cures, traps,
        /// storms, the titan) or on its ground target (Battlewarder, Vacuum Vortex), so the target is
        /// the bot for the request and the enemy again right after.
        /// </summary>
        private bool CastMasterLevelAbility(GameBot bot, BotMasterLevelUse use, Spell spell)
        {
            SpellLine line = SkillBase.GetSpellLine(bot.MasterLevelPath, false) ?? m_mobSpellLine;

            if (use is BotMasterLevelUse.EnduranceDrain or BotMasterLevelUse.PowerDrain)
                return Body.CastSpell(spell, line);

            if (spell.CastTime > 0 && Body.IsMoving)
                Body.StopMoving();

            GameObject enemy = Body.TargetObject;
            Body.TargetObject = Body;

            try
            {
                return Body.CastSpell(spell, line);
            }
            finally
            {
                Body.TargetObject = enemy;
            }
        }
    }
}
