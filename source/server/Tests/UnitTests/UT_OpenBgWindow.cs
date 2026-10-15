using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBgWindow
{
    [Test]
    public void TryParseBgControl_AcceptsAllByteValues_AndRejectsMalformedInput()
    {
        Assert.That(CompanionManagerProtocol.TryParseBgControl("0001", "ff", out ushort revision, out int value), Is.True);
        Assert.That(revision, Is.EqualTo(1));
        Assert.That(value, Is.EqualTo(255));
        Assert.That(CompanionManagerProtocol.TryParseBgControl("00a0", "00", out revision, out value), Is.True);
        Assert.That((revision, value), Is.EqualTo((0xA0, 0)));
        Assert.That(CompanionManagerProtocol.TryParseBgControl("0001", "FF", out _, out _), Is.False);
        Assert.That(CompanionManagerProtocol.TryParseBgControl("001", "ff", out _, out _), Is.False);
        Assert.That(CompanionManagerProtocol.TryParseBgControl("0001", "f", out _, out _), Is.False);
        Assert.That(CompanionManagerProtocol.TryParseBgControl("000g", "ff", out _, out _), Is.False);
        Assert.That(CompanionManagerProtocol.TryParseBgControl(null, "ff", out _, out _), Is.False);
    }
}
