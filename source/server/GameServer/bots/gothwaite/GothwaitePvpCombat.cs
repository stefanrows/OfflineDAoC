using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// The live side of the mode's player tactics (docs/GOTHWAITE_PVP_MODE.md section 13): a mode bot's doctrine (read from its group
/// when it departed, or derived for a solo), the kill order inside a fight (<see cref="ChooseTarget"/>, the brain's one guarded
/// call), the opening scan with the fight-appetite test (<see cref="FindOpening"/>), and the enemy counts the retreat rules need.
/// The pure rules are in <see cref="GothwaitePvpTactics"/> and the shared habits in <see cref="AutonomousRvrCombatHabits"/>; this
/// class only reads the game. Everything answers "no change" for a bot that is not a mode bot.
/// </summary>
public static class GothwaitePvpCombat
{
    /// <summary>A follower picks up the leader's target within this distance (the /assist range players had).</summary>
    public const int CallerRange = 2_000;
    /// <summary>Instant harmful spells and bows reach roughly this far: enough to interrupt a caster.</summary>
    public const int InterruptReach = 1_500;
    public const int MeleeInterruptReach = 350;
    private const int CasterScanMilliseconds = 1_000;
    private const int SoloDoctrineMilliseconds = 60_000;

    // Per-bot habit state: the target it sticks to, the call it answers, and a solo's derived doctrine.
    private sealed class Habit
    {
        public GameLiving Target;
        public long ChosenAt;
        public GameLiving Call;
        public long CallAt;
        public int CallDelay;
        public bool CallIgnored;
        public long NextCasterScan;
        public RvrDoctrine SoloDoctrine;
        public long SoloDoctrineUntil;
    }

    private static readonly ConditionalWeakTable<GameBot, Habit> Habits = new();

    /// <summary>The class traits of a living (a pet reads as its owner's class).</summary>
    public static RvrClassTraits ClassTraits(GameLiving living) => AutonomousRvrDoctrineRuntime.ClassTraits(living);

    /// <summary>The personality of a bot as the doctrine reads it; neutral for a bot without a record.</summary>
    public static RvrLeaderTraits LeaderTraits(GameBot bot) => bot?.PersistentRecord is { } record
        ? new RvrLeaderTraits(record.Aggression, record.RiskTolerance, record.Patience)
        : RvrLeaderTraits.Neutral;

    /// <summary>The doctrine of a set of members led by <paramref name="leader"/>, as the world doctrine reads a group's classes.</summary>
    public static RvrDoctrine Derive(IReadOnlyList<GameBot> members, GameBot leader)
    {
        RvrDoctrineMember[] shape = (members ?? Array.Empty<GameBot>())
            .Where(member => member != null)
            .Select(member => new RvrDoctrineMember((eCharacterClass)(member.CharacterClass?.ID ?? 0), member.Level))
            .ToArray();
        return AutonomousRvrDoctrine.Derive(shape, AutonomousPlayerBehavior.TypeOf(leader?.PersistentRecord), LeaderTraits(leader));
    }

    /// <summary>
    /// The doctrine of a mode bot: its group's, read when the group departed, or for a bot outside a departed group its own
    /// (derived once a minute). Null for a bot that is not a mode bot.
    /// </summary>
    public static RvrDoctrine DoctrineOf(GameBot bot)
    {
        if (!GothwaitePvpMode.IsModeBot(bot))
            return null;
        if (GothwaitePvpGroupFormer.TryGetMembership(bot, out GothwaitePvpGroupFormer.Membership membership) && membership.Doctrine != null)
            return membership.Doctrine;
        Habit habit = Habits.GetValue(bot, _ => new Habit());
        long now = GameLoop.GameLoopTime;
        if (habit.SoloDoctrine == null || now >= habit.SoloDoctrineUntil)
        {
            habit.SoloDoctrine = Derive(new[] { bot }, bot);
            habit.SoloDoctrineUntil = now + SoloDoctrineMilliseconds;
        }
        return habit.SoloDoctrine;
    }

    /// <summary>The leader of the bot's mode group when the bot is a follower of a departed group, else null.</summary>
    public static GameBot LeaderOf(GameBot bot) =>
        GothwaitePvpGroupFormer.TryGetMembership(bot, out GothwaitePvpGroupFormer.Membership membership) && !membership.IsLeader
            ? membership.Leader
            : null;

