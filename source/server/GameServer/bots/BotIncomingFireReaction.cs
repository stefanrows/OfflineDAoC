using System;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>What kind of hit drew an autonomous bot into a fight.</summary>
public enum BotIncomingFireKind
{
    /// <summary>No reaction rule: not a world bot, not a player-shaped attacker, or no hit at all.</summary>
    None,
    /// <summary>Somebody is swinging at the bot in melee.</summary>
    Melee,
    /// <summary>A single-target spell, bolt or arrow aimed at the bot, or an area spell cast from right next to it (a bomber).</summary>
    Direct,
    /// <summary>An area spell from a distance (a nuke, a DoT tick, a debuff with a radius) that happened to catch the bot or a mate.</summary>
    Area,
    /// <summary>A group or guild mate was hit by a single-target attack, not the bot itself.</summary>
    Mate,
}

/// <summary>
/// How an autonomous world bot reacts to being hit by another player-shaped fighter (a player, a companion, a world bot, or their
/// pets), the way a person does instead of the way a mob does. A mob turns on the first thing that scratches it, at once, and so does
/// every mob next to it. A player takes a moment to notice ("I'm dotted"), the healers heal and cure first, a follower waits for the
/// main assist's call, and a splash from an area spell is a reason to find the caster, not an order to drop the current target and
/// run at whoever cast it. Three rules, all pure here and applied by the brain through <c>BotBrain.IncomingFire</c>:
/// <list type="number">
/// <item>A reaction delay, drawn once when the hit pulls the bot into its combat state, by the kind of hit, the class, the bot's
/// Aggression and whether the attacker stood in front of it; while it runs the bot only heals and cures. Someone hitting the bot in
/// melee ends it at once. A follower then waits a little longer for its leader's call before it picks a target on its own, unless
/// there is only one enemy to pick.</item>
/// <item>An area hit from a distance is not a personal call: it adds threat, but it never becomes the bot's priority attacker.</item>
/// <item>A direct attacker only overrides the kill order while it stands in melee reach, or while the bot has no other player-shaped
/// target. A bot fighting a mob still drops the mob for a ganker at once.</item>
/// </list>
/// Companions led by a player, temporary helpers, battleground encounter bots, and every hit from a mob keep the old immediate reaction.
/// </summary>
public static class BotIncomingFireReaction
{
    /// <summary>A reaction delay never runs longer than this, whatever the draw and the factors say.</summary>
    public const int MaxHoldMilliseconds = 2_500;
    /// <summary>A follower waits this long past its own delay for the leader's call before it picks a target itself.</summary>
    public const int CallWaitMilliseconds = 1_500;
    /// <summary>An attacker within the bot's melee range plus this is "on" the bot: a reason to answer it whatever the kill order says.</summary>
    public const int MeleePressureReach = 200;
    /// <summary>A point-blank area spell cast within its radius plus this of the bot is a bomber standing in the group: a personal call.</summary>
    public const int BomberReach = 100;
    /// <summary>The leader counts as the bot's caller within this distance (the /assist range players had).</summary>
    public const int CallerRange = 2_000;
    /// <summary>An attacker the bot was not facing takes this much longer to find (a draw in this range is added).</summary>
    public const int UnseenMinMilliseconds = 500;
    public const int UnseenMaxMilliseconds = 1_000;
    /// <summary>An attacker inside this arc of the bot's heading counts as in front of it.</summary>
    public const int FrontArcDegrees = 180;
    /// <summary>With this many group members carrying a DoT, a healer casts the group cure instead of one cure per member.</summary>
    public const int GroupCureMembers = 3;

    public const int MeleeMinMilliseconds = 300;
    public const int MeleeMaxMilliseconds = 800;
    public const int DirectMinMilliseconds = 500;
    public const int DirectMaxMilliseconds = 1_200;
    public const int AreaMinMilliseconds = 900;
    public const int AreaMaxMilliseconds = 2_200;
    public const int MateMinMilliseconds = 800;
    public const int MateMaxMilliseconds = 1_800;

    // ------------------------------------------------------------ pure rules

    /// <summary>Whether the reaction rules apply to this bot: an autonomous world bot that no player leads and no battleground script drives.</summary>
    public static bool Applies(GameBot bot) =>
        bot?.IsAutonomousWorldBot == true && !bot.IsPlayerLedGroup && !bot.IsTemporaryGroupHelper && !bot.IsBattlegroundEncounterBot;

