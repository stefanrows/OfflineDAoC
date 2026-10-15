using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBattlegroundMuster
{
    [Test]
    public void PoolsAreMergedOnlyForPresetUnitsInServerPvp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundMuster.PoolsMerged(OpenBgMode.ServerPvp, true), Is.True);
            Assert.That(OpenBattlegroundMuster.PoolsMerged(OpenBgMode.ServerPvp, false), Is.False, "no preset: per-realm pools as before");
            Assert.That(OpenBattlegroundMuster.PoolsMerged(OpenBgMode.PerRealm, true), Is.False, "Per realm keeps its pools");
        });
    }

    [Test]
    public void AMemberOfAnotherRegionIsAcceptedOnlyWhenPoolsAreMerged()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundMuster.AcceptsMember(false, true, true), Is.True, "same region within porter reach, as today");
            Assert.That(OpenBattlegroundMuster.AcceptsMember(false, true, false), Is.False, "same region too far, as today");
            Assert.That(OpenBattlegroundMuster.AcceptsMember(false, false, true), Is.False, "other region, per-realm pools");
            Assert.That(OpenBattlegroundMuster.AcceptsMember(true, false, false), Is.True, "other region, remote meetup brings it");
            Assert.That(OpenBattlegroundMuster.AcceptsMember(true, true, false), Is.False, "merged does not waive porter reach in the leader's region");
        });
    }

    [Test]
    public void ARemoteMemberRegionIsAcceptedOnlyWhenAllowed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundMuster.RegionAccepted(false, 1, 1), Is.True);
            Assert.That(OpenBattlegroundMuster.RegionAccepted(true, 1, 1), Is.True);
            Assert.That(OpenBattlegroundMuster.RegionAccepted(false, 200, 1), Is.False, "default rejects another region");
            Assert.That(OpenBattlegroundMuster.RegionAccepted(true, 200, 1), Is.True, "remote meetup brings it");
        });
    }

    [Test]
    public void AMergedUnitIsLedByAMemberOfTheAdmitRealm()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundMuster.SeedAllowed(true, eRealm.Hibernia, eRealm.Albion), Is.False);
            Assert.That(OpenBattlegroundMuster.SeedAllowed(true, eRealm.Albion, eRealm.Albion), Is.True);
            Assert.That(OpenBattlegroundMuster.SeedAllowed(false, eRealm.Hibernia, eRealm.Albion), Is.True);
        });
    }
}