    /// <summary>
    /// The leader's current target as the group's call: the leader is alive, fighting it (attacking or casting), it is player-shaped,
    /// within <see cref="CallerRange"/> of the bot, and the bot may fight it. Null when there is no call.
    /// </summary>
    public static GameLiving CallerTarget(GameBot bot)
    {
        if (LeaderOf(bot) is not { IsAlive: true } leader || leader == bot || leader.TargetObject is not GameLiving { IsAlive: true } target ||
            !(leader.IsAttacking || leader.IsCasting) || !PvpCombatant.IsPlayerShaped(target) ||
            target.CurrentRegionID != bot.CurrentRegionID || !bot.IsWithinRadius(target, CallerRange) ||
            !GothwaitePvpSelfDefence.MayFight(bot, target))
            return null;
        return target;
    }

    /// <summary>
    /// The kill order inside a fight, from the brain's next-target choice (one guarded call in BotBrain.ApplyRvrCombatHabits). Among
    /// the enemies on the aggro list the bot may fight, it sticks to its target for the doctrine's stick time, a damage dealer answers
    /// the leader's call after the 1–2 s /assist delay (and now and then ignores it), a bot that can interrupt looks again when an enemy
    /// caster starts a spell, and the weights favour healers and mezzers, wounded enemies and whoever is hitting our support. Null keeps
    /// the brain's threat order (a bot that is not a mode bot, no player-shaped enemy, nothing left to fight).
    /// </summary>
    public static GameLiving ChooseTarget(GameBot bot, IEnumerable<GameLiving> aggro, GameLiving highest, GameLiving current)
    {
        if (!GothwaitePvpMode.IsModeBot(bot) || aggro == null || highest == null || !PvpCombatant.IsPlayerShaped(highest))
            return null;
        RvrDoctrine doctrine = DoctrineOf(bot);
        if (doctrine == null)
            return null;
        List<GameLiving> pool = aggro
            .Where(living => living != null && living != bot && living.IsAlive && PvpCombatant.IsPlayerShaped(living) &&
                GothwaitePvpSelfDefence.MayFight(bot, living) && GameServer.ServerRules.IsAllowedToAttack(bot, living, true))
            .Distinct()
            .ToList();
        GameLiving caller = doctrine.HasCaller ? CallerTarget(bot) : null;
        if (caller != null && !pool.Contains(caller))
            pool.Add(caller);
        if (pool.Count == 0)
            return null;

        Habit habit = Habits.GetValue(bot, _ => new Habit());
        long now = GameLoop.GameLoopTime;
        GameLiving held = current != null && pool.Contains(current) ? current :
            habit.Target != null && pool.Contains(habit.Target) ? habit.Target : null;
        int patience = bot.PersistentRecord?.Patience ?? 50;
        bool keep = held != null && now - habit.ChosenAt < AutonomousRvrDoctrine.StickMilliseconds(doctrine, patience);

        RvrClassTraits own = ClassTraits(bot);
        if (caller != null && AutonomousRvrCombatHabits.IsDamageDealer(own))
        {
            if (caller != habit.Call)
            {
                habit.Call = caller;
                habit.CallAt = now;
                habit.CallDelay = AutonomousRvrCombatHabits.SwitchDelayMilliseconds(Random.Shared.NextDouble());
                habit.CallIgnored = Random.Shared.NextDouble() < AutonomousRvrCombatHabits.IgnoreCallChance(patience);
            }
            switch (AutonomousRvrCombatHabits.RespondToCall(true, held != null, held == caller, held?.HealthPercent ?? 100,
                now - habit.CallAt, habit.CallDelay, habit.CallIgnored))
            {
                case RvrCallResponse.Switch:
                    habit.Target = caller;
                    habit.ChosenAt = now;
                    return Adopted(bot, aggro, caller);
                case RvrCallResponse.Wait:
                case RvrCallResponse.Finish:
                    return held;
            }
        }

        bool archer = own.HasFlag(RvrClassTraits.Archer);
        bool instant = bot.InstantHarmfulSpells?.Any(spell => spell != null && spell.Level <= bot.Level) == true;
        bool canInterrupt = archer || instant || own.HasFlag(RvrClassTraits.Melee);
        int reach = archer || instant ? InterruptReach : MeleeInterruptReach;
        bool reconsider = false;
        if (keep && canInterrupt && held?.IsCasting != true && now >= habit.NextCasterScan)
        {
            habit.NextCasterScan = now + CasterScanMilliseconds;
            foreach (GameLiving caster in CastingEnemyCasters(bot, pool, reach))
            {
                if (!pool.Contains(caster))
                    pool.Add(caster);
                reconsider = true;
            }
        }

        HashSet<GameLiving> ours = Side(bot);
        RvrTargetView[] views = pool.Select(target => new RvrTargetView(
                ClassTraits(target),
                bot.GetDistanceTo(target),
                target.HealthPercent,
                target == caller,
                AttacksOurSupport(bot, target, ours),
                false,
                PvpCombatant.Resolve(target) != target,
                target.IsCasting))
            .ToArray();
        bool isCaller = doctrine.HasCaller && LeaderOf(bot) == null;
        int index = AutonomousRvrCombatHabits.Choose(views, held == null ? -1 : pool.IndexOf(held), keep, doctrine,
            Random.Shared.NextDouble(), Random.Shared.NextDouble(), new RvrChooser(isCaller, canInterrupt), reconsider);
        if (index < 0)
            return null;
        GameLiving chosen = pool[index];
        if (chosen != habit.Target || !keep)
        {
            habit.Target = chosen;
            habit.ChosenAt = now;
        }
        return Adopted(bot, aggro, chosen);
    }

