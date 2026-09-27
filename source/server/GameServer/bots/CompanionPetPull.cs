using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// /petpull, the 1.65 pet pull as Hibernian Enchanter groups ran it: the
    /// player's pet goes in alone and takes the pack while the companions hold.
    /// A heal-over-time on the pet (the Mentalist HoT drew no aggro) is the only
    /// heal it gets until the pull is on it; loose adds that reach the group are
    /// peeled by the tanks through the ordinary group defence. The group opens
    /// once the pet has held for a while and its target is worn down, and at
    /// once when the pet gets low or dies.
    /// </summary>
    public static class CompanionPetPull
    {
        /// <summary>Veterans waited for the biggest mob to drop to 70-75 % before the first AoE.</summary>
        public const int EngageHealthPercent = 75;
        public const int PetDangerHealthPercent = 45;
        public const long MinimumHoldMilliseconds = 3_000;
        public const long MaximumHoldMilliseconds = 10_000;
        public const long ContactTimeoutMilliseconds = 30_000;
        /// <summary>After the release the pet stays a heal target until the fight has been quiet this long.</summary>
        public const long QuietEndMilliseconds = 8_000;

        private sealed class State
        {
            public GameNPC Pet;
            public GameLiving Target;
            public long Started;
            public long Contact;
            public long LastFight;
            public bool Released;
        }

        private static readonly ConditionalWeakTable<GamePlayer, State> States = new();

        public static string Begin(GamePlayer player, GameLiving target)
        {
            if (player.ControlledBrain is not ControlledMobBrain brain || brain.Body?.IsAlive != true)
                return "You need a pet for /petpull. Summon your pet, target the pull, then /petpull.";
            GameNPC pet = brain.Body;
            if (!pet.IsWithinRadius(target, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS))
                return "That target is too far from your pet.";

            PlayerLedPullCoordinator.CancelForLeader(player);
            long now = GameLoop.GameLoopTime;
            States.AddOrUpdate(player, new State { Pet = pet, Target = target, Started = now, LastFight = now });
            brain.Attack(target);
            return $"Pet pull: {pet.Name} goes in alone. Companions hold, HoT your pet, and peel adds; " +
                   $"damage starts once the pull sits on the pet (target under {EngageHealthPercent}%) " +
                   $"or at once if your pet drops below {PetDangerHealthPercent}%.";
        }

        public static void Cancel(GamePlayer player) => States.Remove(player);

        /// <summary>The player's own attack opens the fight for everyone.</summary>
        public static void OnLeaderAttack(GamePlayer player)
        {
            if (TryGetState(player, out State state) && !state.Released)
            {
                state.Released = true;
                state.LastFight = GameLoop.GameLoopTime;
            }
        }

        /// <summary>True while the companions must leave the pull to the pet.</summary>
        public static bool IsHolding(GamePlayer leader)
        {
            if (!TryGetState(leader, out State state) || state.Released)
                return false;

            long now = GameLoop.GameLoopTime;
            GameNPC pet = state.Pet;
            GameLiving target = pet.TargetObject as GameLiving ?? state.Target;
            if (state.Contact == 0 && (pet.InCombat || pet.IsAttacking))
                state.Contact = now;

            if (state.Contact == 0)
            {
                if (now - state.Started < ContactTimeoutMilliseconds)
                    return true;
                Tell(leader, "Pet pull cancelled: your pet made no contact.");
                States.Remove(leader);
                return false;
            }

            long held = now - state.Contact;
            bool worn = held >= MinimumHoldMilliseconds && target?.IsAlive == true && target.HealthPercent <= EngageHealthPercent;
            bool danger = pet.HealthPercent < PetDangerHealthPercent;
            if (!worn && !danger && held < MaximumHoldMilliseconds && target?.IsAlive == true)
                return true;

            state.Released = true;
            state.LastFight = now;
            Tell(leader, danger
                ? $"{pet.Name} is in trouble: companions engage now."
                : "The pull sits on your pet: companions engage.");
            if (target?.IsAlive == true)
                PlayerLedPullCoordinator.LeaderEngaged(leader, target);
            return false;
        }

        /// <summary>The pulling pet while its pull lasts, as a heal target for the companions.</summary>
        public static GameNPC Pet(GamePlayer leader) =>
            TryGetState(leader, out State state) ? state.Pet : null;

        public static bool IsReleased(GamePlayer leader) =>
            TryGetState(leader, out State state) && state.Released;

        private static bool TryGetState(GamePlayer leader, out State state)
        {
            state = null;
            if (leader == null || !States.TryGetValue(leader, out State found))
                return false;

            GameNPC pet = found.Pet;
            bool petLost = pet?.IsAlive != true || pet.ObjectState != GameObject.eObjectState.Active ||
                leader.ControlledBrain?.Body != pet || !leader.IsAlive;
            long now = GameLoop.GameLoopTime;
            if (!petLost && (pet.InCombat || leader.InCombat))
                found.LastFight = now;
            if (petLost || found.Released && now - found.LastFight >= QuietEndMilliseconds)
            {
                States.Remove(leader);
                if (petLost && !found.Released && leader.IsAlive && found.Target?.IsAlive == true)
                {
                    // The pet died before the release: the pack now looks for the
                    // owner, so everyone engages and the tanks peel it off.
                    Tell(leader, "Your pet fell: companions engage and the tanks take the pull.");
                    PlayerLedPullCoordinator.LeaderEngaged(leader, found.Target);
                }
                return false;
            }

            state = found;
            return true;
        }

        private static void Tell(GamePlayer player, string message) =>
            player.Out?.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
    }
}
