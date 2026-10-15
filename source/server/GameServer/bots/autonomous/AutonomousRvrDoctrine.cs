using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>What a class brings to an RvR fight, as players of the era read it.</summary>
[Flags]
public enum RvrClassTraits
{
    None = 0,
    Healer = 1,
    AreaMez = 2,
    AreaStun = 4,
    PointBlankBomb = 8,
    Caster = 16,
    Pets = 32,
    Melee = 64,
    Guard = 128,
    Stealth = 256,
    Archer = 512,
    Speed = 1024,
}

public enum RvrDoctrineKind
{
    LoneRoamer,
    SoloAssassin,
    StealthPack,
    CasterDuo,
    SmallMan,
    AssistTrain,
    BombGroup,
    MeleeTrain,
    PetCasters,
    PickupGroup,
    GankSquad,
    ArcherGroup,
    KeepRaid,
    KeepDefense,
}

public enum RvrTravelShape { Clump, Column, Loose }

/// <summary>Where a doctrine likes to roam; weights for the roaming pick.</summary>
public readonly record struct RvrRoamTaste(double Keeps, double Clearings, double RecentFights, double Enemies);

/// <summary>
/// A group's shared understanding of what it is and how it plays. Values are
/// habits, not rules: <see cref="FollowCall"/> is the chance a member assists
/// the caller, <see cref="SupportPriority"/> scales how strongly enemy healers
/// and mezzers attract targets, <see cref="Appetite"/> how much bigger an enemy
/// group may be before the group still engages, <see cref="RetreatBias"/> how
/// early it leaves a losing fight.
/// </summary>
public sealed record RvrDoctrine(
    RvrDoctrineKind Kind,
    bool HasCaller,
    double FollowCall,
    double SupportPriority,
    double Appetite,
    double RetreatBias,
    int StickSeconds,
    int LingerMinSeconds,
    int LingerMaxSeconds,
    RvrTravelShape Travel,
    RvrRoamTaste Roam)
{
    public string Label => Kind switch
    {
        RvrDoctrineKind.LoneRoamer => "lone roamer",
        RvrDoctrineKind.SoloAssassin => "solo assassin",
        RvrDoctrineKind.StealthPack => "stealth pack",
        RvrDoctrineKind.CasterDuo => "caster duo",
        RvrDoctrineKind.SmallMan => "small-man",
        RvrDoctrineKind.AssistTrain => "assist train",
        RvrDoctrineKind.BombGroup => "bomb group",
        RvrDoctrineKind.MeleeTrain => "melee train",
        RvrDoctrineKind.PetCasters => "pet casters",
        RvrDoctrineKind.PickupGroup => "pickup group",
        RvrDoctrineKind.GankSquad => "gank squad",
        RvrDoctrineKind.ArcherGroup => "archer group",
        RvrDoctrineKind.KeepRaid => "keep raid party",
        _ => "keep defense",
    };
}

/// <summary>One member as the doctrine sees it.</summary>
public readonly record struct RvrDoctrineMember(eCharacterClass Class, int Level);

/// <summary>The leader's persisted personality (15-85 each).</summary>
public readonly record struct RvrLeaderTraits(int Aggression, int RiskTolerance, int Patience)
{
    public static RvrLeaderTraits Neutral => new(50, 50, 50);
}