    /// <summary>
    /// The opening scan of a leader or a solo: the legal openings within <paramref name="range"/> (<paramref name="accept"/> is the
    /// mode's opening rule), each judged by the fight appetite against the target's party (a hunting stealther by its own rule
    /// instead), then one is drawn by the kill-order weights. Null when nothing may be opened on; <paramref name="refused"/> says a
    /// candidate was seen but its party was too big for this group.
    /// </summary>
    public static GameLiving FindOpening(GameBot bot, int range, Func<GameLiving, bool> accept, RvrDoctrine doctrine, int ownCount,
        int ownLevel, bool stealther, long now, out bool refused)
    {
        refused = false;
        Zone zone = bot?.CurrentZone;
        if (zone == null)
            return null;
        ushort radius = (ushort)Math.Clamp(range, 0, ushort.MaxValue);
        var candidates = new List<GameLiving>();
        foreach (GamePlayer player in bot.GetPlayersInRadius(radius))
            if (IsOpening(bot, player, zone, accept))
                candidates.Add(player);
        foreach (GameNPC npc in bot.GetNPCsInRadius(radius))
            if (PvpCombatant.IsPlayerShaped(npc) && IsOpening(bot, npc, zone, accept))
                candidates.Add(npc);
        if (candidates.Count == 0)
            return null;

        long botKey = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
        long bucket = now / GothwaitePvpTactics.AppetiteBucketMilliseconds;
        double appetite = doctrine?.Appetite ?? 1;
        var weighted = new List<(GameLiving Target, double Weight)>(candidates.Count);
        double total = 0;
        foreach (GameLiving candidate in candidates)
        {
            GameLiving master = PvpCombatant.Resolve(candidate) ?? candidate;
            (int party, int partyLevel) = Party(master);
            RvrClassTraits traits = ClassTraits(candidate);
            double weight;
            if (stealther)
            {
                if (!GothwaitePvpTactics.StealtherMayOpen(party, master.HealthPercent, AutonomousRvrStealthLoop.IsSoft(traits)))
                {
                    refused = true;
                    continue;
                }
                weight = Math.Max(0.01, AutonomousRvrStealthLoop.Score(new RvrStealthTargetView(traits, bot.GetDistanceTo(candidate),
                    master.HealthPercent, party, party <= 1 ? double.PositiveInfinity : 0, party <= 2, master.IsSitting, false)));
            }
            else
            {
                long masterKey = master.Group != null ? RuntimeHelpers.GetHashCode(master.Group) : master.ObjectID;
                if (!GothwaitePvpTactics.TakesFight(appetite, ownCount, ownLevel, party, partyLevel,
                        GothwaitePvpTactics.StableRoll(botKey, masterKey, bucket)))
                {
                    refused = true;
                    continue;
                }
                weight = AutonomousRvrCombatHabits.Weight(new RvrTargetView(traits, bot.GetDistanceTo(candidate), candidate.HealthPercent,
                    false, false, false, master != candidate, candidate.IsCasting), doctrine, new RvrChooser(true, false));
            }
            if (!BotSiegeRuntime.Visible(bot, candidate))
                continue;
            weighted.Add((candidate, weight));
            total += weight;
        }
        if (weighted.Count == 0)
            return null;
        double roll = Random.Shared.NextDouble() * total;
        foreach ((GameLiving target, double weight) in weighted)
        {
            if (roll < weight)
                return target;
            roll -= weight;
        }
        return weighted[^1].Target;
    }

    /// <summary>The living, player-shaped enemies in a fight within <paramref name="range"/> of the bot that the server lets it attack.</summary>
    public static int EnemiesNear(GameBot bot, int range)
    {
        if (bot == null)
            return 0;
        ushort radius = (ushort)Math.Clamp(range, 0, ushort.MaxValue);
        int count = 0;
        foreach (GamePlayer player in bot.GetPlayersInRadius(radius))
            if (IsFightingEnemy(bot, player))
                count++;
        foreach (GameNPC npc in bot.GetNPCsInRadius(radius))
            if (npc is GameBot && IsFightingEnemy(bot, npc))
                count++;
        return count;
    }

