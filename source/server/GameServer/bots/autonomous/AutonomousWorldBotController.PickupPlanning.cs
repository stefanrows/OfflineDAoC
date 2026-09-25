using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    public sealed record PickupCamp(AutonomousBotGroupCoordinator.SharedCamp Camp, double Score);

    // Cheap catalog-only shortlist. Expensive navigation is limited separately
    // by the coordinator. A region needs a waiting local leader; everyone else
    // may join via its real town porter.
    public static PickupCamp[] PickupCampShortlist(GameBot seed, GameBot[] applicants, ISet<string> rejectedCamps = null)
    {
        if (seed?.CharacterClass == null || applicants.Length < 2) return [];
        var regions = applicants.Select(member => member.CurrentRegionID).ToHashSet();
        GameBot[] travelSample = applicants.GroupBy(member => member.CurrentRegionID)
            .SelectMany(group => group.Take(4)).Take(24).ToArray();
        var familiarCamps = applicants.Where(member => !string.IsNullOrEmpty(member.PersistentRecord?.CurrentCampId))
            .GroupBy(member => member.PersistentRecord.CurrentCampId)
            .ToDictionary(group => group.Key, group => group.Max(member => member.PersistentRecord.DeathCount));
        bool healing = applicants.Any(member => member.CharacterClass != null &&
            BotPartyRoles.IsHealingClass((eCharacterClass)member.CharacterClass.ID));
        bool frontline = applicants.Any(member => member.CharacterClass != null &&
            BotPartyRoles.For((eCharacterClass)member.CharacterClass.ID) == BotPartyRole.Tank);
        int preferred = seed.Level + (healing && frontline
            ? AutonomousGroupTargetPolicy.PreferredBonus(Math.Min(8, applicants.Length)) : 0);
        var ranked = CampCatalogSnapshot().Where(cell => rejectedCamps?.Contains(cell.Id) != true && regions.Contains(cell.RegionId) &&
                !cell.IsDungeon && !cell.IsFrontier && cell.LiveMobCount > 0 &&
                IsZoneAccessible(seed.Realm, cell.Zone))
            .Select(cell =>
            {
                int target = cell.Levels.Where(level => AutonomousGroupTargetPolicy.CanUseCampLevel(
                    level, seed.Level, seed.EffectiveLevel, preferred - seed.Level)).DefaultIfEmpty(0).Max();
                // The first shortlist estimate is conservative for remote actors.
                // Exact porter + onward costs decide the eventual invitations.
                double travel = travelSample.Select(member => member.CurrentRegionID == cell.RegionId
                    ? Vector2.Distance(new(member.X, member.Y), new(cell.X, cell.Y)) / Math.Max(1d, member.MaxSpeed) / 60d
                    : 5d).OrderBy(minutes => minutes).Take(8).Average();
                bool familiar = familiarCamps.TryGetValue(cell.Id, out int deaths);
                return new PickupCamp(new(cell.Id, cell.MonsterName, cell.ZoneName, cell.RegionId,
                    cell.X, cell.Y, cell.Z, false, false, target), AutonomousPickupPlanning.CampScore(
                        target, preferred, cell.LiveMobCount, AutonomousOutdoorCampPressure.Population(cell.Id),
                        AutonomousOutdoorCampPressure.WasRecentlyEmpty(cell.Id, GameLoop.GameLoopTime), travel, familiar, deaths));
            }).Where(choice => choice.Camp.TargetLevel > 0)
            .OrderBy(choice => choice.Score).ThenBy(choice => choice.Camp.Id, StringComparer.Ordinal).ToArray();
        // Always retain a local alternative so an attractive distant spot cannot
        // monopolize every formation pass when its town routes are unavailable.
        var regionChoices = ranked.GroupBy(choice => choice.Camp.RegionId).Select(group => group.Take(2).ToArray()).ToArray();
        PickupCamp[] primary = regionChoices.Select(group => group[0]).ToArray();
        PickupCamp[] preferredRegions = primary.Take(2)
            .Concat(primary.Where(choice => choice.Camp.RegionId == seed.CurrentRegionID).Take(1))
            .Concat(primary).DistinctBy(choice => choice.Camp.RegionId).Take(3).ToArray();
        var retainedRegions = preferredRegions.Select(choice => choice.Camp.RegionId).ToHashSet();
        return preferredRegions.Concat(regionChoices.Where(group => group.Length > 1 &&
            retainedRegions.Contains(group[0].Camp.RegionId)).Select(group => group[1])).ToArray();
    }

    public static bool IsPickupCampUsable(string campId, GameBot[] members)
    {
        if (members == null || members.Length < 2) return false;
        bool healing = members.Any(member => member.CharacterClass != null &&
            BotPartyRoles.IsHealingClass((eCharacterClass)member.CharacterClass.ID));
        bool frontline = members.Any(member => member.CharacterClass != null &&
            BotPartyRoles.For((eCharacterClass)member.CharacterClass.ID) == BotPartyRole.Tank);
        int bonus = healing && frontline ? AutonomousGroupTargetPolicy.PreferredBonus(members.Length) : 0;
        return CampCatalogSnapshot().Any(cell => cell.Id == campId && cell.LiveMobCount > 0 &&
            !cell.IsDungeon && !cell.IsFrontier && CampUsableByEveryMember(cell, members, bonus));
    }
}
