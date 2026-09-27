using System.Numerics;
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
    /// peeled by the tanks through the ordinary group defence. The player then
    /// sets the pet passive and it runs back to camp with the pack; the group
    /// opens once the passive pet is back beside the player, and at once when
    /// the pet gets low or dies. While pet pulling, the pet is the group's tank
    /// and gets its buffs first.
    /// </summary>
    public static class CompanionPetPull
    {
        public const int PetDangerHealthPercent = 45;
        /// <summary>"Back at camp": the passive pet is this close to its owner.</summary>
        public const int ReturnedRadius = 400;
        /// <summary>Safety net when the pet is never set passive.</summary>
        public const long MaximumHoldMilliseconds = 60_000;
        public const long ContactTimeoutMilliseconds = 30_000;
        /// <summary>The group counts as pet pulling this long after the last /petpull.</summary>
        public const long SessionMilliseconds = 10 * 60_000;
        /// <summary>After the release the pet stays a heal target until the fight has been quiet this long.</summary>
        public const long QuietEndMilliseconds = 8_000;

        private sealed class State
        {
            public GameNPC Pet;
            public GameLiving Target;
            public Vector3 PullFrom;
            public long Started;
            public long Contact;
            public long LastFight;
            public bool Released;
        }

        private sealed class Session { public long LastPull; }

        private static readonly ConditionalWeakTable<GamePlayer, State> States = new();
        private static readonly ConditionalWeakTable<GamePlayer, Session> Sessions = new();

        /// <summary>The leader pulls with the pet: companions buff that pet before the group.</summary>
        public static GameNPC SessionPet(GamePlayer leader)
        {
            if (leader == null || !Sessions.TryGetValue(leader, out Session session) ||
                GameLoop.GameLoopTime - session.LastPull >= SessionMilliseconds)
                return null;
            GameNPC pet = leader.ControlledBrain?.Body;
            return pet?.IsAlive == true && pet.ObjectState == GameObject.eObjectState.Active ? pet : null;
        }

        /// <summary>
        /// Buffs that do something on a pet: only strength, constitution,
        /// dexterity and quickness count among stat buffs (no other concentration
        /// buff affects pets), plus damage add, shields, ablative, resists and HoTs.
        /// </summary>
        public static bool HelpsPet(Spell spell) => spell != null && spell.SpellType is
            eSpellType.StrengthBuff or eSpellType.ConstitutionBuff or eSpellType.DexterityBuff or
            eSpellType.StrengthConstitutionBuff or eSpellType.DexterityQuicknessBuff or
            eSpellType.DamageAdd or eSpellType.DamageShield or eSpellType.AblativeArmor or
            eSpellType.HealOverTime or eSpellType.HealthRegenBuff or
            eSpellType.BodyResistBuff or eSpellType.ColdResistBuff or eSpellType.EnergyResistBuff or
            eSpellType.HeatResistBuff or eSpellType.MatterResistBuff or eSpellType.SpiritResistBuff or
            eSpellType.BodySpiritEnergyBuff or eSpellType.HeatColdMatterBuff or eSpellType.AllMagicResistBuff;

        public static string Begin(GamePlayer player, GameLiving target)
        {
            if (player.ControlledBrain is not ControlledMobBrain brain || brain.Body?.IsAlive != true)
                return "You need a pet for /petpull. Summon your pet, target the pull, then /petpull.";
            GameNPC pet = brain.Body;
            if (!pet.IsWithinRadius(target, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS))
                return "That target is too far from your pet.";

            PlayerLedPullCoordinator.CancelForLeader(player);
            long now = GameLoop.GameLoopTime;
            States.AddOrUpdate(player, new State { Pet = pet, Target = target, Started = now, LastFight = now,
                PullFrom = new Vector3(target.X, target.Y, target.Z) });
            Sessions.GetOrCreateValue(player).LastPull = now;
            brain.Attack(target);
            return $"Pet pull: {pet.Name} goes in alone. Companions hold, HoT your pet, and peel adds. " +
                   "Set your pet passive to bring the pull back; damage starts once it is beside you " +
                   $"(or at once if your pet drops below {PetDangerHealthPercent}%).";
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

            bool home = leader.ControlledBrain?.AggressionState == eAggressionState.Passive &&
                pet.IsWithinRadius(leader, ReturnedRadius);
            bool danger = pet.HealthPercent < PetDangerHealthPercent;
            if (!home && !danger && now - state.Contact < MaximumHoldMilliseconds && target?.IsAlive == true)
                return true;

            state.Released = true;
            state.LastFight = now;
            Tell(leader, danger
                ? $"{pet.Name} is in trouble: companions engage now."
                : "The pull is at camp: companions engage.");
            if (target?.IsAlive == true)
                PlayerLedPullCoordinator.LeaderEngaged(leader, target);
            return false;
        }

        /// <summary>
        /// While the pull runs: the spot just in front of the waiting group,
        /// toward the pull, where the returning pet drags the pack. An Animist
        /// plants its mushrooms there.
        /// </summary>
        public static bool TryGetCampFront(GamePlayer leader, out Vector3 front)
        {
            front = default;
            if (!TryGetState(leader, out State state) || state.Released)
                return false;
            var camp = new Vector3(leader.X, leader.Y, leader.Z);
            Vector3 toward = state.PullFrom - camp;
            toward.Z = 0;
            if (toward.LengthSquared() < 1)
                return false;
            front = camp + Vector3.Normalize(toward) * CampFrontDistance;
            return true;
        }

        public const int CampFrontDistance = 180;

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
