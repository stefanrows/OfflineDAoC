using DOL.GS;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Camlann crews mix realms, so siege sides are guilds: the opener's guild
/// attacks, the owner's guild defends, everyone else contests on its own, and
/// a member's own realm never decides which rally it belongs to.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_CamlannGuildSiegeSides
{
    [SetUp] public void ResetEvents() => RvrEventTestState.Clear();
    [TearDown] public void ClearEvents() => RvrEventTestState.Clear();

    private static AutonomousRvrEventLayer.LiveObjective GuildKeep() => new("keep-g", "Dun Crauchon",
        AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Hibernia, 163, 100, 100, 0, false, 0, 0, 2, 2,
        OwningGuild: "Owners");

    private static AutonomousRvrEventLayer.Force Force(string id, eRealm realm, string guild) =>
        new(id, realm, 8, 50, 2, GuildName: guild);

    [Test]
    public void TheOpenersGuildAttacksAndAHostileGuildOfTheSameRealmDoesNot()
    {
        var keep = GuildKeep();
        AutonomousRvrEventLayer.ChooseOrJoin(Force("open", eRealm.Albion, "Raiders"), [keep], 1, 0);

        AutonomousRvrEventLayer.Plan mate = AutonomousRvrEventLayer.ChooseOrJoin(Force("mate", eRealm.Midgard, "Raiders"), [keep], 2, 0);
        AutonomousRvrEventLayer.Plan stranger = AutonomousRvrEventLayer.ChooseOrJoin(Force("stranger", eRealm.Albion, "Others"), [keep], 3, 0);

        Assert.Multiple(() =>
        {
            Assert.That(mate.TargetId, Is.EqualTo("keep-g"), "A Midgard guildmate still joins its guild's assault.");
            Assert.That(mate.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.AssaultKeep));
            Assert.That(stranger == null || stranger.Intent != AutonomousRvrEventLayer.Intent.DefendEvent, Is.True,
                "Sharing the opener's realm does not make a hostile guild an attacker's ally or a defender.");
        });
    }

    [Test]
    public void OnlyTheOwningGuildDefendsAGuildKeep()
    {
        var keep = GuildKeep();
        AutonomousRvrEventLayer.ChooseOrJoin(Force("open", eRealm.Albion, "Raiders"), [keep], 1, 0);

        AutonomousRvrEventLayer.Plan owner = AutonomousRvrEventLayer.ChooseOrJoin(Force("owner", eRealm.Albion, "Owners"), [keep], 2, 0);
        AutonomousRvrEventLayer.Plan hibStranger = AutonomousRvrEventLayer.ChooseOrJoin(Force("hib", eRealm.Hibernia, "Others"), [keep], 3, 0);

        Assert.Multiple(() =>
        {
            Assert.That(owner.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.DefendEvent),
                "The owners defend even when born in the attacker's realm.");
            Assert.That(hibStranger == null || hibStranger.Intent != AutonomousRvrEventLayer.Intent.DefendEvent, Is.True,
                "Sharing the keep's realm byte does not recruit a stranger guild as a defender.");
        });
    }

    [Test]
    public void RallyOrdersAndPlansFollowTheForceNotTheMembersRealm()
    {
        var keep = GuildKeep();
        AutonomousRvrEventLayer.ChooseOrJoin(Force("open", eRealm.Albion, "Raiders"), [keep], 1, 0);
        AutonomousRvrEventLayer.ChooseOrJoin(Force("owner", eRealm.Albion, "Owners"), [keep], 2, 0);

        foreach (eRealm memberRealm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            AutonomousRvrEventLayer.Plan attackerPlan = AutonomousRvrEventLayer.KeepPlan("open", memberRealm, 3);
            AutonomousRvrEventLayer.Plan defenderPlan = AutonomousRvrEventLayer.KeepPlan("owner", memberRealm, 3);
            Assert.That(attackerPlan?.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.AssaultKeep), memberRealm.ToString());
            Assert.That(defenderPlan?.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.DefendEvent), memberRealm.ToString());
        }
    }

    [Test]
    public void ContesterRealmIsDefinedForGuildlessDefenders()
    {
        Assert.That(AutonomousRvrEventLayer.ContesterRealm(eRealm.Albion, eRealm.None), Is.Not.EqualTo(eRealm.Albion));
        Assert.That(AutonomousRvrEventLayer.ContesterRealm(eRealm.Albion, eRealm.None), Is.Not.EqualTo(eRealm.None));
    }
}