public static class AutonomousRvrDoctrine
{
    public static RvrClassTraits TraitsOf(eCharacterClass characterClass) => characterClass switch
    {
        eCharacterClass.Armsman or eCharacterClass.Warrior or eCharacterClass.Hero =>
            RvrClassTraits.Melee | RvrClassTraits.Guard,
        eCharacterClass.Paladin => RvrClassTraits.Melee | RvrClassTraits.Guard,
        eCharacterClass.Mercenary or eCharacterClass.Reaver or eCharacterClass.Berserker or
            eCharacterClass.Savage or eCharacterClass.Blademaster or eCharacterClass.Champion or
            eCharacterClass.Vampiir or eCharacterClass.MaulerAlb or eCharacterClass.MaulerMid or
            eCharacterClass.Valkyrie => RvrClassTraits.Melee,
        eCharacterClass.Thane or eCharacterClass.Valewalker => RvrClassTraits.Melee | RvrClassTraits.Caster,
        eCharacterClass.Warden => RvrClassTraits.Melee | RvrClassTraits.Healer,
        eCharacterClass.Friar => RvrClassTraits.Melee | RvrClassTraits.Healer,
        eCharacterClass.Cleric => RvrClassTraits.Healer,
        eCharacterClass.Healer => RvrClassTraits.Healer | RvrClassTraits.AreaMez | RvrClassTraits.AreaStun,
        eCharacterClass.Shaman => RvrClassTraits.Healer | RvrClassTraits.Caster,
        eCharacterClass.Druid => RvrClassTraits.Healer | RvrClassTraits.Pets,
        eCharacterClass.Bard => RvrClassTraits.Healer | RvrClassTraits.AreaMez | RvrClassTraits.Speed,
        eCharacterClass.Minstrel => RvrClassTraits.Speed | RvrClassTraits.AreaMez | RvrClassTraits.Melee,
        eCharacterClass.Skald => RvrClassTraits.Speed | RvrClassTraits.Melee,
        eCharacterClass.Sorcerer => RvrClassTraits.Caster | RvrClassTraits.AreaMez | RvrClassTraits.Pets,
        eCharacterClass.Mentalist => RvrClassTraits.Caster | RvrClassTraits.AreaMez | RvrClassTraits.Healer,
        eCharacterClass.Wizard or eCharacterClass.Eldritch => RvrClassTraits.Caster | RvrClassTraits.PointBlankBomb,
        eCharacterClass.Enchanter => RvrClassTraits.Caster | RvrClassTraits.PointBlankBomb | RvrClassTraits.Pets,
        eCharacterClass.Spiritmaster => RvrClassTraits.Caster | RvrClassTraits.PointBlankBomb | RvrClassTraits.Pets,
        eCharacterClass.Runemaster or eCharacterClass.Bainshee or eCharacterClass.Warlock or
            eCharacterClass.Heretic => RvrClassTraits.Caster,
        eCharacterClass.Theurgist or eCharacterClass.Cabalist or eCharacterClass.Necromancer or
            eCharacterClass.Bonedancer or eCharacterClass.Animist => RvrClassTraits.Caster | RvrClassTraits.Pets,
        eCharacterClass.Infiltrator or eCharacterClass.Shadowblade or eCharacterClass.Nightshade =>
            RvrClassTraits.Stealth | RvrClassTraits.Melee,
        eCharacterClass.Scout or eCharacterClass.Ranger => RvrClassTraits.Archer | RvrClassTraits.Stealth,
        eCharacterClass.Hunter => RvrClassTraits.Archer | RvrClassTraits.Stealth | RvrClassTraits.Pets,
        _ => RvrClassTraits.Melee,
    };

    /// <summary>
    /// How strongly an enemy of this kind draws attention in an assist fight:
    /// whoever mezzes and heals dies first, the tank last.
    /// </summary>
    public static double TargetPriority(RvrClassTraits traits)
    {
        double priority = 1.0;
        if (traits.HasFlag(RvrClassTraits.Healer)) priority = Math.Max(priority, 3.0);
        if (traits.HasFlag(RvrClassTraits.AreaMez) || traits.HasFlag(RvrClassTraits.AreaStun))
            priority = Math.Max(priority, 2.6);
        if (traits.HasFlag(RvrClassTraits.PointBlankBomb)) priority = Math.Max(priority, 1.9);
        if (traits.HasFlag(RvrClassTraits.Caster)) priority = Math.Max(priority, 1.6);
        if (traits.HasFlag(RvrClassTraits.Speed) || traits.HasFlag(RvrClassTraits.Archer))
            priority = Math.Max(priority, 1.4);
        if (traits.HasFlag(RvrClassTraits.Stealth)) priority = Math.Max(priority, 1.3);
        if (traits.HasFlag(RvrClassTraits.Guard)) priority = Math.Min(priority, 0.8);
        return priority;
    }

