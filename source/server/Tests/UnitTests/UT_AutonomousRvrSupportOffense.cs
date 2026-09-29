using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Wave 4 of livelier RvR bots (P1 roles are fluid by spec, P8 support
/// jobs before the assist train): a smite Cleric smites only behind a second
/// healer (D7668), cave Shaman and pac Healer put CC before heals (D5086), and
/// the Healer area stun opens for autonomous bomb groups.</summary>
[TestFixture]
public sealed class UT_AutonomousRvrSupportOffense
{
    private const double Fights = 0.9;
    private const double StaysOnHeals = 0.1;

    /// <summary>A healthy fight with a second healer and a called target.</summary>
    private static RvrSupportAction Healthy(RvrSupportStyle style, bool secondHealer = true, int groupMin = 90,
        int power = 80, bool cure = false, bool caller = true, double roll = Fights, bool attacked = false) =>
        AutonomousRvrSupportOffense.Decide(style, secondHealer, groupMin, power, cure, caller, roll, attacked);

    [Test]
    public void StylesFollowTheSpec()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrSupportOffense.StyleOf(eCharacterClass.Cleric, eSpecType.SmiteCleric), Is.EqualTo(RvrSupportStyle.Offensive));
            Assert.That(AutonomousRvrSupportOffense.StyleOf(eCharacterClass.Cleric, eSpecType.RejuvCleric), Is.EqualTo(RvrSupportStyle.HealFirst));
            Assert.That(AutonomousRvrSupportOffense.StyleOf(eCharacterClass.Druid, eSpecType.NatureDruid), Is.EqualTo(RvrSupportStyle.Offensive));
            Assert.That(AutonomousRvrSupportOffense.StyleOf(eCharacterClass.Druid, eSpecType.RegrowthDruid), Is.EqualTo(RvrSupportStyle.HealFirst));
            Assert.That(AutonomousRvrSupportOffense.StyleOf(eCharacterClass.Healer, eSpecType.PacHealer), Is.EqualTo(RvrSupportStyle.ControlFirst));
            Assert.That(AutonomousRvrSupportOffense.StyleOf(eCharacterClass.Healer, eSpecType.MendHealer), Is.EqualTo(RvrSupportStyle.HealFirst));
            Assert.That(AutonomousRvrSupportOffense.StyleOf(eCharacterClass.Shaman, eSpecType.SubtShaman), Is.EqualTo(RvrSupportStyle.ControlFirst));
            Assert.That(AutonomousRvrSupportOffense.StyleOf(eCharacterClass.Friar, eSpecType.StaffFriar), Is.EqualTo(RvrSupportStyle.MeleeHybrid));
            Assert.That(AutonomousRvrSupportOffense.StyleOf(eCharacterClass.Warden, eSpecType.BattleWarden), Is.EqualTo(RvrSupportStyle.MeleeHybrid));
            Assert.That(AutonomousRvrSupportOffense.StyleOf(eCharacterClass.Bard, eSpecType.None), Is.EqualTo(RvrSupportStyle.HealFirst));
            Assert.That(AutonomousRvrSupportOffense.StyleOf(eCharacterClass.Cleric, eSpecType.None), Is.EqualTo(RvrSupportStyle.HealFirst),
                "unknown spec keeps today's behaviour");
        });
    }

    [Test]
    public void HealSpecsNeverLeaveHeals()
    {
        Assert.That(Healthy(RvrSupportStyle.HealFirst, attacked: true), Is.EqualTo(RvrSupportAction.HealOnly));
    }

    [Test]
    public void SmiteNeedsASecondHealer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Healthy(RvrSupportStyle.Offensive), Is.EqualTo(RvrSupportAction.Offense));
            Assert.That(Healthy(RvrSupportStyle.Offensive, secondHealer: false), Is.EqualTo(RvrSupportAction.HealOnly),
                "8v8 with one cleric: no smiting");
        });
    }

    [Test]
    public void OffenseThresholdsAreInclusive()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Healthy(RvrSupportStyle.Offensive, groupMin: 75), Is.EqualTo(RvrSupportAction.Offense));
            Assert.That(Healthy(RvrSupportStyle.Offensive, groupMin: 74), Is.EqualTo(RvrSupportAction.HealOnly));
            Assert.That(Healthy(RvrSupportStyle.Offensive, power: 50), Is.EqualTo(RvrSupportAction.Offense));
            Assert.That(Healthy(RvrSupportStyle.Offensive, power: 49), Is.EqualTo(RvrSupportAction.HealOnly));
            Assert.That(Healthy(RvrSupportStyle.Offensive, caller: false), Is.EqualTo(RvrSupportAction.HealOnly),
                "no called target, nothing to smite");
        });
    }

    [Test]
    public void PerBotHealthGateMovesTheThreshold()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrSupportOffense.Decide(RvrSupportStyle.Offensive, true, 72, 80, false, true, Fights, false, 70),
                Is.EqualTo(RvrSupportAction.Offense));
            Assert.That(AutonomousRvrSupportOffense.Decide(RvrSupportStyle.Offensive, true, 79, 80, false, true, Fights, false, 80),
                Is.EqualTo(RvrSupportAction.HealOnly));
        });
    }

    [Test]
    public void CureComesBeforeOffenseAndControl()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Healthy(RvrSupportStyle.Offensive, cure: true), Is.EqualTo(RvrSupportAction.HealOnly));
            Assert.That(Healthy(RvrSupportStyle.ControlFirst, cure: true, attacked: true), Is.EqualTo(RvrSupportAction.HealOnly));
            Assert.That(Healthy(RvrSupportStyle.MeleeHybrid, cure: true), Is.EqualTo(RvrSupportAction.HealOnly));
        });
    }

    [Test]
    public void ThirtyPercentOfFightsStayOnHeals()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Healthy(RvrSupportStyle.Offensive, roll: StaysOnHeals), Is.EqualTo(RvrSupportAction.HealOnly));
            Assert.That(Healthy(RvrSupportStyle.Offensive, roll: 0.30), Is.EqualTo(RvrSupportAction.Offense), "boundary");
            Assert.That(Healthy(RvrSupportStyle.ControlFirst, roll: StaysOnHeals, attacked: true), Is.EqualTo(RvrSupportAction.Control),
                "the roll does not hold back CC");
        });
    }

    [Test]
    public void ControlSpecsCcBeforeHealsWithoutSecondHealer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Healthy(RvrSupportStyle.ControlFirst, secondHealer: false, groupMin: 55, power: 20, caller: false, attacked: true),
                Is.EqualTo(RvrSupportAction.Control), "Healing is nice, but CC is better");
            Assert.That(Healthy(RvrSupportStyle.ControlFirst, groupMin: 40, attacked: true), Is.EqualTo(RvrSupportAction.Control));
            Assert.That(Healthy(RvrSupportStyle.ControlFirst, groupMin: 39, attacked: true), Is.EqualTo(RvrSupportAction.HealOnly),
                "a mate below 40 % gets the heal first");
            Assert.That(Healthy(RvrSupportStyle.ControlFirst, secondHealer: false, attacked: false), Is.EqualTo(RvrSupportAction.HealOnly),
                "nobody to control and no second healer");
            Assert.That(Healthy(RvrSupportStyle.ControlFirst, attacked: false), Is.EqualTo(RvrSupportAction.Offense),
                "nobody to control: offense on the ordinary gate");
            Assert.That(Healthy(RvrSupportStyle.Offensive, attacked: true, secondHealer: false), Is.EqualTo(RvrSupportAction.HealOnly),
                "only the control specs use Control");
        });
    }

    [Test]
    public void MeleeHybridsFightWhileTheGroupIsHealthy()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Healthy(RvrSupportStyle.MeleeHybrid, secondHealer: false, power: 0, caller: false, roll: StaysOnHeals),
                Is.EqualTo(RvrSupportAction.Offense));
            Assert.That(Healthy(RvrSupportStyle.MeleeHybrid, groupMin: 70), Is.EqualTo(RvrSupportAction.HealOnly),
                "steps out of the assist train when someone drops");
        });
    }

    [Test]
    public void SmitersDoNotCoverEachOther()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrSupportOffense.CoversHeals(eCharacterClass.Cleric, eSpecType.RejuvCleric), Is.True);
            Assert.That(AutonomousRvrSupportOffense.CoversHeals(eCharacterClass.Shaman, eSpecType.MendShaman), Is.True);
            Assert.That(AutonomousRvrSupportOffense.CoversHeals(eCharacterClass.Shaman, eSpecType.SubtShaman), Is.False, "CC first");
            Assert.That(AutonomousRvrSupportOffense.CoversHeals(eCharacterClass.Healer, eSpecType.PacHealer), Is.False, "CC first");
            Assert.That(AutonomousRvrSupportOffense.CoversHeals(eCharacterClass.Friar, eSpecType.StaffFriar), Is.False, "melee line");
            Assert.That(AutonomousRvrSupportOffense.CoversHeals(eCharacterClass.Warden, eSpecType.BattleWarden), Is.False, "melee line");
            Assert.That(AutonomousRvrSupportOffense.CoversHeals(eCharacterClass.Cleric, eSpecType.SmiteCleric), Is.False);
            Assert.That(AutonomousRvrSupportOffense.CoversHeals(eCharacterClass.Druid, eSpecType.NatureDruid), Is.False);
            Assert.That(AutonomousRvrSupportOffense.CoversHeals(eCharacterClass.Paladin, eSpecType.None), Is.False);
            Assert.That(AutonomousRvrSupportOffense.CoversHeals(eCharacterClass.Bard, eSpecType.None), Is.False);
        });
    }

    [Test]
    public void HealerAreaStunOpensOnlyForBombGroups()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrSupportOffense.AllowsHealerAreaStun(true, true, null), Is.True, "player-led as before");
            Assert.That(AutonomousRvrSupportOffense.AllowsHealerAreaStun(true, false, RvrDoctrineKind.BombGroup), Is.True);
            Assert.That(AutonomousRvrSupportOffense.AllowsHealerAreaStun(true, false, RvrDoctrineKind.AssistTrain), Is.False);
            Assert.That(AutonomousRvrSupportOffense.AllowsHealerAreaStun(true, false, null), Is.False, "not RvR or not autonomous");
            Assert.That(AutonomousRvrSupportOffense.AllowsHealerAreaStun(false, true, RvrDoctrineKind.BombGroup), Is.False, "Healers only");
        });
    }

    [Test]
    public void CounterLineIsPerWindowAndNamesOnlyActiveClasses()
    {
        RvrSupportCounters counters = new();
        Assert.That(counters.Flush(1_000), Is.Empty, "first call opens the window");
        counters.Record(eCharacterClass.Cleric, RvrSupportAction.Offense);
        counters.Record(eCharacterClass.Cleric, RvrSupportAction.Offense);
        counters.Record(eCharacterClass.Healer, RvrSupportAction.Control);
        counters.Record(eCharacterClass.Druid, RvrSupportAction.HealOnly);
        counters.Record(eCharacterClass.Bard, RvrSupportAction.HealOnly);
        counters.RecordAreaStun();
        Assert.That(counters.Due(200_000), Is.False);
        Assert.That(counters.Flush(200_000), Is.Empty, "window still open");
        Assert.That(counters.Due(301_000), Is.True);
        Assert.That(counters.Flush(301_000), Is.EqualTo(new[]
        {
            "RVR_SUPPORT_OFFENSE window_s=300 offense=2 control=1 heal_only=2 by_class=Cleric:2/0,Healer:0/1",
            "RVR_HEALER_AREA_STUN window_s=300 casts=1",
        }));
        Assert.That(counters.Flush(700_000), Is.Empty, "nothing happened in the next window");
    }
}
