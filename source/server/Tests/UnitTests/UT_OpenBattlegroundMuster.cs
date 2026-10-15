using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBattlegroundMuster
{
    [Test]
    public void AMemberIsPortedOutOnlyWhenItIsNotAlreadyAtTheLeadersLanding()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundMuster.NeedsPortOut(200, 252, false), Is.True, "Hibernian frontier member, leader inside the BG");
            Assert.That(OpenBattlegroundMuster.NeedsPortOut(252, 252, true), Is.False, "already at the landing");
            Assert.That(OpenBattlegroundMuster.NeedsPortOut(252, 252, false), Is.True, "in the BG but elsewhere: out and back in");
            Assert.That(OpenBattlegroundMuster.NeedsPortOut(200, 200, true), Is.False, "both outside, the normal porter boarding applies");
        });
    }
}