    /// <summary>
    /// The kind of a hit from its attack type and whether the spell behind it has an area (pure). A melee swing is melee whatever its
    /// weapon proc's radius. An area spell from a bomber in reach (<paramref name="areaFromReach"/>) is a direct hit, not a splash.
    /// </summary>
    public static BotIncomingFireKind KindOf(AttackData.eAttackType attackType, bool area, bool areaFromReach = false)
    {
        if (attackType == AttackData.eAttackType.Spell)
            return area && !areaFromReach ? BotIncomingFireKind.Area : BotIncomingFireKind.Direct;
        if (attackType == AttackData.eAttackType.Ranged)
            return BotIncomingFireKind.Direct;
        return BotIncomingFireKind.Melee;
    }

    /// <summary>
    /// Whether an area hit came from within reach: a point-blank spell cast within its radius plus <see cref="BomberReach"/> of the
    /// bot, or any area spell whose caster stands within the bot's melee reach.
    /// </summary>
    public static bool AreaFromReach(bool pointBlank, double distance, int radius, int meleeReach) =>
        distance <= meleeReach || pointBlank && distance <= radius + BomberReach;

    /// <summary>The kind a mate's hit gives the bot: the same area splash is probably on the bot too, any other hit is a mate's.</summary>
    public static BotIncomingFireKind MateKindOf(bool area) => area ? BotIncomingFireKind.Area : BotIncomingFireKind.Mate;

    /// <summary>The delay range of a kind of hit, in milliseconds (inclusive). None has no delay.</summary>
    public static (int Min, int Max) DelayRange(BotIncomingFireKind kind) => kind switch
    {
        BotIncomingFireKind.Melee => (MeleeMinMilliseconds, MeleeMaxMilliseconds),
        BotIncomingFireKind.Direct => (DirectMinMilliseconds, DirectMaxMilliseconds),
        BotIncomingFireKind.Area => (AreaMinMilliseconds, AreaMaxMilliseconds),
        BotIncomingFireKind.Mate => (MateMinMilliseconds, MateMaxMilliseconds),
        _ => (0, 0),
    };

    /// <summary>A guard tank is the first to turn (×0.8); a pure caster takes a beat longer (×1.15); everyone else ×1.</summary>
    public static double RoleFactor(RvrClassTraits traits)
    {
        if (traits.HasFlag(RvrClassTraits.Melee) && traits.HasFlag(RvrClassTraits.Guard))
            return 0.8;
        if (traits.HasFlag(RvrClassTraits.Caster) && !traits.HasFlag(RvrClassTraits.Melee) && !traits.HasFlag(RvrClassTraits.Healer))
            return 1.15;
        return 1.0;
    }

    /// <summary>Aggression 15 reacts at ×1.3 of the drawn delay, 85 at ×0.75; a straight line between, clamped outside.</summary>
    public static double AggressionFactor(int aggression) => 1.3 - (Math.Clamp(aggression, 15, 85) - 15) / 70d * 0.55;

    /// <summary>
    /// The reaction delay of a hit: a draw in the kind's range (<paramref name="roll"/> in [0,1)), scaled by role and Aggression, plus
    /// the time to find an attacker the bot was not facing (<paramref name="unseen"/>), capped at <see cref="MaxHoldMilliseconds"/>.
    /// Zero for <see cref="BotIncomingFireKind.None"/>.
    /// </summary>
    public static int DelayMilliseconds(BotIncomingFireKind kind, RvrClassTraits traits, int aggression, double roll, bool unseen = false)
    {
        (int min, int max) = DelayRange(kind);
        if (max <= 0)
            return 0;
        double fraction = Math.Clamp(roll, 0, 0.999_999);
        double drawn = (min + fraction * (max - min)) * RoleFactor(traits) * AggressionFactor(aggression);
        if (unseen)
            drawn += UnseenMinMilliseconds + fraction * (UnseenMaxMilliseconds - UnseenMinMilliseconds);
        return (int)Math.Min(MaxHoldMilliseconds, Math.Round(drawn));
    }

    /// <summary>A far area splash is never the bot's personal call; every other kind (including none, for bots outside the rules) is.</summary>
    public static bool IsPersonalCall(BotIncomingFireKind kind) => kind != BotIncomingFireKind.Area;

    /// <summary>
    /// Whether a direct attacker overrides the kill order: only when the bot has no other live player-shaped target, or the attacker
    /// is in melee reach. A bot already fighting another player stays on it while a far attacker only scratches it; a bot fighting a
    /// mob drops the mob for the player at once, as players did.
    /// </summary>
    public static bool OverridesKillOrder(bool hasOtherPlayerTarget, bool attackerInMeleeReach) => !hasOtherPlayerTarget || attackerInMeleeReach;

    /// <summary>The hold is over at <paramref name="holdUntil"/>, unless an attacker is in melee reach, which ends it at once.</summary>
    public static bool Holds(long now, long holdUntil, bool attackerInMeleeReach) => holdUntil > 0 && !attackerInMeleeReach && now < holdUntil;

