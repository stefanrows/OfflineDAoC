using DOL.Database;
using DOL.GS;
using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Installed 0.242.0: 52 world bots of a bot guild were recalled
/// (GUILD_KEEP_RECALL) to defend Thidranki Faste (keep 11, region 252, the open
/// BG) because the guild claim made it an owned keep under attack. Battleground
/// keeps belong to their battleground crews; world bots never defend, assault
/// or roam to them.</summary>
[TestFixture]
public sealed class UT_WorldKeepPolicy
{
    // Frontiers, a dungeon region and the capitals are world regions.
    [TestCase(1, true)]
    [TestCase(100, true)]
    [TestCase(200, true)]
    [TestCase(163, true)]
    [TestCase(51, true)]
    // The ten level-bracket battlegrounds and the open BG (native Thidranki).
    [TestCase(234, false)]
    [TestCase(235, false)]
    [TestCase(236, false)]
    [TestCase(237, false)]
    [TestCase(238, false)]
    [TestCase(251, false)]
    [TestCase(240, false)]
    [TestCase(241, false)]
    [TestCase(242, false)]
    [TestCase(165, false)]
    [TestCase(252, false)]
    public void OnlyKeepsOutsideCampaignBattlegroundRegionsAreWorldKeeps(int region, bool expected) =>
        Assert.That(AutonomousRvrKeepPolicy.IsWorldKeep((ushort)region), Is.EqualTo(expected));

    [Test]
    public void EveryCampaignBattlegroundRegionIsExcluded()
    {
        foreach (BattlegroundDefinition definition in BattlegroundCampaignCatalog.Definitions)
            Assert.That(AutonomousRvrKeepPolicy.IsWorldKeep(definition.RegionId), Is.False, definition.Name);
    }

    [Test]
    public void TheOpenBgKeepAndABracketKeepAreNeverWorldKeepsButAFrontierKeepIs()
    {
        var openBg = new GameKeep { DBKeep = new DbKeep { BaseLevel = 50, Realm = 3, Region = 252 } };
        var bracket = new GameKeep { DBKeep = new DbKeep { BaseLevel = 39, Realm = 1, Region = 238 } };
        var frontier = new GameKeep { DBKeep = new DbKeep { BaseLevel = 50, Realm = 1, Region = 1 } };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrKeepPolicy.IsWorldKeep(openBg), Is.False, "open BG Thidranki Faste");
            Assert.That(AutonomousRvrKeepPolicy.IsWorldKeep(bracket), Is.False, "bracket Thidranki");
            Assert.That(AutonomousRvrKeepPolicy.IsWorldKeep(frontier), Is.True, "frontier keep");
            Assert.That(AutonomousRvrKeepPolicy.IsWorldKeep((AbstractGameKeep)null), Is.False);
        });
    }

    [Test]
    public void ABattlegroundKeepStaysASiegeObjectiveForItsOwnCrew()
    {
        // The exclusion is a world-bot filter only: the keep keeps its
        // siege classification, which the battleground crew relies on.
        var openBg = new GameKeep { DBKeep = new DbKeep { BaseLevel = 50, Realm = 3, Region = 252 } };
        Assert.That(AutonomousRvrKeepPolicy.IsSiegeObjective(openBg), Is.True);
    }
}
