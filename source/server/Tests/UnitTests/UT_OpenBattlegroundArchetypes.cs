using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBattlegroundArchetypes
{
    [Test]
    public void TheStandardEightHasTwoHealersOneCcOneSpeedOneTankThreeMelee()
    {
        OpenBgArchetype std8 = OpenBattlegroundArchetypes.Get("std8");
        Assert.That(std8, Is.Not.Null);
        Assert.That(std8.Size, Is.EqualTo(8));
        int Min(OpenBgSlotRole role) => std8.Slots.Where(slot => slot.Role == role).Sum(slot => slot.Min);
        Assert.Multiple(() =>
        {
            Assert.That(Min(OpenBgSlotRole.Healer), Is.EqualTo(2));
            Assert.That(Min(OpenBgSlotRole.AreaCc), Is.EqualTo(1));
            Assert.That(Min(OpenBgSlotRole.Speed), Is.EqualTo(1));
            Assert.That(Min(OpenBgSlotRole.Tank), Is.EqualTo(1));
            Assert.That(Min(OpenBgSlotRole.Melee), Is.EqualTo(2));
        });
    }

    [Test]
    public void EveryArchetypeFitsItsSizeAndNoMinimumsExceedIt()
    {
        foreach (string key in new[] { "std8", "caster8", "raid8", "stealth4", "small", "duo" })
        {
            OpenBgArchetype archetype = OpenBattlegroundArchetypes.Get(key);
            Assert.That(archetype, Is.Not.Null, key);
            Assert.That(archetype.Slots.Sum(slot => slot.Min), Is.LessThanOrEqualTo(archetype.Size), key + " minimums fit the group");
        }
    }

    [Test]
    public void ARoleListsClassesOfAllThreeRealms()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundArchetypes.ClassesFor(OpenBgSlotRole.Healer), Does.Contain(eCharacterClass.Cleric));
            Assert.That(OpenBattlegroundArchetypes.ClassesFor(OpenBgSlotRole.Healer), Does.Contain(eCharacterClass.Healer));
            Assert.That(OpenBattlegroundArchetypes.ClassesFor(OpenBgSlotRole.Healer), Does.Contain(eCharacterClass.Druid));
            Assert.That(OpenBattlegroundArchetypes.ClassesFor(OpenBgSlotRole.Healer), Does.Not.Contain(eCharacterClass.Paladin), "a Paladin is no healer");
            Assert.That(OpenBattlegroundArchetypes.ClassesFor(OpenBgSlotRole.Speed), Is.SupersetOf(new[] { eCharacterClass.Minstrel, eCharacterClass.Skald, eCharacterClass.Bard }));
            Assert.That(OpenBattlegroundArchetypes.ClassesFor(OpenBgSlotRole.Assassin), Is.SupersetOf(new[] { eCharacterClass.Infiltrator, eCharacterClass.Shadowblade, eCharacterClass.Nightshade }));
        });
    }

    [Test]
    public void TheMixParsesCountsAndIgnoresGarbage()
    {
        var mix = OpenBattlegroundArchetypes.ParseMix("std8:2, caster8:1,nope:3,solo:x,stealth4:1");
        Assert.That(mix.Select(entry => (entry.Archetype.Key, entry.Count)), Is.EqualTo(new[] { ("std8", 2), ("caster8", 1), ("stealth4", 1) }));
        Assert.That(OpenBattlegroundArchetypes.ParseMix(""), Is.Empty);
        Assert.That(OpenBattlegroundArchetypes.ParseMix(null), Is.Empty);
    }

    [Test]
    public void FillPicksSlotsInPriorityOrderAndReportsMissedMinimums()
    {
        var pool = new[]
        {
            eCharacterClass.Paladin, eCharacterClass.Paladin, eCharacterClass.Friar, eCharacterClass.Cleric, eCharacterClass.Minstrel,
            eCharacterClass.Mercenary, eCharacterClass.Mercenary, eCharacterClass.Savage, eCharacterClass.Wizard,
        };
        OpenBgFill fill = OpenBattlegroundArchetypes.Fill(OpenBattlegroundArchetypes.Get("std8"), pool, OpenBgMode.ServerPvp);
        var picked = fill.PickedIndexes.Select(index => pool[index]).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(picked.Count(OpenBattlegroundRoster.IsRosterHealer), Is.EqualTo(2), "Cleric and Friar are the two healers");
            Assert.That(picked, Does.Contain(eCharacterClass.Minstrel), "speed and mez come from the Minstrel");
            Assert.That(fill.MissedMinimums, Is.Empty);
        });
    }

    [Test]
    public void FillWithoutAnyHealerReportsTheMissAndStillFillsTheRest()
    {
        var pool = Enumerable.Repeat(eCharacterClass.Mercenary, 9).ToArray();
        OpenBgFill fill = OpenBattlegroundArchetypes.Fill(OpenBattlegroundArchetypes.Get("std8"), pool, OpenBgMode.ServerPvp);
        Assert.That(fill.MissedMinimums, Does.Contain(OpenBgSlotRole.Healer));
        Assert.That(fill.PickedIndexes, Has.Length.EqualTo(8), "never a crew short because of a missed slot");
    }

    [Test]
    public void APaladinIsNeverARosterHealer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundRoster.IsRosterHealer(eCharacterClass.Paladin), Is.False);
            Assert.That(OpenBattlegroundRoster.IsRosterHealer(eCharacterClass.Druid), Is.True);
            Assert.That(OpenBattlegroundRoster.IsRosterHealer(eCharacterClass.Bard), Is.False);
        });
    }
}