    /// <summary>
    /// Reads a group's doctrine from its real members. The order of the checks
    /// is the order a player would name the group: "that's a bomb group", "a
    /// stealth pack", "just a pickup".
    /// </summary>
    public static RvrDoctrine Derive(IReadOnlyList<RvrDoctrineMember> members, AutonomousPlayerType leaderType,
        RvrLeaderTraits traits, bool siegeCommitted = false, bool defendingKeep = false)
    {
        members ??= [];
        int count = members.Count;
        RvrClassTraits[] kinds = members.Select(member => TraitsOf(member.Class)).ToArray();
        int Count(RvrClassTraits flag) => kinds.Count(kind => kind.HasFlag(flag));
        int healers = Count(RvrClassTraits.Healer);
        int stealth = kinds.Count(kind => kind.HasFlag(RvrClassTraits.Stealth) && !kind.HasFlag(RvrClassTraits.Archer));
        int archers = Count(RvrClassTraits.Archer);
        int melee = Count(RvrClassTraits.Melee);
        int pets = kinds.Count(kind => kind.HasFlag(RvrClassTraits.Pets) && !kind.HasFlag(RvrClassTraits.Healer));
        bool areaControl = kinds.Any(kind => kind.HasFlag(RvrClassTraits.AreaMez) || kind.HasFlag(RvrClassTraits.AreaStun));
        bool areaStun = members.Any(member => member.Class == eCharacterClass.Healer && member.Level >= 32);
        bool bomber = members.Any(member => TraitsOf(member.Class).HasFlag(RvrClassTraits.PointBlankBomb) && member.Level >= 20);
        bool speed = Count(RvrClassTraits.Speed) > 0;

        RvrDoctrineKind kind =
            defendingKeep ? RvrDoctrineKind.KeepDefense :
            siegeCommitted ? RvrDoctrineKind.KeepRaid :
            count <= 1 ? (stealth == 1 ? RvrDoctrineKind.SoloAssassin : RvrDoctrineKind.LoneRoamer) :
            leaderType == AutonomousPlayerType.Hunter && count <= 5 && stealth + archers >= 1 && count >= 3
                ? RvrDoctrineKind.GankSquad :
            stealth >= 2 && healers == 0 && stealth * 2 >= count ? RvrDoctrineKind.StealthPack :
            archers >= 2 && archers * 2 >= count ? RvrDoctrineKind.ArcherGroup :
            count == 2 && (healers >= 1 || areaControl) && kinds.Any(k => k.HasFlag(RvrClassTraits.Caster))
                ? RvrDoctrineKind.CasterDuo :
            count >= 4 && bomber && areaStun ? RvrDoctrineKind.BombGroup :
            pets * 2 >= count && count >= 3 ? RvrDoctrineKind.PetCasters :
            count >= 6 && healers >= 2 && areaControl && melee >= 2 ? RvrDoctrineKind.AssistTrain :
            count >= 5 && speed && melee * 2 > count && healers >= 1 ? RvrDoctrineKind.MeleeTrain :
            count <= 5 && healers >= 1 ? RvrDoctrineKind.SmallMan :
            RvrDoctrineKind.PickupGroup;

        return Tune(Base(kind), traits);
    }

    private static RvrDoctrine Base(RvrDoctrineKind kind) => kind switch
    {
        RvrDoctrineKind.SoloAssassin => new(kind, false, 0, 1.2, 0.7, 0.35, 25, 120, 300, RvrTravelShape.Loose,
            new(0.4, 0.8, 1.2, 1.4)),
        RvrDoctrineKind.StealthPack => new(kind, true, 0.8, 1.2, 0.9, 0.3, 20, 90, 240, RvrTravelShape.Loose,
            new(0.4, 0.8, 1.3, 1.4)),
        RvrDoctrineKind.CasterDuo => new(kind, true, 0.75, 0.9, 0.8, 0.25, 12, 60, 150, RvrTravelShape.Clump,
            new(1.3, 0.8, 0.8, 1.0)),
        RvrDoctrineKind.SmallMan => new(kind, true, 0.7, 1.0, 1.0, 0.15, 12, 45, 120, RvrTravelShape.Column,
            new(1.0, 0.9, 1.2, 1.3)),
        RvrDoctrineKind.AssistTrain => new(kind, true, 0.85, 1.0, 1.15, 0.0, 10, 60, 180, RvrTravelShape.Column,
            new(1.0, 0.7, 1.4, 1.3)),
        RvrDoctrineKind.BombGroup => new(kind, true, 0.6, 0.6, 1.3, 0.2, 8, 60, 180, RvrTravelShape.Clump,
            new(1.5, 0.6, 1.3, 1.0)),
        RvrDoctrineKind.MeleeTrain => new(kind, true, 0.8, 0.9, 1.2, 0.05, 10, 40, 120, RvrTravelShape.Column,
            new(0.9, 0.8, 1.5, 1.3)),
        RvrDoctrineKind.PetCasters => new(kind, true, 0.6, 0.8, 0.9, 0.15, 14, 90, 240, RvrTravelShape.Clump,
            new(1.6, 0.8, 0.8, 0.8)),
        RvrDoctrineKind.PickupGroup => new(kind, false, 0.45, 0.7, 0.85, 0.25, 16, 60, 180, RvrTravelShape.Clump,
            new(1.4, 0.9, 1.4, 0.9)),
        RvrDoctrineKind.GankSquad => new(kind, true, 0.75, 1.1, 0.8, 0.25, 15, 90, 240, RvrTravelShape.Loose,
            new(0.5, 1.2, 1.0, 1.5)),
        RvrDoctrineKind.ArcherGroup => new(kind, false, 0.5, 1.1, 0.8, 0.25, 18, 90, 240, RvrTravelShape.Loose,
            new(1.5, 0.8, 1.0, 1.0)),
        RvrDoctrineKind.KeepRaid => new(kind, true, 0.6, 0.8, 1.4, -0.1, 12, 60, 180, RvrTravelShape.Column,
            new(2.0, 0.3, 1.0, 0.8)),
        RvrDoctrineKind.KeepDefense => new(kind, false, 0.5, 1.0, 1.5, -0.2, 14, 120, 300, RvrTravelShape.Clump,
            new(2.0, 0.2, 0.8, 0.6)),
        _ => new(kind, false, 0, 0.8, 0.9, 0.2, 14, 60, 150, RvrTravelShape.Clump,
            new(1.0, 1.0, 1.0, 1.1)),
    };