    /// <summary>
    /// After its own delay a follower whose leader has not yet engaged keeps waiting, up to <see cref="CallWaitMilliseconds"/> past
    /// the delay, for the call. A leader, a solo, a follower whose leader is gone, and a follower with only one enemy to pick (nothing
    /// to agree on) pick for themselves.
    /// </summary>
    public static bool WaitsForCall(long now, long holdUntil, bool follower, bool leaderEngaged, int enemies) =>
        follower && !leaderEngaged && enemies > 1 && holdUntil > 0 && now < holdUntil + CallWaitMilliseconds;

    // ------------------------------------------------------------ live reads

    /// <summary>The kind of this hit for this bot: None unless the rules apply and the attacker is another player-shaped fighter.</summary>
    public static BotIncomingFireKind KindOf(GameBot bot, AttackData ad)
    {
        if (!Applies(bot) || ad?.Attacker is not GameLiving attacker || attacker == bot || !PvpCombatant.IsPlayerShaped(attacker) ||
            PvpCombatant.Resolve(attacker) == bot)
            return BotIncomingFireKind.None;
        bool area = AutonomousDeathAttribution.IsAreaEffect(ad);
        bool fromReach = false;
        if (area && attacker.CurrentRegionID == bot.CurrentRegionID)
        {
            Spell spell = ad.SpellHandler.Spell;
            fromReach = AreaFromReach(spell.Range <= 0 || spell.IsPBAoE, bot.GetDistanceTo(attacker), spell.Radius,
                bot.MeleeAttackRange + MeleePressureReach);
        }
        return KindOf(ad.AttackType, area, fromReach);
    }

    /// <summary>The kind of a mate's hit by this attacker for this bot: None unless the rules apply and the attacker is player-shaped.</summary>
    public static BotIncomingFireKind MateKindOf(GameBot bot, GameLiving attacker, bool area) =>
        Applies(bot) && attacker != null && attacker != bot && PvpCombatant.IsPlayerShaped(attacker) ? MateKindOf(area) : BotIncomingFireKind.None;

    /// <summary>Draws this bot's reaction delay for a kind of hit, from its class traits, its Aggression and whether it faced the attacker.</summary>
    public static int Draw(GameBot bot, BotIncomingFireKind kind, GameLiving attacker)
    {
        if (kind == BotIncomingFireKind.None || bot == null)
            return 0;
        bool unseen = attacker != null && attacker.CurrentRegionID == bot.CurrentRegionID && !bot.IsObjectInFront(attacker, FrontArcDegrees);
        return DelayMilliseconds(kind, AutonomousRvrDoctrineRuntime.ClassTraits(bot), bot.PersistentRecord?.Aggression ?? 50,
            Util.RandomDouble(), unseen);
    }

    /// <summary>
    /// The bot's caller: the leader of its departed Gothwaite group, or in normal mode the living leader of a group whose doctrine
    /// has a caller. Null for a leader, a solo, or a bot without such a group.
    /// </summary>
    public static GameLiving CallerOf(GameBot bot)
    {
        if (bot == null)
            return null;
        if (GothwaitePvpMode.IsModeBot(bot))
            return GothwaitePvpCombat.LeaderOf(bot);
        if (AutonomousRvrDoctrineRuntime.For(bot)?.HasCaller != true)
            return null;
        return bot.Group?.LivingLeader is GameLiving leader && leader != bot ? leader : null;
    }

    /// <summary>
    /// True when the bot is a follower whose caller is alive, near and not yet fighting an enemy it may attack: the call has not
    /// come. False when the bot leads, roams alone, or its caller is dead, far or already engaged. A leader healing a friend is not
    /// engaged.
    /// </summary>
    public static bool AwaitsCall(GameBot bot, out bool follower)
    {
        follower = false;
        if (CallerOf(bot) is not { IsAlive: true } caller || caller.CurrentRegionID != bot.CurrentRegionID ||
            !bot.IsWithinRadius(caller, CallerRange))
            return false;
        follower = true;
        bool engaged = caller.TargetObject is GameLiving { IsAlive: true } target && (caller.IsAttacking || caller.IsCasting) &&
            PvpCombatant.IsPlayerShaped(target) && GameServer.ServerRules.IsAllowedToAttack(bot, target, true);
        return !engaged;
    }

    /// <summary>True when the attacker stands within the bot's melee range plus <see cref="MeleePressureReach"/>.</summary>
    public static bool InMeleeReach(GameLiving bot, GameLiving attacker) =>
        bot != null && attacker != null && attacker.CurrentRegionID == bot.CurrentRegionID &&
        bot.IsWithinRadius(attacker, bot.MeleeAttackRange + MeleePressureReach);
}
