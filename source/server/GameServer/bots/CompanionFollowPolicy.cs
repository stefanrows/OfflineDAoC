using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>Player-led companions only. Never alters autonomous world bots.</summary>
    public static class CompanionFollowPolicy
    {
        public const int SettleMilliseconds = 600;
        private static readonly ConditionalWeakTable<GameBot, State> States = new();

        public sealed class State
        {
            public GameLiving Leader;
            public Vector3 LastLeaderPosition, FormationPoint, LeaderVelocity;
            public bool Observed, BuffsBlocked;
            public long BuffsAfter, FormationUntil, LastObservation;

            public bool Observe(Vector3 position, bool moving, long now)
            {
                if (Observed && now > LastObservation)
                    LeaderVelocity = moving ? (position - LastLeaderPosition) * (1000f / (now - LastObservation)) : Vector3.Zero;
                if (moving || Observed && Vector3.DistanceSquared(position, LastLeaderPosition) > 4 * 4)
                    BuffsAfter = now + SettleMilliseconds;
                Observed = true;
                LastLeaderPosition = position;
                LastObservation = now;
                return moving || now < BuffsAfter;
            }
        }

        // A companion's own group and its follow anchor's group are the same
        // instance for ordinary companions and for squad leaders following the
        // owner directly. A squad member's anchor is its squad leader, who
        // shares the same squad Group, so the same "grouped together" check
        // holds there too; only the "anchor == owner" case needs ownership
        // instead of group equality, since a squad leader's own Group is the
        // squad, never the owner's group (task 42/43).
        public static bool Applies(GameBot bot) => bot is { IsAutonomousWorldBot: false, IsPlayerLedGroup: true } &&
            bot.FollowAnchor is { IsAlive: true, ObjectState: GameObject.eObjectState.Active } leader &&
            bot.Group != null && bot.Group.IsInTheGroup(bot) &&
            (leader is GamePlayer owner ? bot.Owner == owner : bot.Group == leader.Group) &&
            bot.CurrentRegion == leader.CurrentRegion;

        private static State For(GameBot bot)
        {
            State state = States.GetOrCreateValue(bot);
            if (state.Leader != bot.FollowAnchor)
            {
                state.Leader = bot.FollowAnchor;
                state.Observed = false;
                state.LeaderVelocity = Vector3.Zero;
                state.BuffsAfter = state.FormationUntil = 0;
                state.BuffsBlocked = false;
            }
            return state;
        }

        public static bool WaitingForLeaderToStop(GameBot bot)
        {
            if (!Applies(bot)) return false;
            var leader = bot.FollowAnchor;
            return For(bot).Observe(new(leader.X, leader.Y, leader.Z), leader.IsMoving, GameLoop.GameLoopTime);
        }

        public static bool IsOrdinaryBuff(GameLiving caster, Spell spell)
        {
            if (spell == null || spell.IsHarmful || spell.IsHealing || BotSongTwistPolicy.IsMobileSong(caster, spell)) return false;
            if (spell.SpellType == eSpellType.PetSpell && spell.SubSpellID > 0)
            {
                Spell payload = SkillBase.GetSpellByID(spell.SubSpellID);
                return payload != null && payload.SpellType != eSpellType.PetSpell && IsOrdinaryBuff(caster, payload);
            }
            return BotBrain.IsMaintainableClassBuff(spell);
        }

        public static bool DeferBuff(GameLiving caster, Spell spell) => caster is GameBot bot &&
            Applies(bot) && IsOrdinaryBuff(bot, spell) && WaitingForLeaderToStop(bot);

        // Do not discard a queued heal, resurrection, attack or mobile song.
        public static bool CancelOrdinaryBuffs(GameLiving caster)
        {
            var casting = caster?.castingComponent;
            if (casting == null) return false;
            Spell active = casting.SpellHandler?.Spell;
            Spell queued = casting.QueuedSpellHandler?.Spell;
            bool pending = casting.TryPeekPendingSpell(out Spell requested);
            if (active == null && queued == null && !pending) return false;
            if (active != null && !IsOrdinaryBuff(caster, active) ||
                queued != null && !IsOrdinaryBuff(caster, queued) ||
                pending && !IsOrdinaryBuff(caster, requested)) return false;
            caster.StopCurrentSpellcast();
            return true;
        }

        // True on settling, so existing selectors can retry without retaining
        // a canceled-cast backoff. No new timer, population scan or persistence.
        public static bool ObserveAndCancelBuffs(GameBot bot)
        {
            if (!Applies(bot)) return false;
            State state = For(bot);
            bool waiting = WaitingForLeaderToStop(bot);
            bool settled = state.BuffsBlocked && !waiting;
            state.BuffsBlocked = waiting;
            if (waiting)
            {
                CancelOrdinaryBuffs(bot);
                if (bot.ControlledBrain is NecromancerPetBrain servant)
                    servant.CancelCompanionTravelBuffs();
            }
            return settled;
        }

        public static void BeginFormation(GameBot bot, Vector3 point)
        {
            if (!Applies(bot)) return;
            State state = For(bot);
            state.FormationPoint = point;
            state.FormationUntil = GameLoop.GameLoopTime + 750;
        }

        public static bool HasFormationOrder(GameBot bot) => Applies(bot) &&
            States.TryGetValue(bot, out State state) && GameLoop.GameLoopTime < state.FormationUntil;

        public static Vector3 Predict(Vector3 point, Vector3 velocity, int leaderSpeed)
        {
            float length = velocity.Length();
            if (!float.IsFinite(length) || length < 1 || leaderSpeed <= 0) return point;
            // Half a second, never more than 200 units: longer than one AI
            // turn, so a companion in its slot keeps walking instead of
            // arriving, stopping and restarting several times a second.
            return point + velocity / length * Math.Min(200, Math.Min(length, leaderSpeed) * 0.5f);
        }

        public static Vector3 FormationDestination(GameBot bot, Vector3 point)
        {
            if (!Applies(bot)) return point;
            var leader = bot.FollowAnchor;
            if (leader.IsMoving) point = Predict(point, For(bot).LeaderVelocity, leader.MaxSpeed);
            Vector3 origin = new(leader.X, leader.Y, leader.Z);
            if (leader.CurrentZone == null) return point;
            return PathfindingProvider.Instance.GetMoveAlongSurface(leader.CurrentZone, origin, point,
                PathfindingProvider.Instance.DefaultFilters) ?? origin;
        }

        public static bool SendsDruidPet(GameBot bot) => Applies(bot) &&
            bot.CharacterClass?.ID == (int)eCharacterClass.Druid && bot.Stance != eBotStance.Passive;

        public static int CalculateSpeed(int normal, int leaderSpeed, double distanceFromSlot)
        {
            if (normal <= 0) return normal;
            double catchup = Math.Clamp((distanceFromSlot - 60) / 900d, 0, 1) * 0.20;
            return (int)Math.Clamp(Math.Round(Math.Max(normal, leaderSpeed) * (1 + catchup)), 0, short.MaxValue);
        }

        // GamePlayer and GameBot each declare IsOnHorse themselves rather than through a
        // shared GameLiving member, so the follow anchor (owner or squad leader) needs a
        // small type check here.
        private static bool IsOnHorse(GameLiving living) =>
            living is GamePlayer player ? player.IsOnHorse : living is GameBot bot && bot.IsOnHorse;

        public static short SpeedLimit(GameBot bot, short normal)
        {
            bool regrouping = CompanionEngagementMode.ShouldRegroup(bot);
            GameLiving anchor = bot.FollowAnchor;
            if (!Applies(bot) || !States.TryGetValue(bot, out State state) ||
                GameLoop.GameLoopTime >= state.FormationUntil || normal <= 0 || !bot.IsAlive ||
                bot.IsOnStableMasterRoute || bot.IsRecoveryResting || !regrouping && bot.InCombat || bot.IsAttacking ||
                bot.IsCrowdControlled || bot.IsDiseased || bot.HealthPercent < 33 ||
                bot.Brain is BotBrain { HasAggro: true } || !regrouping && anchor.InCombat ||
                !regrouping && anchor.IsAttacking || IsOnHorse(anchor) ||
                bot.IsStealthed || anchor.IsStealthed ||
                GameRelic.IsPlayerCarryingRelic(bot) || GameRelic.IsPlayerCarryingRelic(anchor) ||
                bot.effectListComponent?.ContainsEffectForEffectType(eEffect.MovementSpeedDebuff) == true ||
                bot.BuffBonusMultCategory1.Get((int)eProperty.MaxSpeed) < 1 ||
                (bot.IsCasting || bot.castingComponent?.HasPendingSkillRequests == true) && !BotSongTwistPolicy.HasMobileSongCast(bot))
                return normal;
            return (short)CalculateSpeed(normal, anchor.MaxSpeed,
                Vector3.Distance(new(bot.X, bot.Y, bot.Z), state.FormationPoint));
        }
    }
}