    /// <summary>The living, player-shaped attackers the bot's tracker holds, within <paramref name="range"/> (a pet counts as its owner, once).</summary>
    public static int PlayerShapedAttackers(GameBot bot, int range)
    {
        if (bot?.attackComponent?.AttackerTracker is not { } tracker)
            return 0;
        var seen = new HashSet<GameLiving>();
        foreach (GameLiving attacker in tracker.Attackers)
        {
            if (attacker is not { IsAlive: true } || attacker == bot || !PvpCombatant.IsPlayerShaped(attacker) ||
                attacker.CurrentRegionID != bot.CurrentRegionID || !bot.IsWithinRadius(attacker, range))
                continue;
            seen.Add(PvpCombatant.Resolve(attacker) ?? attacker);
        }
        return seen.Count;
    }

    /// <summary>The size and average level of a living's party: its living group members within <see cref="GothwaitePvpTactics.PartyRadius"/> of it, itself included. (1, its level) alone.</summary>
    public static (int Size, int Level) Party(GameLiving master)
    {
        if (master == null)
            return (0, 0);
        int size = 1, levels = master.Level;
        if (master.Group != null)
            foreach (GameLiving member in master.Group.GetMembersInTheGroup())
            {
                if (member == null || member == master || !member.IsAlive || member.CurrentRegionID != master.CurrentRegionID ||
                    !member.IsWithinRadius(master, GothwaitePvpTactics.PartyRadius))
                    continue;
                size++;
                levels += member.Level;
            }
        return (size, levels / size);
    }

    /// <summary>Forgets a bot's habit state (tests).</summary>
    public static void Forget(GameBot bot)
    {
        if (bot != null)
            Habits.Remove(bot);
    }

    // The chosen target is put on the aggro list when it is not there yet (the leader's call), so the FSM can take it.
    private static GameLiving Adopted(GameBot bot, IEnumerable<GameLiving> aggro, GameLiving chosen)
    {
        if (chosen != null && bot.Brain is BotBrain brain && !aggro.Contains(chosen))
            brain.AddToAggroList(chosen, 1);
        return chosen;
    }

    // The cheap tests; the line-of-sight raycast runs after the appetite test, once per surviving candidate.
    private static bool IsOpening(GameBot bot, GameLiving target, Zone zone, Func<GameLiving, bool> accept) =>
        BattlegroundCombatTargets.IsLegal(bot, target, zone) && !target.IsStealthed && !BotPvpCrowdControl.Protected(bot, target) &&
        (accept == null || accept(target));

    private static bool IsFightingEnemy(GameBot bot, GameLiving living) =>
        living != null && living != bot && living.IsAlive && living.InCombat && living.ObjectState == GameObject.eObjectState.Active &&
        PvpCombatant.IsPlayerShaped(living) && GameServer.ServerRules.IsAllowedToAttack(bot, living, true);

    // Enemy casters and healers within reach that are casting now: those in the pool, or in a group the pool is fighting.
    private static IEnumerable<GameLiving> CastingEnemyCasters(GameBot bot, List<GameLiving> pool, int reach)
    {
        HashSet<Group> fighting = pool.Select(target => PvpCombatant.Resolve(target)?.Group)
            .Where(group => group != null).ToHashSet();
        return bot.GetPlayersInRadius((ushort)reach).Cast<GameLiving>()
            .Concat(bot.GetNPCsInRadius((ushort)reach).OfType<GameBot>())
            .Where(enemy => enemy != bot && enemy.IsAlive && enemy.IsCasting && !enemy.IsStealthed &&
                enemy.ObjectState == GameObject.eObjectState.Active && PvpCombatant.IsPlayerShaped(enemy) &&
                (pool.Contains(enemy) || enemy.Group != null && fighting.Contains(enemy.Group)) &&
                AutonomousRvrCombatHabits.IsCasterClass(ClassTraits(enemy)) && GothwaitePvpSelfDefence.MayFight(bot, enemy))
            .ToArray();
    }

    private static HashSet<GameLiving> Side(GameBot bot)
    {
        var side = new HashSet<GameLiving> { bot };
        if (bot.Group != null)
            foreach (GameLiving member in bot.Group.GetMembersInTheGroup())
                if (member != null)
                    side.Add(member);
        return side;
    }

    private static bool AttacksOurSupport(GameBot bot, GameLiving target, HashSet<GameLiving> ours)
    {
        if (target.TargetObject is not GameLiving victim || victim == bot || !ours.Contains(victim))
            return false;
        RvrClassTraits traits = ClassTraits(victim);
        return traits.HasFlag(RvrClassTraits.Healer) || traits.HasFlag(RvrClassTraits.Caster);
    }
}
