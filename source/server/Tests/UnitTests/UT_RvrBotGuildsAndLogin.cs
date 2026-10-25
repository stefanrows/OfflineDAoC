using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.UnitTests;

/// <summary>
/// RvR package R4a (docs/RVR_BOTS.md, "Guilds and login"): bot guild identity per realm (realm names, a
/// balanced charter mix per realm, no cross-realm recruitment), the realm login balancer with a target per
/// realm, and grudges keyed by realm. Every rule is checked in both worlds; Camlann answers as before.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrBotGuildsAndLogin
{
    private static readonly eRealm[] Realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];
    private static readonly AutonomousGuildCharter[] Charters = Enum.GetValues<AutonomousGuildCharter>();

    private static AutonomousGuildSeeding.GuildPool[] RvrPools(int perRealm) =>
        AutonomousGuildSeeding.PlanPools(WorldModelKind.Realm,
            Enumerable.Range(0, perRealm * 3).Select(index => (eRealm)(1 + index % 3)));

    // --- Guild names -------------------------------------------------------------------------------------

    [Test]
    public void EveryRealmHasEightValidUniqueNamesPerCharter()
    {
        var all = new List<string>();
        foreach (eRealm realm in Realms)
        foreach (AutonomousGuildCharter charter in Charters)
        {
            IReadOnlyList<string> names = AutonomousRealmGuildNames.For(realm, charter);
            Assert.That(names, Has.Count.EqualTo(8), $"{realm} {charter}");
            all.AddRange(names);
        }

        Assert.That(all.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(all.Count), "unique across realms");
        Assert.That(all.Where(name => !GuildCommandHandler.IsValidGuildName(name)), Is.Empty, "letters and spaces only");
        Assert.That(all.Where(name => name.Length > 30), Is.Empty);
        Assert.That(all.Where(name => name.Contains("Camlann", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(AutonomousCrewManager.RealmCrewNamePrefix, StringComparison.Ordinal)), Is.Empty);
    }

    [Test]
    public void RealmNamesNeverReuseTheCamlannCrewNames()
    {
        string[] camlann = Charters.SelectMany(charter => Enumerable.Range(0, 6)
            .Select(ordinal => AutonomousCrewManager.NameForOrdinal(ordinal, charter))).ToArray();

        foreach (eRealm realm in Realms)
        {
            Assert.That(AutonomousRealmGuildNames.All(realm).Intersect(camlann), Is.Empty, realm.ToString());
            Assert.That(camlann.Where(name => AutonomousRealmGuildNames.IsRealmName(realm, name)), Is.Empty,
                "an R5a-era Camlann-style name counts as legacy and is renamed once");
        }
    }

    [Test]
    public void PickTakesTheFirstFreeNameAndNumbersOnlyWhenAllAreTaken()
    {
        IReadOnlyList<string> names = AutonomousRealmGuildNames.For(eRealm.Midgard, AutonomousGuildCharter.Keep);
        var taken = new HashSet<string>(StringComparer.Ordinal) { names[0], names[1] };

        Assert.That(AutonomousRealmGuildNames.Pick(eRealm.Midgard, AutonomousGuildCharter.Keep, taken.Contains), Is.EqualTo(names[2]));

        taken.UnionWith(names);
        string numbered = AutonomousRealmGuildNames.Pick(eRealm.Midgard, AutonomousGuildCharter.Keep, taken.Contains);
        Assert.That(numbered, Is.EqualTo(names[0] + " 2"));
        Assert.That(AutonomousRealmGuildNames.IsRealmName(eRealm.Midgard, numbered), Is.True);
        Assert.That(AutonomousRealmGuildNames.IsRealmName(eRealm.Albion, numbered), Is.False, "another realm's name");
        Assert.That(AutonomousRealmGuildNames.IsRealmName(eRealm.Midgard, AutonomousCrewManager.RealmCrewNamePrefix + "17"), Is.False);
        Assert.That(AutonomousRealmGuildNames.Pick(eRealm.None, AutonomousGuildCharter.Keep, null), Is.Null);
    }

    [Test]
    public void FortyFiveRvrGuildsGetRealmNamesWithoutANumber()
    {
        foreach (AutonomousGuildSeeding.GuildPool pool in RvrPools(600))
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (int ordinal in Enumerable.Range(pool.FirstOrdinal, pool.GuildCount))
            {
                AutonomousGuildCharter charter = AutonomousGuildSeeding.CharterFor(WorldModelKind.Realm, pool, ordinal,
                    BotGoalSettings.Defaults.Mix);
                string name = AutonomousRealmGuildNames.Pick(pool.Realm, charter, used.Contains);
                Assert.That(used.Add(name), Is.True);
                Assert.That(name, Does.Not.Match(@"\d"), $"{pool.Realm} slot {ordinal - pool.FirstOrdinal}");
            }
        }
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TheTemporaryCreateMarkerIsACrewGuildOnlyInTheRvrWorld(WorldModelKind world)
    {
        Assert.That(AutonomousCrewManager.IsCrewMarkerName(world, AutonomousCrewManager.RealmCrewNamePrefix + "3"),
            Is.EqualTo(BothWorlds.Expect(world, false, true)));
        Assert.That(AutonomousCrewManager.IsCrewMarkerName(world, AutonomousCrewManager.CrewNamePrefix + "Stone Ward"), Is.True,
            "the Camlann marker is recognised in both worlds");
        Assert.That(AutonomousCrewManager.IsCrewMarkerName(world, "Lions of Albion"), Is.False, "a final realm name is managed by its charter row");
        Assert.That(AutonomousCrewManager.IsCrewMarkerName(world, null), Is.False);
    }

    // --- Charter mix per realm ---------------------------------------------------------------------------

    [TestCase(PopulationPreset.Camlann2003)]
    [TestCase(PopulationPreset.Peaceful)]
    [TestCase(PopulationPreset.Bloodbath)]
    [TestCase(PopulationPreset.KeepWars)]
    public void ACharterPlanFollowsTheMixByGuildSize(PopulationPreset preset)
    {
        PlayerTypeMix mix = BotGoalSettings.ForPreset(preset).Mix;
        int[] scores = AutonomousBotIdentity.CharterScores(mix);
        AutonomousGuildCharter[] plan = AutonomousGuildSeeding.PlanCharters(mix);
        int totalWeight = Enumerable.Range(0, plan.Length).Sum(AutonomousGuildSeeding.SlotWeight);

        Assert.That(plan, Has.Length.EqualTo(15));
        Assert.That(totalWeight, Is.EqualTo(35));
        for (int charter = 0; charter < scores.Length; charter++)
        {
            double share = scores[charter] / (double)scores.Sum() * totalWeight;
            int weight = Enumerable.Range(0, plan.Length).Where(slot => (int)plan[slot] == charter).Sum(AutonomousGuildSeeding.SlotWeight);
            Assert.That(weight, Is.InRange(share - 4, share + 4), $"{(AutonomousGuildCharter)charter}: weight {weight}, share {share:F1}");
            if (share >= 2)
                Assert.That(plan, Has.Member((AutonomousGuildCharter)charter), $"{(AutonomousGuildCharter)charter} has a guild");
        }
    }

    [Test]
    public void TheDefaultMixGivesEveryRealmAKeepGuild()
    {
        PlayerTypeMix mix = BotGoalSettings.Defaults.Mix;
        foreach (AutonomousGuildSeeding.GuildPool pool in RvrPools(600))
        {
            AutonomousGuildCharter[] charters = Enumerable.Range(pool.FirstOrdinal, pool.GuildCount)
                .Select(ordinal => AutonomousGuildSeeding.CharterFor(WorldModelKind.Realm, pool, ordinal, mix)).ToArray();
            Assert.That(charters, Has.Member(AutonomousGuildCharter.Keep), pool.Realm.ToString());
            Assert.That(charters, Has.Member(AutonomousGuildCharter.Rvr), pool.Realm.ToString());
        }
    }

    [Test]
    public void AllThreeRealmsGetTheSameCharterSequence()
    {
        PlayerTypeMix mix = BotGoalSettings.ForPreset(PopulationPreset.KeepWars).Mix;
        AutonomousGuildCharter[][] perRealm = RvrPools(600)
            .Select(pool => Enumerable.Range(pool.FirstOrdinal, pool.GuildCount)
                .Select(ordinal => AutonomousGuildSeeding.CharterFor(WorldModelKind.Realm, pool, ordinal, mix)).ToArray())
            .ToArray();

        Assert.That(perRealm, Has.Length.EqualTo(3));
        Assert.That(perRealm[1], Is.EqualTo(perRealm[0]));
        Assert.That(perRealm[2], Is.EqualTo(perRealm[0]));
    }

    [Test]
    public void ASmallRealmUsesAPrefixOfThePlanAndKeepsItWhenItGrows()
    {
        PlayerTypeMix mix = BotGoalSettings.Defaults.Mix;
        AutonomousGuildCharter[] full = AutonomousGuildSeeding.PlanCharters(mix);

        for (int slots = 1; slots <= 15; slots++)
            Assert.That(AutonomousGuildSeeding.PlanCharters(mix, slots), Is.EqualTo(full.Take(slots)));
    }

    [Test]
    public void ACharterTheMixDoesNotAskForGetsNoGuild()
    {
        var mix = new PlayerTypeMix(40, 20, 0, 40, 0, 0); // no Hybrid, Roamer or KeepWarrior: no RvR or Keep charter

        AutonomousGuildCharter[] plan = AutonomousGuildSeeding.PlanCharters(mix);

        Assert.That(plan, Has.No.Member(AutonomousGuildCharter.Rvr));
        Assert.That(plan, Has.No.Member(AutonomousGuildCharter.Keep));
    }

    [Test]
    public void CamlannChartersStayTheGlobalOrdinalDraw()
    {
        PlayerTypeMix mix = BotGoalSettings.Defaults.Mix;
        AutonomousGuildSeeding.GuildPool pool = AutonomousGuildSeeding.PlanPools(WorldModelKind.Camlann,
            Enumerable.Repeat(eRealm.Albion, 1800))[0];

        foreach (int ordinal in Enumerable.Range(0, 45))
        {
            Assert.That(AutonomousGuildSeeding.CharterFor(WorldModelKind.Camlann, pool, ordinal, mix),
                Is.EqualTo(AutonomousBotIdentity.CharterForOrdinal(ordinal, mix)));
            Assert.That(AutonomousGuildSeeding.CharterFor(WorldModelKind.Camlann, pool, ordinal, null),
                Is.EqualTo(AutonomousBotIdentity.CharterForOrdinal(ordinal)));
        }
        Assert.That(AutonomousBotIdentity.CharterScores(mix), Is.EqualTo(new[] { 22, 27, 11, 32, 16 }));
    }

    // --- No cross-realm recruitment ----------------------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void AGuildRecruitsOnlyItsRealmInTheRvrWorld(WorldModelKind world)
    {
        Assert.That(AutonomousGuildSeeding.MayJoin(world, eRealm.Albion, eRealm.Midgard), Is.EqualTo(BothWorlds.Expect(world, true, false)));
        Assert.That(AutonomousGuildSeeding.MayJoin(world, eRealm.Hibernia, eRealm.Hibernia), Is.True);
        Assert.That(AutonomousGuildSeeding.MayJoin(world, eRealm.Albion, eRealm.None), Is.EqualTo(BothWorlds.Expect(world, true, false)));
    }

    [Test]
    public void AGuildsRecruitingRealmFallsBackToTheInvitersRealm()
    {
        Assert.That(AutonomousCrewManager.GuildRealm(eRealm.Midgard, eRealm.Albion), Is.EqualTo(eRealm.Midgard));
        Assert.That(AutonomousCrewManager.GuildRealm(eRealm.None, eRealm.Hibernia), Is.EqualTo(eRealm.Hibernia),
            "an old realm-less guild recruits its inviter's realm");
        Assert.That(AutonomousCrewManager.GuildRealm(null, null), Is.EqualTo(eRealm.None));
    }

    [Test]
    public void TheAltTrickleFeedsTheSmallestRealmThatHasAGuild()
    {
        eRealm[] roster = [.. Enumerable.Repeat(eRealm.Albion, 600), .. Enumerable.Repeat(eRealm.Midgard, 590),
            .. Enumerable.Repeat(eRealm.Hibernia, 610)];

        Assert.That(AutonomousGuildSeeding.AltRealm(roster, Realms), Is.EqualTo(eRealm.Midgard));
        Assert.That(AutonomousGuildSeeding.AltRealm(roster, [eRealm.Albion, eRealm.Hibernia]), Is.EqualTo(eRealm.Albion),
            "a realm without a managed guild cannot take the alt");
        Assert.That(AutonomousGuildSeeding.AltRealm([.. Enumerable.Repeat(eRealm.Albion, 5), .. Enumerable.Repeat(eRealm.Hibernia, 5)],
            [eRealm.Hibernia, eRealm.Albion]), Is.EqualTo(eRealm.Albion), "ties in realm order");
        Assert.That(AutonomousGuildSeeding.AltRealm(roster, [eRealm.None]), Is.Null);
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void AnObsoleteGuildPassesItsClaimsOnlyToItsOwnRealmInRvr(WorldModelKind world)
    {
        AutonomousGuildSeeding.GuildPool[] pools = RvrPools(56);
        var plan = pools.Select(pool => (pool, AutonomousCrewManager.BuildTargets(
            Enumerable.Range(0, pool.GuildCount).Select(index => $"{pool.Realm}-{index}").ToArray(), pool.BotCount))).ToArray();
        string[] all = plan.SelectMany(entry => entry.Item2).Select(target => target.GuildId).ToArray();

        for (int sequence = 0; sequence < 7; sequence++)
        {
            string target = AutonomousGuildSeeding.ObsoleteGuildTarget(world, eRealm.Hibernia, sequence, plan);
            if (world == WorldModelKind.Realm)
                Assert.That(target, Does.StartWith("Hibernia-"));
            else
                Assert.That(target, Is.EqualTo(all[sequence % all.Length]), "Camlann: round robin over every target, as before");
        }
        Assert.That(AutonomousGuildSeeding.ObsoleteGuildTarget(world, eRealm.None, 0, plan), Is.EqualTo(all[0]),
            "no guild of that realm left: any target, so the guild can still be removed");
        Assert.That(AutonomousGuildSeeding.ObsoleteGuildTarget(world, eRealm.Albion, 0, []), Is.Null);
    }

    // --- Realm login balancer ----------------------------------------------------------------------------

    [Test]
    public void RealmTargetsCapEachRealmAtTheTargetOrItsRoster()
    {
        eRealm[] roster = [.. Enumerable.Repeat(eRealm.Albion, 650), .. Enumerable.Repeat(eRealm.Midgard, 600),
            .. Enumerable.Repeat(eRealm.Hibernia, 300)];

        Assert.That(AutonomousRealmLoginBalancer.Targets(600, roster), Is.EquivalentTo(new Dictionary<eRealm, int>
            { [eRealm.Albion] = 600, [eRealm.Midgard] = 600, [eRealm.Hibernia] = 300 }));
        Assert.That(AutonomousRealmLoginBalancer.Targets(0, roster), Is.EquivalentTo(new Dictionary<eRealm, int>
            { [eRealm.Albion] = 650, [eRealm.Midgard] = 600, [eRealm.Hibernia] = 300 }), "0 = every bot of each realm");
        Assert.That(AutonomousRealmLoginBalancer.Shortfalls(600, roster), Is.EquivalentTo(new Dictionary<eRealm, int>
            { [eRealm.Hibernia] = 300 }));
        Assert.That(AutonomousRealmLoginBalancer.Shortfalls(0, roster), Is.Empty);
        Assert.That(AutonomousRealmLoginBalancer.Targets(600, []).Values, Is.All.EqualTo(0));
    }

    [Test]
    public void TheLoginRampRaisesAllRealmsEvenlyToTheirTargets()
    {
        eRealm[] roster = [.. Enumerable.Repeat(eRealm.Hibernia, 700), .. Enumerable.Repeat(eRealm.Midgard, 640),
            .. Enumerable.Repeat(eRealm.Albion, 620)];
        Dictionary<eRealm, int> targets = AutonomousRealmLoginBalancer.Targets(600, roster);
        var available = roster.GroupBy(realm => realm).ToDictionary(group => group.Key, group => group.Count());
        var load = new Dictionary<eRealm, int>();

        int logins = 0;
        for (eRealm? realm; (realm = AutonomousRealmLoginBalancer.SelectRealm(targets, load,
                 available.Where(pair => pair.Value > 0).Select(pair => pair.Key))).HasValue; logins++)
        {
            load[realm.Value] = load.GetValueOrDefault(realm.Value) + 1;
            available[realm.Value]--;
            if (logins + 1 is 300 or 900)
                Assert.That(load.Values.Max() - load.Values.Min(), Is.LessThanOrEqualTo(1), $"even after {logins + 1} logins");
        }

        Assert.That(logins, Is.EqualTo(1800));
        Assert.That(load.Values, Is.All.EqualTo(600), "each realm stops at its target although it has more bots");
    }

    [Test]
    public void AShortRealmLogsInAllItHasAndTheOthersDoNotFillItsGap()
    {
        eRealm[] roster = [.. Enumerable.Repeat(eRealm.Albion, 600), .. Enumerable.Repeat(eRealm.Midgard, 250),
            .. Enumerable.Repeat(eRealm.Hibernia, 600)];
        Dictionary<eRealm, int> targets = AutonomousRealmLoginBalancer.Targets(600, roster);
        var available = roster.GroupBy(realm => realm).ToDictionary(group => group.Key, group => group.Count());
        var load = new Dictionary<eRealm, int>();

        for (eRealm? realm; (realm = AutonomousRealmLoginBalancer.SelectRealm(targets, load,
                 available.Where(pair => pair.Value > 0).Select(pair => pair.Key))).HasValue;)
        {
            load[realm.Value] = load.GetValueOrDefault(realm.Value) + 1;
            available[realm.Value]--;
            if (load.Values.Sum() == 750)
                Assert.That(load.Values, Is.All.EqualTo(250), "the ramp stays even while the short realm still has bots");
        }

        Assert.That(load, Is.EquivalentTo(new Dictionary<eRealm, int>
            { [eRealm.Albion] = 600, [eRealm.Midgard] = 250, [eRealm.Hibernia] = 600 }));
    }

    [Test]
    public void ARealmAtOrAboveItsTargetGetsNoLoginAndNoRealmMeansNone()
    {
        var targets = new Dictionary<eRealm, int> { [eRealm.Albion] = 600, [eRealm.Midgard] = 600, [eRealm.Hibernia] = 600 };
        var load = new Dictionary<eRealm, int> { [eRealm.Albion] = 700, [eRealm.Midgard] = 600, [eRealm.Hibernia] = 10 };

        Assert.That(AutonomousRealmLoginBalancer.SelectRealm(targets, load, Realms), Is.EqualTo(eRealm.Hibernia));
        Assert.That(AutonomousRealmLoginBalancer.SelectRealm(targets, load, [eRealm.Albion, eRealm.Midgard]), Is.Null,
            "the owner lowered the target: nobody is logged out, nobody more logs in");
        Assert.That(AutonomousRealmLoginBalancer.SelectRealm(targets, load, [eRealm.None]), Is.Null);
        Assert.That(AutonomousRealmLoginBalancer.SelectRealm(null, null, Realms), Is.Null);
    }

    [Test]
    public void ARealmAboveItsTargetCannotMaskAnotherRealmsDeficit()
    {
        var targets = new Dictionary<eRealm, int> { [eRealm.Albion] = 600, [eRealm.Midgard] = 600, [eRealm.Hibernia] = 600 };
        var load = new Dictionary<eRealm, int> { [eRealm.Albion] = 900, [eRealm.Midgard] = 300, [eRealm.Hibernia] = 600 };

        Assert.That(AutonomousRealmLoginBalancer.Missing(targets, load), Is.EqualTo(300),
            "the total is 1800 = 1800, but Midgard still lacks 300");
        Assert.That(AutonomousRealmLoginBalancer.SelectRealm(targets, load, Realms), Is.EqualTo(eRealm.Midgard));
        Assert.That(AutonomousRealmLoginBalancer.Missing(targets, new Dictionary<eRealm, int>()), Is.EqualTo(1800));
        Assert.That(AutonomousRealmLoginBalancer.Missing(null, load), Is.Zero);
    }

    [Test]
    public void EachRealmRampsToItsOwnTarget()
    {
        var targets = new Dictionary<eRealm, int> { [eRealm.Albion] = 600, [eRealm.Midgard] = 300, [eRealm.Hibernia] = 0 };

        Dictionary<eRealm, int> early = AutonomousRealmLoginBalancer.RampedTargets(targets, true, 15, TimeSpan.FromMinutes(5));
        Dictionary<eRealm, int> done = AutonomousRealmLoginBalancer.RampedTargets(targets, true, 15, TimeSpan.FromMinutes(15));
        Dictionary<eRealm, int> off = AutonomousRealmLoginBalancer.RampedTargets(targets, false, 15, TimeSpan.FromMinutes(15));

        Assert.That(early, Is.EquivalentTo(new Dictionary<eRealm, int> { [eRealm.Albion] = 198, [eRealm.Midgard] = 99, [eRealm.Hibernia] = 0 }),
            "33 % after five minutes");
        Assert.That(done, Is.EquivalentTo(targets));
        Assert.That(off.Values, Is.All.EqualTo(0), "population disabled");
    }

    [Test]
    public void TheDefaultRealmTargetIsSixHundred()
    {
        Assert.That(AutonomousPopulationController.DefaultRealmActiveTarget, Is.EqualTo(600));
        object attribute = typeof(AutonomousPopulationProperties).GetField(nameof(AutonomousPopulationProperties.RVR_REALM_ACTIVE_TARGET))!
            .GetCustomAttributes(typeof(DOL.GS.ServerProperties.ServerPropertyAttribute), false).Single();
        var property = (DOL.GS.ServerProperties.ServerPropertyAttribute)attribute;
        Assert.That(property.Key, Is.EqualTo("rvr_realm_active_target"));
        Assert.That(property.DefaultValue, Is.EqualTo(600));
    }

    // --- Grudges and banter ------------------------------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void GrudgesBelongToTheGuildInCamlannAndToTheRealmInRvr(WorldModelKind world)
    {
        string first = AutonomousGuildGrudgePolicy.OwnerKey(world, "guild-a", eRealm.Midgard);
        string second = AutonomousGuildGrudgePolicy.OwnerKey(world, "guild-b", eRealm.Midgard);
        string otherRealm = AutonomousGuildGrudgePolicy.OwnerKey(world, "guild-a", eRealm.Hibernia);

        Assert.That(first, Is.EqualTo(BothWorlds.Expect(world, "guild-a", "realm:2")));
        Assert.That(first == second, Is.EqualTo(BothWorlds.Expect(world, false, true)),
            "RvR: two guilds of one realm share the realm's grudges");
        Assert.That(first == otherRealm, Is.EqualTo(BothWorlds.Expect(world, true, false)));
        Assert.That(AutonomousGuildGrudgePolicy.OwnerKey(world, null, eRealm.Albion),
            Is.EqualTo(BothWorlds.Expect<string>(world, null, "realm:1")), "RvR: a guildless bot still holds its realm's grudges");
        Assert.That(AutonomousGuildGrudgePolicy.OwnerKey(world, "guild-a", eRealm.None),
            Is.EqualTo(BothWorlds.Expect<string>(world, "guild-a", null)));
        Assert.That(AutonomousGuildGrudgePolicy.MaximumTargets(world), Is.EqualTo(BothWorlds.Expect(world, 16, 48)));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TheKosCallNamesACrewOnlyInCamlann(WorldModelKind world)
    {
        string line = AutonomousBotChatCoordinator.GuildKosLine(world, "Ked", "Emain Macha");

        if (world == WorldModelKind.Camlann)
            Assert.That(line, Is.EqualTo("KOS Ked, last seen in Emain Macha. Watch for the crew."), "Camlann text unchanged");
        else
        {
            Assert.That(line, Does.StartWith("KOS Ked, last seen in Emain Macha."));
            Assert.That(line, Does.Not.Contain("crew").IgnoreCase);
        }
    }
}
