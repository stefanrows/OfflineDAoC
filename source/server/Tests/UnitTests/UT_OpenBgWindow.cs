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

    [Test]
    public void LastField_RoundTrips_AndStartsEmpty()
    {
        OpenBgMetrics.ResetLastField();
        Assert.That(OpenBgMetrics.TryGetLastField(out _), Is.False);
        OpenBgMetrics.PublishField(3, 12, 20, 72);
        Assert.That(OpenBgMetrics.TryGetLastField(out OpenBgFieldFigures field), Is.True);
        Assert.That(field, Is.EqualTo(new OpenBgFieldFigures(3, 12, 20, 72, 28)));
        Assert.That(OpenBgMetrics.FieldLine(3, 12, 20, 72), Does.Contain("pct=28"));
        OpenBgMetrics.ResetLastField();
    }

    [Test]
    public void BotStatus_ArchetypeDefaultsEmpty_AndUnknownUnitHasNone()
    {
        var bot = new OpenBgBotStatus("Albion", "A", 50, "Cleric", 1, "small", null, "inside", true);
        Assert.That(bot.Archetype, Is.EqualTo(""));
        Assert.That(OpenBattlegroundRoster.TryGetUnitArchetype(-12345, out string archetype), Is.False);
        Assert.That(archetype, Is.EqualTo(""));
        Assert.That(OpenBattlegroundStatus.ArchetypeOf(true, "classic8"), Is.EqualTo("classic8"));
        Assert.That(OpenBattlegroundStatus.ArchetypeOf(true, null), Is.EqualTo(""));
        Assert.That(OpenBattlegroundStatus.ArchetypeOf(false, "x"), Is.EqualTo(""));
    }
}
