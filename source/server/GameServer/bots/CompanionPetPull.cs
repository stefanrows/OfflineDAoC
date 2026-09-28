using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// /petpull, the 1.65 pet pull as Enchanter, Cabalist and Spiritmaster
    /// groups ran it, as a mode for the owner's whole force (his group and his
    /// companion squads). While the mode is on, every pull starts with the
    /// player's own pet: he sends it the normal way (pet attack), the group waits
    /// at camp, the pet brings the pack back, and the group opens.
    /// <para>
    /// During a pull the companions do no real damage. They keep a
    /// heal-over-time on the pet (the Mentalist HoT drew no aggro), heal the
    /// group, and intercept only adds that are on a group member or running at
    /// one. If the pet gets into danger (health falling or several attackers),
    /// healers heal it and tanks taunt adds off it before the release. The pull
    /// is released once the passive pet is back beside the player, at once when
    /// the pet drops below <see cref="PetDangerHealthPercent"/> or dies, when the
    /// player attacks, or after <see cref="MaximumHoldMilliseconds"/>. A release
    /// ends that pull, not the mode; the pet's next engage starts the next one.
    /// While the mode is on, the pet is the group's tank and gets its buffs first.
    /// </para>
    /// The mode lives on the logged-in character object, so it ends at logout.
    /// </summary>
    public static class CompanionPetPull
    {
        public const int PetDangerHealthPercent = 45;
        /// <summary>Below this the pet is "getting hurt": healers heal it and tanks take adds off it.</summary>
        public const int PetWarningHealthPercent = 70;
        /// <summary>This many live attackers on the pet count as danger once it has lost some health.</summary>
        public const int PetWarningAttackers = 3;
        /// <summary>
        /// A pack on the pet is its job: only once the pet is below this is the
        /// pack a danger. A fresh three-mob pull keeps the tanks at camp.
        /// </summary>
        public const int PetSwarmedHealthPercent = 90;
        /// <summary>"Back at camp": the passive pet is this close to its owner.</summary>
        public const int ReturnedRadius = 400;
        /// <summary>Safety net when the pet is never set passive.</summary>
        public const long MaximumHoldMilliseconds = 60_000;
        public const long ContactTimeoutMilliseconds = 30_000;
        /// <summary>After the release the pull lasts until the fight has been quiet this long.</summary>
        public const long QuietEndMilliseconds = 8_000;
        public const long DangerCheckMilliseconds = 500;
        public const int CampFrontDistance = 180;

        private sealed class Pull
        {
            public GameNPC Pet;
            public GameLiving Target;
            public Vector3 PullFrom;
            public long Started;
            public long Contact;
            public long LastFight;
            public bool Released;
            public bool WentOut;
            public bool Danger;
            public bool DangerTold;
            public long DangerCheckedAt = -DangerCheckMilliseconds;
            public readonly Dictionary<GameBot, GameLiving> TauntClaims = new();
        }

        private sealed class Mode
        {
            public bool On;
            public Pull Current;
        }

        private static readonly ConditionalWeakTable<GamePlayer, Mode> Modes = new();

        #region Mode

        public static bool IsModeOn(GamePlayer leader) =>
            leader != null && Modes.TryGetValue(leader, out Mode mode) && mode.On;

        /// <summary>No argument toggles; on/off set explicitly; anything else is null (show usage).</summary>
        public static bool? ParseMode(string[] args, bool current)
        {
            if (args == null || args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                return !current;
            return args[1].Trim().ToLowerInvariant() switch
            {
                "on" or "1" or "true" => true,
                "off" or "0" or "false" => false,
                _ => null
            };
        }

        public const string Usage = "Usage: /petpull [on|off]. Without an argument it toggles pet pull mode for your group and squads.";

        /// <summary>Switches the mode for the whole force of <paramref name="player"/> and returns the reply.</summary>
        public static string SetMode(GamePlayer player, bool on)
        {
            if (player == null)
                return Usage;
            Mode mode = Modes.GetOrCreateValue(player);
            GameNPC pet = LivePet(player);
            lock (mode)
            {
                mode.On = on;
                mode.Current = null;
                // Switched on in the middle of a fight: that fight is already
                // open, so it counts as a released pull and the next pet engage
                // after it starts the first real pet pull.
                if (on && pet != null && (pet.InCombat || player.InCombat))
                    mode.Current = OpenFight(pet, GameLoop.GameLoopTime);
            }
            return ModeReply(on, pet != null);
        }

        public static string ModeReply(bool on, bool hasPet)
        {
            if (!on)
                return "Pet pull mode is OFF. Companions fight your pet's targets at once again.";
            return "Pet pull mode is ON for your group and squads: every pull now starts with your pet's attack. " +
                   "Send your pet in, then set it passive to bring the pull back; companions wait at camp, keep your pet " +
                   "healed and buffed, only take adds that come at the group, and open once your pet is beside you " +
                   $"(or at once if it drops below {PetDangerHealthPercent}% or you attack)." +
                   (hasPet ? string.Empty : " Summon your pet first.");
        }

        #endregion

        #region Pull state

        /// <summary>True while the companions must leave the pull to the pet.</summary>
        public static bool IsHolding(GamePlayer leader)
        {
            Pull pull = Current(leader);
            return pull != null && !pull.Released;
        }

        /// <summary>True while a companion of this owner's force must hold for the pet (squads follow the owner).</summary>
        public static bool HoldsFor(GameBot bot) =>
            bot?.IsPlayerLedGroup == true && IsHolding(bot.PlayerGroupLeader);

        /// <summary>
        /// The player's own attack opens the fight for everyone; the mode stays on.
        /// A fight the player opens himself is no pet pull: his pet assisting him
        /// must not make the companions hold.
        /// </summary>
        public static void OnLeaderAttack(GamePlayer player)
        {
            if (player == null || !Modes.TryGetValue(player, out Mode mode))
                return;
            long now = GameLoop.GameLoopTime;
            GameNPC pet = LivePet(player);
            lock (mode)
            {
                if (mode.Current is { } pull)
                {
                    pull.Released = true;
                    pull.LastFight = now;
                }
                else if (mode.On && pet != null)
                    mode.Current = OpenFight(pet, now);
            }
        }

        private static Pull OpenFight(GameNPC pet, long now) => new()
        {
            Pet = pet, Target = pet.TargetObject as GameLiving, Started = now, Contact = now, LastFight = now,
            Released = true
        };

        /// <summary>The leader pulls with the pet: companions buff that pet before the group.</summary>
        public static GameNPC SessionPet(GamePlayer leader) => IsModeOn(leader) ? LivePet(leader) : null;

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

        /// <summary>
        /// While the pull runs: the spot just in front of the waiting group,
        /// toward the pull, where the returning pet drags the pack. An Animist
        /// plants its mushrooms there.
        /// </summary>
        public static bool TryGetCampFront(GamePlayer leader, out Vector3 front)
        {
            front = default;
            Pull pull = Current(leader);
            if (pull == null || pull.Released)
                return false;
            var camp = new Vector3(leader.X, leader.Y, leader.Z);
            Vector3 toward = pull.PullFrom - camp;
            toward.Z = 0;
            if (toward.LengthSquared() < 1)
                return false;
            front = camp + Vector3.Normalize(toward) * CampFrontDistance;
            return true;
        }

        /// <summary>The pulling pet while its pull lasts (held or released), for the HoT and heals.</summary>
        public static GameNPC Pet(GamePlayer leader) => Current(leader)?.Pet;

        public static bool IsReleased(GamePlayer leader) => Current(leader)?.Released == true;

        /// <summary>The held pet is getting hurt or swarmed: act before the release.</summary>
        public static bool PetInDanger(GamePlayer leader) => Current(leader) is { Released: false, Danger: true };

        /// <summary>
        /// The pet as a heal target: every companion heals it after the release;
        /// before it, only healers and only while it is in danger.
        /// </summary>
        public static GameNPC PetHealTarget(GamePlayer leader, bool healer)
        {
            Pull pull = Current(leader);
            if (pull == null)
                return null;
            return pull.Released || healer && pull.Danger ? pull.Pet : null;
        }

        public static bool IsDanger(int healthPercent, int attackers) =>
            healthPercent < PetWarningHealthPercent ||
            attackers >= PetWarningAttackers && healthPercent < PetSwarmedHealthPercent;

        /// <summary>
        /// Whether the pet's engage starts a pet pull. An order onto a monster
        /// that is already in combat joins that fight (for example a /pull whose
        /// tank sent the pet in), and while the last pull is still being killed
        /// only an order onto a fresh monster (a chain pull) starts the next one.
        /// Pets sent at enemy players never pet pull: 1.65 RvR had no pet pulls.
        /// </summary>
        public static bool StartsPull(bool firstPull, GameLiving ordered, GameLiving previousTarget, bool petEngaged,
            bool enemyPlayer)
        {
            if (enemyPlayer)
                return false;
            if (ordered != null)
                return !ordered.InCombat && (firstPull || ordered != previousTarget);
            return firstPull && petEngaged;
        }

        /// <summary>
        /// A tank takes one attacker off the endangered pet: never one another
        /// tank already took, preferably an add rather than the pet's own
        /// target (the pet keeps the pull), the pet's target only when nothing
        /// else is on it. Null when the pet is not in danger.
        /// </summary>
        public static GameLiving TakeAddFromPet(GamePlayer leader, GameBot tank)
        {
            if (tank == null || leader == null || !Modes.TryGetValue(leader, out Mode mode))
                return null;
            Pull pull = Current(leader);
            if (pull == null || pull.Released || !pull.Danger)
                return null;
            GameLiving[] attackers = AttackersOf(pull.Pet)
                .Where(attacker => tank.IsWithinRadius(attacker, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS) &&
                                   GameServer.ServerRules.IsAllowedToAttack(tank, attacker, true))
                .ToArray();
            lock (mode)
            {
                if (pull.TauntClaims.TryGetValue(tank, out GameLiving held) && held.IsAlive &&
                    held.TargetObject == pull.Pet)
                    return held;
                pull.TauntClaims.Remove(tank);
                GameLiving chosen = ChooseAddToTake(attackers, pull.Pet.TargetObject as GameLiving,
                    candidate => pull.TauntClaims.ContainsValue(candidate), attacker => tank.GetDistanceTo(attacker));
                if (chosen != null)
                    pull.TauntClaims[tank] = chosen;
                return chosen;
            }
        }

        public static GameLiving ChooseAddToTake(IEnumerable<GameLiving> attackers, GameLiving petTarget,
            Func<GameLiving, bool> claimed, Func<GameLiving, int> distance)
        {
            GameLiving[] open = attackers.Where(attacker => attacker?.IsAlive == true && !claimed(attacker)).ToArray();
            return open.Where(attacker => attacker != petTarget).OrderBy(attacker => distance(attacker)).FirstOrDefault() ??
                   open.FirstOrDefault(attacker => attacker == petTarget);
        }

        /// <summary>
        /// An add the companions may intercept while the pet holds the pull: a
        /// live monster whose target is someone on the group's side, but not the
        /// pulling pet (the pull itself stays on the pet).
        /// </summary>
        public static bool IsAddOnGroup(GameNPC npc, GameNPC pullPet, Func<GameLiving, bool> onGroupSide) =>
            npc?.IsAlive == true && npc.ObjectState == GameObject.eObjectState.Active && npc != pullPet &&
            npc.TargetObject is GameLiving victim && victim != pullPet && victim.IsAlive &&
            (npc.IsAttacking || npc.InCombat) && onGroupSide(victim);

        /// <summary>The nearest add on, or running at, a member of the owner's force.</summary>
        public static GameNPC IncomingAdd(GamePlayer leader, GameBot bot)
        {
            Pull pull = Current(leader);
            if (pull == null || pull.Released || bot == null || !bot.IsAlive)
                return null;
            bool OnOurSide(GameLiving victim) =>
                CompanionSquads.IsOwnerForceMember(victim, leader) || CompanionAddControl.OnGroupSide(bot.Group, victim) ||
                victim is GameNPC { Brain: IControlledBrain controlled } &&
                controlled.GetLivingOwner() is GameLiving owner && CompanionSquads.IsOwnerForceMember(owner, leader);
            return bot.GetNPCsInRadius(CompanionAddControl.ScanRadius)
                .Where(npc => IsAddOnGroup(npc, pull.Pet, OnOurSide) &&
                              GameServer.ServerRules.IsAllowedToAttack(bot, npc, true) &&
                              CompanionEngagementMode.Allows(bot, npc) &&
                              !CompanionAddControl.ProtectsMezz(bot, npc))
                .OrderBy(npc => bot.GetDistanceTo(npc))
                .FirstOrDefault();
        }

        #endregion

        #region State machine

        private static GameNPC LivePet(GamePlayer leader)
        {
            GameNPC pet = (leader?.ControlledBrain as ControlledMobBrain)?.Body;
            return pet?.IsAlive == true && pet.ObjectState == GameObject.eObjectState.Active ? pet : null;
        }

        private static IEnumerable<GameLiving> AttackersOf(GameNPC pet)
        {
            ICollection<GameLiving> attackers = pet?.attackComponent?.AttackerTracker?.Attackers;
            return attackers == null
                ? []
                : attackers.Where(attacker => attacker?.IsAlive == true && attacker.TargetObject == pet).ToArray();
        }

        /// <summary>Advances the owner's pull (end, release, start) and returns the live one.</summary>
        private static Pull Current(GamePlayer leader)
        {
            if (leader == null || !Modes.TryGetValue(leader, out Mode mode))
                return null;
            Action after = null;
            Pull result;
            lock (mode)
                result = Advance(leader, mode, ref after);
            after?.Invoke();
            return result;
        }

        private static Pull Advance(GamePlayer leader, Mode mode, ref Action after)
        {
            long now = GameLoop.GameLoopTime;
            Pull pull = mode.Current;

            if (pull != null)
            {
                GameNPC pet = pull.Pet;
                bool petLost = pet?.IsAlive != true || pet.ObjectState != GameObject.eObjectState.Active ||
                    leader.ControlledBrain?.Body != pet || !leader.IsAlive;
                if (!petLost && (pet.InCombat || leader.InCombat))
                    pull.LastFight = now;
                if (petLost)
                {
                    mode.Current = null;
                    GameLiving target = pull.Target;
                    if (!pull.Released && leader.IsAlive && target?.IsAlive == true)
                    {
                        // The pet died before the release: the pack now looks for the
                        // owner, so everyone engages and the tanks peel it off.
                        after += () =>
                        {
                            Tell(leader, "Your pet fell: companions engage and the tanks take the pull.");
                            PlayerLedPullCoordinator.LeaderEngaged(leader, target);
                        };
                    }
                    pull = null;
                }
                else if (pull.Released && now - pull.LastFight >= QuietEndMilliseconds)
                {
                    mode.Current = null;
                    pull = null;
                }
            }

            if (pull is { Released: false })
                HoldOrRelease(leader, pull, now, ref after);

            if (mode.On && (pull == null || pull.Released) && TryStart(leader, pull, now) is Pull started)
            {
                mode.Current = pull = started;
                string petName = started.Pet.Name;
                after += () => Tell(leader,
                    $"Pet pull: {petName} takes the pull. Companions hold until it is back beside you.");
            }

            return pull;
        }

        private static Pull TryStart(GamePlayer leader, Pull previous, long now)
        {
            if (leader.ControlledBrain is not ControlledMobBrain brain || LivePet(leader) is not GameNPC pet)
                return null;
            GameLiving ordered = brain.OrderedAttackTarget is { IsAlive: true } order ? order : null;
            GameLiving target = ordered ?? pet.TargetObject as GameLiving;
            if (!StartsPull(previous == null, ordered, previous?.Target, pet.IsAttacking || pet.InCombat,
                    target != null && CompanionPvpEngagement.Enemy(leader, target)))
                return null;
            GameObject from = (GameObject)target ?? pet;
            return new Pull { Pet = pet, Target = target, Started = now, LastFight = now,
                PullFrom = new Vector3(from.X, from.Y, from.Z),
                WentOut = !pet.IsWithinRadius(leader, ReturnedRadius) };
        }

        private static void HoldOrRelease(GamePlayer leader, Pull pull, long now, ref Action after)
        {
            GameNPC pet = pull.Pet;
            if (pull.Target?.IsAlive != true && pet.TargetObject is GameLiving { IsAlive: true } petTarget)
                pull.Target = petTarget;
            GameLiving target = pull.Target;
            if (pull.Contact == 0 && pet.InCombat)
                pull.Contact = now;
            if (!pet.IsWithinRadius(leader, ReturnedRadius))
                pull.WentOut = true;

            if (pull.Contact == 0)
            {
                if (now - pull.Started < ContactTimeoutMilliseconds)
                    return;
                // Treat it like an ordinary open fight from here: no hold, no engage order.
                pull.Released = true;
                pull.LastFight = now;
                after += () => Tell(leader, "Pet pull cancelled: your pet made no contact. Pet pull mode stays on.");
                return;
            }

            if (now - pull.DangerCheckedAt >= DangerCheckMilliseconds)
            {
                pull.DangerCheckedAt = now;
                pull.Danger = IsDanger(pet.HealthPercent, AttackersOf(pet).Count());
                if (!pull.Danger)
                    pull.TauntClaims.Clear();
            }
            if (pull.Danger && !pull.DangerTold)
            {
                pull.DangerTold = true;
                string petName = pet.Name;
                after += () => Tell(leader, $"{petName} is in danger: healers heal it and tanks take adds off it.");
            }

            // A pull that never left camp is at camp as soon as it lands on the pet.
            bool home = pet.IsWithinRadius(leader, ReturnedRadius) &&
                (leader.ControlledBrain?.AggressionState == eAggressionState.Passive || !pull.WentOut);
            bool critical = pet.HealthPercent < PetDangerHealthPercent;
            if (!home && !critical && now - pull.Contact < MaximumHoldMilliseconds && target?.IsAlive == true)
                return;

            pull.Released = true;
            pull.LastFight = now;
            string message = critical
                ? $"{pet.Name} is in trouble: companions engage now."
                : "The pull is at camp: companions engage.";
            after += () =>
            {
                Tell(leader, message);
                if (target?.IsAlive == true)
                    PlayerLedPullCoordinator.LeaderEngaged(leader, target);
            };
        }

        #endregion

        private static void Tell(GamePlayer player, string message) =>
            player.Out?.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
    }
}
