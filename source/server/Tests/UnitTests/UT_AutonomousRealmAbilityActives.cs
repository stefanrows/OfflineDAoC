using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS.RealmAbilities;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_AutonomousRealmAbilityActives
    {
        [Test]
        public void SavedCooldownRoundTripRetainsExactGameplayDeadlineAndUnknownExtensions()
        {
            long deadline = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc).Ticks;
            string token = "ra-cooldown|" + AutonomousRealmAbilityActives.Purge + "|" +
                deadline.ToString(CultureInfo.InvariantCulture);
            var original = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            AutonomousRealmAbilityActives.Restore(original, token + ";future-extension|keep", true);
            string saved = AutonomousRealmAbilityTraining.Serialize(50,
                new System.Collections.Generic.Dictionary<string, int>
                {
                    [AutonomousRealmAbilityActives.Purge] = 1,
                }, AutonomousRealmAbilityActives.SerializeCooldownTokens(original));

            var reloaded = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            AutonomousRealmAbilityActives.Restore(reloaded, saved, true);
            string restored = AutonomousRealmAbilityActives.SerializeCooldownTokens(reloaded);

            Assert.That(restored, Is.EqualTo(token + ";future-extension|keep"));
            Assert.That(AutonomousRealmAbilityActives.TryReadCooldownTokens(restored,
                out var deadlines, out _, out bool valid), Is.True);
            Assert.That(valid, Is.True);
            Assert.That(deadlines[AutonomousRealmAbilityActives.Purge], Is.EqualTo(deadline));
        }

        [TestCase("ra-cooldown|AtlasOF_Purge|not-a-deadline")]
        [TestCase("ra-cooldown|AtlasOF_FutureAbility|123")]
        [TestCase("ra-cooldown|AtlasOF_Purge|638712864000000000;ra-cooldown|AtlasOF_Purge|638712864000000001")]
        public void InvalidCooldownStateIsPreservedVerbatimThroughReload(string tokens)
        {
            var original = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            AutonomousRealmAbilityActives.Restore(original, tokens + ";future-extension|keep", true);
            string saved = AutonomousRealmAbilityActives.SerializeCooldownTokens(original);
            var reloaded = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            AutonomousRealmAbilityActives.Restore(reloaded, saved, true);

            Assert.That(saved, Is.EqualTo(tokens + ";future-extension|keep"));
            Assert.That(AutonomousRealmAbilityActives.SerializeCooldownTokens(reloaded), Is.EqualTo(saved));
            Assert.That(AutonomousRealmAbilityActives.TryReadCooldownTokens(saved,
                out _, out _, out bool valid), Is.False);
            Assert.That(valid, Is.False);
        }

        [Test]
        public void Eligibility_ExcludesCompanionsPlayerLedGroupsAndStableTravel()
        {
            Assert.That(AutonomousRealmAbilityActives.IsEligible(true, false, false, false), Is.True);
            Assert.That(AutonomousRealmAbilityActives.IsEligible(false, false, false, false), Is.False);
            Assert.That(AutonomousRealmAbilityActives.IsEligible(true, true, false, false), Is.False);
            Assert.That(AutonomousRealmAbilityActives.IsEligible(true, false, true, false), Is.False);
            Assert.That(AutonomousRealmAbilityActives.IsEligible(true, false, false, true), Is.False);
        }

        [Test]
        public void Activation_UsesConservativeThresholdsAndAllowsPurgeWhileCrowdControlled()
        {
            Assert.That(AutonomousRealmAbilityActives.ShouldUse(
                AutonomousRealmAbilityActives.Purge, true, false, true, true, true, 100, 100), Is.True);
            Assert.That(AutonomousRealmAbilityActives.ShouldUse(
                AutonomousRealmAbilityActives.PurgeReduced, true, false, true, false, false, 60, 100), Is.True);
            Assert.That(AutonomousRealmAbilityActives.ShouldUse(
                AutonomousRealmAbilityActives.Purge, true, false, false, true, true, 20, 100), Is.False);
            Assert.That(AutonomousRealmAbilityActives.ShouldUse(
                AutonomousRealmAbilityActives.IgnorePainTank, true, false, false, true, true, 30, 100), Is.True);
            Assert.That(AutonomousRealmAbilityActives.ShouldUse(
                AutonomousRealmAbilityActives.SecondWind, true, false, false, true, true, 100, 30), Is.True);
            Assert.That(AutonomousRealmAbilityActives.ShouldUse(
                AutonomousRealmAbilityActives.FirstAid, true, false, false, false, false, 30, 100), Is.True);
            Assert.That(AutonomousRealmAbilityActives.ShouldUse(
                AutonomousRealmAbilityActives.FirstAid, true, false, false, true, true, 20, 100), Is.False);
            Assert.That(AutonomousRealmAbilityActives.ShouldUse(
                AutonomousRealmAbilityActives.FirstAid, true, true, false, false, false, 20, 100), Is.False);
        }

        [Test]
        public void CooldownReader_ParsesKnownDeadlineAndFailsClosedOnUnknownToken()
        {
            long ticks = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc).Ticks;
            string known = "ra-cooldown|" + AutonomousRealmAbilityActives.Purge + "|" +
                ticks.ToString(CultureInfo.InvariantCulture);
            string unknown = "ra-cooldown|AtlasOF_FutureAbility|123";

            bool valid = AutonomousRealmAbilityActives.TryReadCooldownTokens(
                "trained-level|50;" + known + ";" + unknown,
                out var deadlines, out string[] preserved, out bool reportedValid);

            Assert.That(valid, Is.False);
            Assert.That(reportedValid, Is.False);
            Assert.That(deadlines[AutonomousRealmAbilityActives.Purge], Is.EqualTo(ticks));
            Assert.That(preserved, Is.EqualTo(new[] { known, unknown }));
        }

        [Test]
        public void CooldownReader_RejectsMalformedAndDuplicateEntriesWithoutDroppingThem()
        {
            string malformed = "ra-cooldown|AtlasOF_Purge|not-a-deadline";
            string duplicate = "ra-cooldown|AtlasOF_Purge|638712864000000000";
            bool valid = AutonomousRealmAbilityActives.TryReadCooldownTokens(
                malformed + ";" + duplicate,
                out _, out string[] preserved, out bool reportedValid);

            Assert.That(valid, Is.False);
            Assert.That(reportedValid, Is.False);
            Assert.That(preserved, Is.EqualTo(new[] { malformed, duplicate }));
        }

        [Test]
        public void SavedAllocations_RequireActivePrerequisitesButAllowPassivePlansWithoutThem()
        {
            Assert.That(AutonomousRealmAbilityActives.HasValidSavedPrerequisites(
                new System.Collections.Generic.Dictionary<string, int>
                {
                    [AutonomousRealmAbilityActives.IgnorePainTank] = 1,
                    [AutonomousRealmAbilityActives.FirstAid] = 1,
                }), Is.False);
            Assert.That(AutonomousRealmAbilityActives.HasValidSavedPrerequisites(
                new System.Collections.Generic.Dictionary<string, int>
                {
                    [AutonomousRealmAbilityActives.IgnorePain] = 1,
                    [AutonomousRealmAbilityActives.FirstAid] = 2,
                }), Is.True);
            Assert.That(AutonomousRealmAbilityActives.HasValidSavedPrerequisites(
                new System.Collections.Generic.Dictionary<string, int>
                {
                    [AutonomousRealmAbilityActives.SecondWind] = 1,
                    [AutonomousRealmAbilityActives.AugmentedConstitution] = 3,
                }), Is.True);
            Assert.That(AutonomousRealmAbilityActives.HasValidSavedPrerequisites(
                new System.Collections.Generic.Dictionary<string, int>
                {
                    [AutonomousRealmAbilityActives.SecondWind] = 1,
                    [AutonomousRealmAbilityActives.AugmentedConstitution] = 2,
                }), Is.False);
            Assert.That(AutonomousRealmAbilityActives.HasValidSavedPrerequisites(
                new System.Collections.Generic.Dictionary<string, int>
                {
                    ["MasteryOfPain"] = 4,
                }), Is.True);
        }

        [Test]
        public void PurchaseKeys_UseRuntimeVariantTypeAndPersistCatalogKeys()
        {
            var reducedPurge = new AtlasOF_PurgeAbilityReduced(
                new DbAbility { KeyName = AutonomousRealmAbilityActives.Purge }, 0);
            var tankIgnorePain = new AtlasOF_IgnorePainTank(
                new DbAbility { KeyName = AutonomousRealmAbilityActives.IgnorePain }, 0);

            var keys = AutonomousRealmAbilityActives.PurchaseKeys(new RealmAbility[]
            {
                reducedPurge,
                tankIgnorePain,
            });

            Assert.That(reducedPurge.CostForUpgrade(0), Is.EqualTo(4));
            Assert.That(tankIgnorePain.CostForUpgrade(0), Is.EqualTo(8));
            Assert.That(keys, Does.Contain(AutonomousRealmAbilityActives.Purge));
            Assert.That(keys, Does.Contain(AutonomousRealmAbilityActives.IgnorePain));
            Assert.That(keys, Does.Not.Contain(AutonomousRealmAbilityActives.PurgeReduced));
            Assert.That(keys, Does.Not.Contain(AutonomousRealmAbilityActives.IgnorePainTank));
        }
    }
}
