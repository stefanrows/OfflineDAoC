using System.Linq;
using DOL.Database;
using DOL.GS.RealmAbilities;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Majestic Will at its patch 1.68 shape: five levels, 1 / 3 / 6 / 10 / 14 points, 5 % per level
/// (25 % at level V) and a 10 minute recast. Characters trained under the old three level version keep
/// their level; the points spent are recomputed from the new costs, so they only ever get points back.
/// </summary>
[TestFixture]
public sealed class UT_MajesticWillRework
{
    private static AtlasOF_MajesticWill Ability(int level) =>
        new(new DbAbility { KeyName = "AtlasOF_MajesticWill", Name = "Majestic Will" }, level);

    [Test]
    public void HasFiveLevels()
    {
        Assert.That(Ability(1).MaxLevel, Is.EqualTo(5));
    }

    [Test]
    public void LevelCostsFollowTheCommonFiveLevelChart()
    {
        var ra = Ability(1);
        Assert.That(Enumerable.Range(0, 5).Select(ra.CostForUpgrade), Is.EqualTo(new[] { 1, 3, 6, 10, 14 }));
        Assert.That(Enumerable.Range(0, 5).Sum(ra.CostForUpgrade), Is.EqualTo(34));
        Assert.That(ra.CostForUpgrade(5), Is.GreaterThanOrEqualTo(1000), "no sixth level can be bought");
    }

    [Test]
    public void RecastIsTenMinutesAtEveryLevel()
    {
        for (int level = 1; level <= 5; level++)
            Assert.That(Ability(level).GetReUseDelay(level), Is.EqualTo(600), "level " + level);
    }

    [Test]
    public void BonusIsFivePercentPerLevelUpToTwentyFive()
    {
        var ra = Ability(1);
        Assert.That(Enumerable.Range(0, 7).Select(ra.GetAmountForLevel),
            Is.EqualTo(new[] { 0, 5, 10, 15, 20, 25, 25 }));
    }

    [Test]
    public void CharactersTrainedUnderTheOldThreeLevelVersionOnlyGainPoints()
    {
        // Old chart (3 level helper): 3 / 6 / 10. Spent points per level after training: 3, 9, 19.
        int[] oldSpent = { 0, 3, 9, 19 };
        var ra = Ability(1);
        for (int level = 1; level <= 3; level++)
        {
            int newSpent = Enumerable.Range(0, level).Sum(ra.CostForUpgrade);
            Assert.That(newSpent, Is.LessThan(oldSpent[level]), "level " + level + " keeps valid points and frees some");
        }
    }

    [Test]
    public void DelveTextStatesFivePercentPerLevelOnce()
    {
        string text = string.Join("\n", Ability(3).DelveInfo);
        Assert.That(text, Does.Contain("5% per level of this ability"));
        Assert.That(text, Does.Not.Contain("per level per level"));
    }
}
