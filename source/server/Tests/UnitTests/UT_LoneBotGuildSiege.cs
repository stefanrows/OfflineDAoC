using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Bug 122: a lone world bot was registered as an attacker of its guild's started
/// siege, then rejected ("A guild assault requires a formed party") and blocked
/// from that keep for 20 minutes, because only formed parties are ever released by
/// the guild army. The rule now applies when the target is selected: a lone bot
/// roams until the army has launched, then follows as a loose helper.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_LoneBotGuildSiege
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [SetUp] public void ResetEvents() => RvrEventTestState.Clear();
    [TearDown] public void ClearEvents() => RvrEventTestState.Clear();

    private static AutonomousRvrEventLayer.LiveObjective Keep(string owningGuild = "Owners") => new("keep-lone", "Dun Crauchon",
        AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Hibernia, 163, 100, 100, 0, false, 0, 0, 2, 2,
        OwningGuild: owningGuild);

    private static AutonomousRvrEventLayer.LiveObjective Roam() => new("roam-lone", "frontier patrol",
        AutonomousRvrEventLayer.Intent.Roam, eRealm.None, 163, 500, 500, 0, false, 0, 0, 0, 0);

    private static AutonomousRvrEventLayer.Force Force(string id, int members, string guild, eRealm realm = eRealm.Albion) =>
        new(id, realm, members, 50, members > 1 ? 2 : 0, GuildName: guild);

    private static void OpenSiege(AutonomousRvrEventLayer.LiveObjective keep)
    {
        var plan = AutonomousRvrEventLayer.ChooseOrJoin(Force("open", 8, "Raiders"), [keep], 1, 0);
        Assert.That(plan?.TargetId, Is.EqualTo(keep.Id));
        Assert.That(AutonomousRvrEventLayer.IsBattleForce("open", 2), Is.True, "the siege is a started automatic battle");
    }

    [Test]
    public void ALoneGuildmateDoesNotPickItsGuildsSiegeBeforeTheArmyLaunches()
    {
        var keep = Keep(); var roam = Roam();
        OpenSiege(keep);

        var plan = AutonomousRvrEventLayer.ChooseOrJoin(Force("rvr-724", 1, "Raiders", eRealm.Midgard), [keep, roam], 3, 0);

        Assert.Multiple(() =>
        {
            Assert.That(plan?.TargetId, Is.Not.EqualTo(keep.Id), "no keep target it will be rejected from");
            Assert.That(plan?.IsSharedEvent ?? false, Is.False);
            Assert.That(AutonomousRvrEventLayer.IsBattleForce("rvr-724", 4), Is.False, "never registered as an attacker");
            Assert.That(AutonomousRvrEventLayer.KeepPlan("rvr-724", eRealm.Midgard, 4), Is.Null);
        });
    }

    [Test]
    public void AFormedPartyOfTheSameGuildStillJoinsAndALoneBotOfAnotherGuildIsNotAffected()
    {
        var keep = Keep(); var roam = Roam();
        OpenSiege(keep);

        var party = AutonomousRvrEventLayer.ChooseOrJoin(Force("party", 8, "Raiders"), [keep, roam], 3, 0);
        var stranger = AutonomousRvrEventLayer.ChooseOrJoin(Force("rvr-9", 1, "Others"), [keep, roam], 4, 0);

        Assert.Multiple(() =>
        {
            Assert.That(party?.TargetId, Is.EqualTo(keep.Id), "a formed party reinforces its guild's siege");
            Assert.That(stranger?.TargetId, Is.Not.EqualTo(keep.Id), "a stranger guild's lone bot never contested a siege");
        });
    }

    [Test]
    public void ALoneOwnerStillHelpsDefendItsKeep()
    {
        var keep = Keep(); var roam = Roam();
        OpenSiege(keep);

        var defender = AutonomousRvrEventLayer.ChooseOrJoin(Force("rvr-1", 1, "Owners"), [keep, roam], 3, 0);

        Assert.That(defender?.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.DefendEvent),
            "lone guildmates keep helping defend; only the attack-side army gate applies to them");
    }

    [Test]
    public void ALoneGuildmateMayFollowAsAHelperOnceTheArmyHasLaunched()
    {
        var keep = Keep(); var roam = Roam();
        OpenSiege(keep);
        MarkArmyLaunched(keep.Id, "Raiders");

        var plan = AutonomousRvrEventLayer.ChooseOrJoin(Force("rvr-724", 1, "Raiders", eRealm.Midgard), [keep, roam], 3, 0);

        Assert.That(plan?.TargetId, Is.EqualTo(keep.Id));
        Assert.That(plan.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.AssaultKeep));
    }

    [Test]
    public void AFailedArmyDoesNotInviteLoneHelpers()
    {
        var keep = Keep(); var roam = Roam();
        OpenSiege(keep);
        MarkArmyLaunched(keep.Id, "Raiders", failed: true);

        var plan = AutonomousRvrEventLayer.ChooseOrJoin(Force("rvr-724", 1, "Raiders", eRealm.Midgard), [keep, roam], 3, 0);

        Assert.That(plan?.TargetId, Is.Not.EqualTo(keep.Id));
    }

    private sealed class ConfiguredServer : GameServer
    {
        private static readonly IObjectDatabase Empty =
            DispatchProxy.Create<IObjectDatabase, DOL.UnitTests.UT_UnobservedConcentration.EmptyReads>();
        private static readonly GameServerConfiguration Normal = new() { ServerType = EGameServerType.GST_Normal };
        protected override IObjectDatabase DataBaseImpl => Empty;
        public override GameServerConfiguration Configuration => Normal;
    }

    private static Guild FakeGuild(string name)
    {
        // Guild's static constructor reads the server configuration once.
        GameServer previous = GameServer.Instance;
        GameServer.LoadTestDouble((ConfiguredServer)RuntimeHelpers.GetUninitializedObject(typeof(ConfiguredServer)));
        try { RuntimeHelpers.RunClassConstructor(typeof(Guild).TypeHandle); }
        finally { GameServer.LoadTestDouble(previous); }
        var guild = (Guild)RuntimeHelpers.GetUninitializedObject(typeof(Guild));
        typeof(Guild).GetField("m_DBguild", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(guild, new DbGuild { GuildID = "guild-" + name, GuildName = name });
        return guild;
    }

    private static void MarkArmyLaunched(string targetId, string guildName, bool failed = false)
    {
        var layer = typeof(AutonomousRvrEventLayer);
        var events = (IDictionary)layer.GetField("Events", Any).GetValue(null);
        object active = events[targetId];
        object army = Activator.CreateInstance(layer.GetNestedType("GuildArmy", Any), true);
        Type armyType = army.GetType();
        armyType.GetField("Guild").SetValue(army, FakeGuild(guildName));
        armyType.GetField("Launched").SetValue(army, 100L);
        armyType.GetField("Failed").SetValue(army, failed);
        // No real leaders exist here; keep the periodic evaluation (which would
        // see no released party and reset the launch) out of this fixture.
        armyType.GetField("NextEvaluation").SetValue(army, long.MaxValue);
        ((IDictionary)active.GetType().GetField("GuildArmies").GetValue(active)).Add("guild-" + guildName, army);
    }
}
