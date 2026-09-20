using System;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.Tests.UnitTests;

[TestFixture]
public class UT_CamlannTier5KeepsRelics
{
    [Test]
    public void KeepAndRelicRowsPersistGuildOwnershipState()
    {
        DateTime claimedAt = DateTime.UtcNow;
        var keep = new DbKeep { Realm = 0, ClaimedGuildName = string.Empty, ClaimedAt = claimedAt };
        var relic = new DbRelic { KeepID = 1234, Realm = 0 };

        Assert.That(keep.Realm, Is.EqualTo(0));
        Assert.That(keep.ClaimedGuildName, Is.Empty);
        Assert.That(keep.ClaimedAt, Is.EqualTo(claimedAt));
        Assert.That(relic.KeepID, Is.EqualTo(1234));
    }

    [Test]
    public void EventObjectiveCarriesGuildOwnershipInsteadOfRealmOwnership()
    {
        var objective = new AutonomousRvrEventLayer.LiveObjective(
            "rvr-keep-123", "Keep", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.None, 163, 1, 2, 3, false, 0, 0, 0, 0,
            OwningGuild: "Camlann guild");

        Assert.That(objective.OwningRealm, Is.EqualTo(eRealm.None));
        Assert.That(objective.OwningGuild, Is.EqualTo("Camlann guild"));
    }
}
