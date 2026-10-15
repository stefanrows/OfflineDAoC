using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// How an autonomous world bot reacts to another player's hit (BotIncomingFireReaction): the kind of a hit, the reaction delay
    /// by kind, class, Aggression and facing, its cap, the hold and the follower's wait for the call, and when a direct attacker may
    /// override the kill order. Pure rules only: no GameBot, no database.
    /// </summary>
    [TestFixture]
    public class UT_BotIncomingFireReaction
    {
        private static RvrClassTraits Traits(eCharacterClass characterClass) => AutonomousRvrDoctrine.TraitsOf(characterClass);

        [Test]
        public void KindOf_reads_the_attack_type_the_area_flag_and_the_bomber_reach()
        {
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.Spell, true), Is.EqualTo(BotIncomingFireKind.Area), "an AoE DoT tick from range");
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.Spell, true, areaFromReach: true), Is.EqualTo(BotIncomingFireKind.Direct),
                "a bomber standing in the group is a personal call");
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.Spell, false), Is.EqualTo(BotIncomingFireKind.Direct), "a single-target nuke");
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.Ranged, false), Is.EqualTo(BotIncomingFireKind.Direct), "an arrow");
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.MeleeOneHand, false), Is.EqualTo(BotIncomingFireKind.Melee));
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.MeleeTwoHand, true), Is.EqualTo(BotIncomingFireKind.Melee),
                "a weapon proc with a radius is still a melee swing");
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.MeleeDualWield, false), Is.EqualTo(BotIncomingFireKind.Melee));
            Assert.That(BotIncomingFireReaction.KindOf(AttackData.eAttackType.Unknown, false), Is.EqualTo(BotIncomingFireKind.Melee), "a bare-handed swing");
        }

        [Test]
        public void An_area_hit_is_from_reach_for_a_bomber_in_its_radius_or_any_caster_in_melee_reach()
        {
            Assert.That(BotIncomingFireReaction.AreaFromReach(pointBlank: true, distance: 400, radius: 350, meleeReach: 400), Is.True, "a PBAoE bomber inside radius + 100");
            Assert.That(BotIncomingFireReaction.AreaFromReach(pointBlank: true, distance: 460, radius: 350, meleeReach: 400), Is.False, "the bomber stands outside");
            Assert.That(BotIncomingFireReaction.AreaFromReach(pointBlank: false, distance: 1_400, radius: 350, meleeReach: 400), Is.False, "a Cabalist DoT from range");
            Assert.That(BotIncomingFireReaction.AreaFromReach(pointBlank: false, distance: 300, radius: 350, meleeReach: 400), Is.True, "a caster casting an AoE from right next to the bot");
        }

        [Test]
        public void A_mates_hit_is_the_same_splash_for_an_area_spell_and_a_mates_hit_otherwise()
        {
            Assert.That(BotIncomingFireReaction.MateKindOf(true), Is.EqualTo(BotIncomingFireKind.Area));
            Assert.That(BotIncomingFireReaction.MateKindOf(false), Is.EqualTo(BotIncomingFireKind.Mate));
        }

        [Test]
        public void Area_hits_are_never_a_personal_call_and_every_other_kind_is()
        {
            Assert.That(BotIncomingFireReaction.IsPersonalCall(BotIncomingFireKind.Area), Is.False);
            Assert.That(BotIncomingFireReaction.IsPersonalCall(BotIncomingFireKind.Direct), Is.True);
            Assert.That(BotIncomingFireReaction.IsPersonalCall(BotIncomingFireKind.Melee), Is.True);
            Assert.That(BotIncomingFireReaction.IsPersonalCall(BotIncomingFireKind.None), Is.True, "a bot outside the rules keeps the old reaction");
        }

        [Test]
        public void Delay_ranges_order_melee_direct_mate_area_and_none_is_zero()
        {
            (int meleeMin, int meleeMax) = BotIncomingFireReaction.DelayRange(BotIncomingFireKind.Melee);
            (int directMin, int directMax) = BotIncomingFireReaction.DelayRange(BotIncomingFireKind.Direct);
            (int mateMin, int mateMax) = BotIncomingFireReaction.DelayRange(BotIncomingFireKind.Mate);
            (int areaMin, int areaMax) = BotIncomingFireReaction.DelayRange(BotIncomingFireKind.Area);
            Assert.That(meleeMin, Is.LessThan(directMin));
            Assert.That(directMin, Is.LessThan(mateMin));
            Assert.That(mateMin, Is.LessThan(areaMin));
            Assert.That(meleeMax, Is.LessThan(directMax));
            Assert.That(mateMax, Is.LessThan(areaMax));
            Assert.That(areaMax, Is.LessThanOrEqualTo(BotIncomingFireReaction.MaxHoldMilliseconds));
            Assert.That(BotIncomingFireReaction.DelayRange(BotIncomingFireKind.None), Is.EqualTo((0, 0)));
            Assert.That(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.None, Traits(eCharacterClass.Armsman), 50, 0.5), Is.Zero);
        }

        [Test]
        public void Delay_draw_stays_inside_the_range_at_neutral_role_and_aggression()
        {
            RvrClassTraits melee = Traits(eCharacterClass.Mercenary);
            Assert.That(BotIncomingFireReaction.RoleFactor(melee), Is.EqualTo(1.0), "a plain melee class has no role factor");
            Assert.That(BotIncomingFireReaction.AggressionFactor(50), Is.EqualTo(1.3 - 35 / 70d * 0.55).Within(1e-9));
            for (double roll = 0; roll < 1; roll += 0.125)
            {
                int delay = BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Direct, melee, 15, roll);
                Assert.That(delay, Is.InRange((int)(BotIncomingFireReaction.DirectMinMilliseconds * 1.3) - 1,
                    (int)(BotIncomingFireReaction.DirectMaxMilliseconds * 1.3) + 1), $"roll {roll}");
            }
            Assert.That(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Area, melee, 50, 0.0),
                Is.LessThan(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Area, melee, 50, 0.99)), "a higher roll is a longer delay");
        }

        [Test]
        public void The_delay_is_capped_and_an_unseen_attacker_takes_longer_to_find()
        {
            RvrClassTraits caster = Traits(eCharacterClass.Cabalist);
            int seen = BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Area, caster, 50, 0.5);
            int unseen = BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Area, caster, 50, 0.5, unseen: true);
            Assert.That(unseen, Is.GreaterThan(seen), "an attacker behind the bot adds to the delay");
            Assert.That(unseen - seen, Is.InRange(BotIncomingFireReaction.UnseenMinMilliseconds, BotIncomingFireReaction.UnseenMaxMilliseconds + 1));
            int slowest = BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Area, caster, 15, 0.99, unseen: true);
            Assert.That(slowest, Is.EqualTo(BotIncomingFireReaction.MaxHoldMilliseconds), "the slowest caster, hit from behind, still answers within the cap");
        }

        [Test]
        public void A_guard_tank_turns_first_a_pure_caster_last_and_aggression_shortens_the_wait()
        {
            RvrClassTraits tank = Traits(eCharacterClass.Armsman);
            RvrClassTraits caster = Traits(eCharacterClass.Cabalist);
            RvrClassTraits healer = Traits(eCharacterClass.Cleric);
            Assert.That(tank.HasFlag(RvrClassTraits.Guard) && tank.HasFlag(RvrClassTraits.Melee), "the Armsman reads as a guard tank");
            Assert.That(BotIncomingFireReaction.RoleFactor(tank), Is.EqualTo(0.8));
            Assert.That(BotIncomingFireReaction.RoleFactor(caster), Is.EqualTo(1.15));
            Assert.That(BotIncomingFireReaction.RoleFactor(healer), Is.EqualTo(1.0), "a healer is not slowed: it heals during the hold anyway");
            int tankDelay = BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Area, tank, 50, 0.5);
            int casterDelay = BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Area, caster, 50, 0.5);
            Assert.That(tankDelay, Is.LessThan(casterDelay));
            Assert.That(BotIncomingFireReaction.AggressionFactor(15), Is.EqualTo(1.3).Within(1e-9));
            Assert.That(BotIncomingFireReaction.AggressionFactor(85), Is.EqualTo(0.75).Within(1e-9));
            Assert.That(BotIncomingFireReaction.AggressionFactor(0), Is.EqualTo(BotIncomingFireReaction.AggressionFactor(15)), "clamped below");
            Assert.That(BotIncomingFireReaction.AggressionFactor(100), Is.EqualTo(BotIncomingFireReaction.AggressionFactor(85)), "clamped above");
            Assert.That(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Direct, tank, 85, 0.5),
                Is.LessThan(BotIncomingFireReaction.DelayMilliseconds(BotIncomingFireKind.Direct, tank, 15, 0.5)));
        }

        [Test]
        public void The_hold_runs_until_its_end_unless_an_attacker_stands_in_melee_reach()
        {
            Assert.That(BotIncomingFireReaction.Holds(1_000, 2_000, false), Is.True, "still reacting");
            Assert.That(BotIncomingFireReaction.Holds(2_000, 2_000, false), Is.False, "the delay ran out");
            Assert.That(BotIncomingFireReaction.Holds(1_000, 2_000, true), Is.False, "someone is hitting the bot in the face");
            Assert.That(BotIncomingFireReaction.Holds(1_000, 0, false), Is.False, "no hold was drawn");
        }

        [Test]
        public void A_follower_waits_for_the_call_only_while_its_leader_has_not_engaged_and_there_is_a_choice()
        {
            long holdEnd = 5_000;
            Assert.That(BotIncomingFireReaction.WaitsForCall(holdEnd + 500, holdEnd, follower: true, leaderEngaged: false, enemies: 2), Is.True,
                "two enemies, the leader has not called yet");
            Assert.That(BotIncomingFireReaction.WaitsForCall(holdEnd + 500, holdEnd, follower: true, leaderEngaged: false, enemies: 1), Is.False,
                "one enemy: nothing to agree on, go");
            Assert.That(BotIncomingFireReaction.WaitsForCall(holdEnd + 500, holdEnd, follower: true, leaderEngaged: true, enemies: 2), Is.False,
                "the leader is fighting: assist");
            Assert.That(BotIncomingFireReaction.WaitsForCall(holdEnd + 500, holdEnd, follower: false, leaderEngaged: false, enemies: 2), Is.False,
                "a leader or a solo picks itself");
            Assert.That(BotIncomingFireReaction.WaitsForCall(holdEnd + BotIncomingFireReaction.CallWaitMilliseconds, holdEnd, true, false, 2), Is.False,
                "the wait for the call runs out");
            Assert.That(BotIncomingFireReaction.WaitsForCall(holdEnd + 500, 0, true, false, 2), Is.False, "no hold, no wait");
        }

        [Test]
        public void A_direct_attacker_overrides_the_kill_order_only_in_melee_reach_or_without_another_player_target()
        {
            Assert.That(BotIncomingFireReaction.OverridesKillOrder(hasOtherPlayerTarget: false, attackerInMeleeReach: false), Is.True,
                "nothing else player-shaped to fight (a mob is dropped for the ganker)");
            Assert.That(BotIncomingFireReaction.OverridesKillOrder(hasOtherPlayerTarget: true, attackerInMeleeReach: true), Is.True, "someone is on the bot");
            Assert.That(BotIncomingFireReaction.OverridesKillOrder(hasOtherPlayerTarget: true, attackerInMeleeReach: false), Is.False,
                "a far attacker scratching a bot that is already fighting another player does not pull it off its target");
        }

        [Test]
        public void A_null_bot_draws_nothing_and_has_no_caller()
        {
            Assert.That(BotIncomingFireReaction.Applies(null), Is.False);
            Assert.That(BotIncomingFireReaction.Draw(null, BotIncomingFireKind.Area, null), Is.Zero);
            Assert.That(BotIncomingFireReaction.KindOf(null, null), Is.EqualTo(BotIncomingFireKind.None));
            Assert.That(BotIncomingFireReaction.MateKindOf(null, null, true), Is.EqualTo(BotIncomingFireKind.None));
            Assert.That(BotIncomingFireReaction.CallerOf(null), Is.Null);
        }
    }
}