    // Personality colours the habit: an aggressive leader takes worse odds, a
    // careless one stays in longer, a patient one sits on a spot longer.
    private static RvrDoctrine Tune(RvrDoctrine doctrine, RvrLeaderTraits traits)
    {
        double aggression = Math.Clamp(traits.Aggression, 0, 100) / 100d;
        double risk = Math.Clamp(traits.RiskTolerance, 0, 100) / 100d;
        double patience = Math.Clamp(traits.Patience, 0, 100) / 100d;
        double lingerScale = 0.7 + patience * 0.6;
        return doctrine with
        {
            Appetite = doctrine.Appetite * (0.75 + aggression * 0.5),
            RetreatBias = doctrine.RetreatBias + (0.5 - risk) * 0.3,
            LingerMinSeconds = (int)Math.Round(doctrine.LingerMinSeconds * lingerScale),
            LingerMaxSeconds = (int)Math.Round(doctrine.LingerMaxSeconds * lingerScale),
        };
    }

    /// <summary>
    /// Whether a group takes a fight. It compares effective numbers (own count
    /// times appetite) and levels; <paramref name="dareRoll"/> in [0,1) lets an
    /// eager group occasionally go in anyway, like players who "just try it".
    /// </summary>
    public static bool AcceptsFight(RvrDoctrine doctrine, int ownCount, int ownAverageLevel,
        int enemyCount, int enemyAverageLevel, double dareRoll)
    {
        if (doctrine == null)
            return !AutonomousPvpOpportunityPolicy.IsVisiblyStronger(ownCount, ownAverageLevel,
                enemyCount, enemyAverageLevel);
        double levelFactor = Math.Clamp(1 + (ownAverageLevel - enemyAverageLevel) * 0.06, 0.5, 1.4);
        double strength = Math.Max(1, ownCount) * doctrine.Appetite * levelFactor;
        if (enemyCount <= strength)
            return true;
        double dare = Math.Clamp((doctrine.Appetite - 0.9) * 0.4, 0, 0.2);
        return enemyCount <= strength * 1.6 && dareRoll < dare;
    }

    /// <summary>
    /// Whether a group leaves a fight now. Pressure comes from a dead healer,
    /// half the group down and being clearly outnumbered; the doctrine's bias
    /// and <paramref name="stayRoll"/> keep it a decision, not a reflex. A group
    /// that sees a chance may stay in.
    /// </summary>
    public static bool ShouldRetreat(RvrDoctrine doctrine, int aliveMembers, int formedMembers,
        int aliveHealers, int formedHealers, int enemiesNear, double stayRoll)
    {
        if (doctrine == null || formedMembers <= 0 || aliveMembers <= 0)
            return false;
        double pressure = 0;
        if (formedHealers > 0 && aliveHealers == 0) pressure += 0.45;
        if (aliveMembers * 2 <= formedMembers) pressure += 0.4;
        if (enemiesNear > aliveMembers * 1.5) pressure += 0.35;
        else if (enemiesNear > aliveMembers) pressure += 0.15;
        pressure += doctrine.RetreatBias;
        return pressure >= 0.6 && stayRoll >= 0.15;
    }

    /// <summary>
    /// The main pressure behind a retreat, for the log: <c>healer_dead</c>,
    /// <c>half_down</c>, <c>outnumbered</c>, or <c>doctrine</c> when only the
    /// doctrine's own caution (and small pressure) tipped it.
    /// </summary>
    public static string RetreatReason(int aliveMembers, int formedMembers, int aliveHealers, int formedHealers,
        int enemiesNear)
    {
        if (formedHealers > 0 && aliveHealers == 0) return "healer_dead";
        if (formedMembers > 0 && aliveMembers * 2 <= formedMembers) return "half_down";
        if (enemiesNear > aliveMembers) return "outnumbered";
        return "doctrine";
    }

    /// <summary>How long a member keeps its target before it may switch.</summary>
    public static int StickMilliseconds(RvrDoctrine doctrine, int patience) =>
        (int)Math.Round((doctrine?.StickSeconds ?? 12) * (0.6 + Math.Clamp(patience, 0, 100) / 100d) * 1000);

    /// <summary>A linger time between the doctrine's bounds.</summary>
    public static int LingerMilliseconds(RvrDoctrine doctrine, double roll)
    {
        int min = doctrine?.LingerMinSeconds ?? 60;
        int max = Math.Max(min, doctrine?.LingerMaxSeconds ?? 150);
        return (int)Math.Round((min + (max - min) * Math.Clamp(roll, 0, 1)) * 1000);
    }
}
