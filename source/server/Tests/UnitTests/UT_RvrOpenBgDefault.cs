// Owner decision 2026-10-25 (bug 179, docs/RVR_WORLDS.md): the open BG is off by default in the RvR world (Realm-1),
// as upstream shadowofze/OfflineDAoC 0.38c has no open battleground; Camlann keeps its setting.
using System.Reflection;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public sealed class UT_RvrOpenBgDefault
{
    [TestCase(WorldModelKind.Realm, false, ExpectedResult = true, TestName = "RvR: an existing save without the default switches the open BG off once")]
    [TestCase(WorldModelKind.Realm, true, ExpectedResult = false, TestName = "RvR: a save that has the default keeps the owner's later setting")]
    [TestCase(WorldModelKind.Camlann, false, ExpectedResult = false, TestName = "Camlann: the open BG setting is never touched")]
    [TestCase(WorldModelKind.Camlann, true, ExpectedResult = false, TestName = "Camlann: the marker changes nothing")]
    public bool TheOpenBgIsSwitchedOffOnceOnlyInTheRvrWorld(WorldModelKind world, bool defaultApplied) =>
        OpenBattlegroundPolicy.TurnsOffForRvrWorld(world, defaultApplied);

    [TestCase(nameof(OpenBattlegroundProperties.OPEN_BG_ENABLED), "open_bg_enabled")]
    [TestCase(nameof(OpenBattlegroundProperties.OPEN_BG_RVR_DEFAULT_APPLIED), "open_bg_rvr_default_applied")]
    public void TheServerPropertyDefaultsAreOff(string field, string key)
    {
        var attribute = typeof(OpenBattlegroundProperties).GetField(field)
            .GetCustomAttribute<DOL.GS.ServerProperties.ServerPropertyAttribute>();
        Assert.Multiple(() =>
        {
            Assert.That(attribute.Key, Is.EqualTo(key));
            Assert.That(attribute.DefaultValue, Is.EqualTo(false), "a save without the row reads off");
        });
    }
}
