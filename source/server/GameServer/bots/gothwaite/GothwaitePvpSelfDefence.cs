using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// The mode bot's self-defence and area rules (docs/GOTHWAITE_PVP_MODE.md section 6.2). A mode bot opens a fight only on a target
/// that <see cref="GothwaitePvpTargetRules.MayOpen"/> allows. The one exception is self-defence: a target that attacked the bot,
/// its group or its pet may be fought back, whatever its con or position, except inside the castle circle and only where the
/// server rules allow the attack. A harmful area spell may not catch a spared bystander either. The class brain asks
/// <see cref="MayFight"/>, <see cref="Drops"/> or <see cref="Keep"/> through one guarded line each; with the mode off every one of
/// them answers "no change".
/// </summary>
public static class GothwaitePvpSelfDefence
{
    /// <summary>A hit the brain noted counts as a recent attack for this long (game time), about as long as a tracked attack lasts.</summary>
    public const int NoteMilliseconds = 10_000;
    private const int NoteSlots = 4;

    private sealed class Notes
    {
        public readonly object Sync = new();
        public readonly GameLiving[] Attackers = new GameLiving[NoteSlots];
        public readonly long[] Times = new long[NoteSlots];
    }

    private static readonly ConditionalWeakTable<GameBot, Notes> NotesByBot = new();

    /// <summary>
    /// The truth table of the candidate rule (pure). A non-mode bot is never filtered. A mode bot may take a target that is a
    /// legal opening, or one that attacked it or its side.
    /// </summary>
    public static bool Allows(bool modeBot, bool mayOpen, bool attackedUs) => !modeBot || mayOpen || attackedUs;

    /// <summary>The truth table of the area rule (pure): a harmful area spell of a mode bot is refused when a spared target stands in its area.</summary>
    public static bool AreaRefused(bool modeCaster, bool harmful, bool hasArea, bool sparedInArea) =>
        modeCaster && harmful && hasArea && sparedInArea;

    /// <summary>A noted hit is recent while it is no older than <see cref="NoteMilliseconds"/> (pure).</summary>
    public static bool IsRecent(long noteTime, long now) => now - noteTime <= NoteMilliseconds;

    /// <summary>
    /// May this bot fight this target now? True for every target when the bot is not a mode bot. For a mode bot the target must be
    /// alive and not the bot, and either a legal opening (<see cref="GothwaitePvpTargetRules.MayOpen"/>, a pet also judged by its
    /// owner's con) or a target that attacked the bot or its side (<see cref="AttackedUs"/>).
    /// </summary>
    public static bool MayFight(GameBot bot, GameLiving target)
    {
        if (!GothwaitePvpMode.IsModeBot(bot))
            return true;
        if (target == null || target == bot || !target.IsAlive)
            return false;
        // Stage 6: a bot that runs from a lost fight fights nothing, not even back; it keeps running, unless it has been caught
        // (crowd-controlled, at the end of its run, or hit in melee): then it turns and fights (review 2026-10-14).
        if (GothwaitePvpDriver.IsRetreating(bot) && !GothwaitePvpDriver.CaughtWhileRetreating(bot, target))
            return false;
        bool mayOpen = GothwaitePvpTargetRules.MayOpen(bot, target) && OwnerConAllows(bot, target);
        return Allows(true, mayOpen, !mayOpen && Defends(bot, target));
    }

    /// <summary>True when a mode bot must not take this living at all (a candidate it may not fight). False for every other bot.</summary>
    public static bool Drops(GameBot bot, GameLiving living) => GothwaitePvpMode.IsModeBot(bot) && !MayFight(bot, living);

    /// <summary>The target when the bot may keep fighting it, otherwise null. Every target is kept for a non-mode bot.</summary>
    public static GameLiving Keep(GameBot bot, GameLiving target) => Drops(bot, target) ? null : target;

    /// <summary>
    /// True when the target, its master (a pet's owner), or the attacker of a recent hit has attacked the bot, its group or its pet.
    /// It reads the attacker tracker of each of those, and the brain's own notes of its last hits (see <see cref="NoteAttacker"/>):
    /// the tracker is filled only after a spell hit's brain callback, so the notes cover the hit being decided.
    /// </summary>
    public static bool AttackedUs(GameBot bot, GameLiving target)
    {
        if (bot == null || target == null)
            return false;
        GameLiving master = PvpCombatant.Resolve(target) ?? target;
        long now = GameLoop.GameLoopTime;
        foreach (GameLiving member in Side(bot))
        {
            if (member is GameBot noting && Noted(noting, target, master, now))
                return true;
            // A plain loop over the tracker: the same test as before, without a LINQ query and its closure per decision.
            if (member.attackComponent?.AttackerTracker is { } tracker)
                foreach (GameLiving attacker in tracker.Attackers)
                    if (SameFighter(attacker, target, master))
                        return true;
        }
        return false;
    }

