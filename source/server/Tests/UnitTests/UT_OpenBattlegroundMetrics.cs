using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBattlegroundMetrics
{
    [Test]
    public void FieldLineReportsUnitsAndBotsOutWithAPercentage()
    {
        Assert.That(OpenBgMetrics.FieldLine(3, 9, 40, 72), Is.EqualTo("OPEN_BG_FIELD units_out=3/9 bots_out=40/72 pct=56"));
        Assert.That(OpenBgMetrics.FieldLine(0, 0, 0, 0), Is.EqualTo("OPEN_BG_FIELD units_out=0/0 bots_out=0/0 pct=0"), "an empty roster must not divide by zero");
    }
}