    /// <summary>Records that <paramref name="attacker"/> just hit this mode bot. The brain calls it first in its hit handler, before any decision.</summary>
    public static void NoteAttacker(GameBot bot, GameLiving attacker)
    {
        if (attacker == null || attacker == bot || !GothwaitePvpMode.IsModeBot(bot))
            return;
        Notes notes = NotesByBot.GetValue(bot, _ => new Notes());
        long now = GameLoop.GameLoopTime;
        lock (notes.Sync)
        {
            int slot = -1;
            for (int index = 0; index < NoteSlots && slot < 0; index++)
                if (notes.Attackers[index] == attacker)
                    slot = index;
            if (slot < 0)
            {
                // A new attacker takes an empty slot, otherwise the slot whose last hit is oldest.
                slot = 0;
                for (int index = 1; index < NoteSlots; index++)
                    if (notes.Attackers[slot] != null && (notes.Attackers[index] == null || notes.Times[index] < notes.Times[slot]))
                        slot = index;
            }
            notes.Attackers[slot] = attacker;
            notes.Times[slot] = now;
        }
    }

    /// <summary>
    /// Refuses a harmful area spell (the truth table is <see cref="AreaRefused"/>) when a target the mode bot may not fight stands in
    /// its area. The caster is the bot, or a pet resolved to its owner. A point-blank or cone spell is centred on the caster, any other
    /// area spell on its target object (a ground point included). A single-target spell has no area, and its target is chosen by
    /// <see cref="MayFight"/>.
    /// </summary>
    public static bool RefusesCast(GameLiving caster, Spell spell, GameObject target)
    {
        if (!GothwaitePvpMode.IsActive || caster == null || spell == null || !spell.IsHarmful)
            return false;
        if (PvpCombatant.Resolve(caster) is not GameBot bot || !GothwaitePvpMode.IsModeBot(bot))
            return false;
        GameObject centre;
        int radius;
        if (spell.Target == eSpellTarget.CONE)
        {
            centre = caster;
            radius = Math.Max(1, spell.Range);
        }
        else if (spell.IsPBAoE)
        {
            centre = caster;
            radius = spell.Radius;
        }
        else
        {
            centre = target;
            radius = spell.Radius;
        }
        if (centre == null || radius <= 0)
            return false;
        return AreaRefused(true, true, true, SparedWithin(bot, centre, radius).Length > 0);
    }

    /// <summary>
    /// The living things around a mode bot that it may not fight (the spared bystanders an area spell must not catch), within
    /// <paramref name="radius"/> of the bot. Empty for any other bot.
    /// </summary>
    public static GameLiving[] SparedAround(GameBot bot, int radius) =>
        GothwaitePvpMode.IsModeBot(bot) && radius > 0 ? SparedWithin(bot, bot, radius) : [];

    // A pet is judged by its owner's con too: a pet of a grey or purple player is no legal opening, whatever the pet's own level.
    private static bool OwnerConAllows(GameBot bot, GameLiving target)
    {
        if (PvpCombatant.Resolve(target) is not { } owner || owner == target)
            return true;
        return GothwaitePvpCastleRules.ConMayOpen(ConLevels.GetConColor(ConLevels.GetConLevel(bot.Level, owner.Level)));
    }

    // Self-defence: alive, active, outside the castle circle, legal under the server rules, and an attacker of the bot or its side.
    private static bool Defends(GameBot bot, GameLiving target) =>
        target.IsAlive && target.ObjectState == GameObject.eObjectState.Active &&
        !GothwaitePvpMode.Arena.InCastle(target.CurrentRegionID, target.X, target.Y) &&
        GameServer.ServerRules.IsAllowedToAttack(bot, target, true) && AttackedUs(bot, target);

    // The bot, its main pet, and its group: the side whose attackers decide self-defence.
    private static IEnumerable<GameLiving> Side(GameBot bot)
    {
        yield return bot;
        if (bot.ControlledBrain?.Body is { } pet)
            yield return pet;
        if (bot.Group == null)
            yield break;
        foreach (GameLiving member in bot.Group.GetMembersInTheGroup())
            if (member != null && member != bot)
                yield return member;
    }

    private static bool Noted(GameBot bot, GameLiving target, GameLiving master, long now)
    {
        if (!NotesByBot.TryGetValue(bot, out Notes notes))
            return false;
        lock (notes.Sync)
        {
            for (int index = 0; index < NoteSlots; index++)
                if (notes.Attackers[index] is { } attacker && IsRecent(notes.Times[index], now) && SameFighter(attacker, target, master))
                    return true;
        }
        return false;
    }

    // An attacker is the target itself, or a pet of the target's master (a pet hit counts as its owner).
    private static bool SameFighter(GameLiving attacker, GameLiving target, GameLiving master) =>
        attacker == target || PvpCombatant.Resolve(attacker) is { } owner && owner == master;

    private static GameLiving[] SparedWithin(GameBot bot, GameObject centre, int radius)
    {
        ushort range = (ushort)Math.Min(radius, ushort.MaxValue);
        return centre.GetPlayersInRadius(range).Cast<GameLiving>()
            .Concat(centre.GetNPCsInRadius(range).Cast<GameLiving>())
            .Where(living => living != bot && living.IsAlive && GameServer.ServerRules.IsAllowedToAttack(bot, living, true) &&
                !MayFight(bot, living))
            .ToArray();
    }
}
