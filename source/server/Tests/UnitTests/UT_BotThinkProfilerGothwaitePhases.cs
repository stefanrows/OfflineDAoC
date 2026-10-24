using System;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// The three Gothwaite PvP phases of stage 4 in <see cref="BotThinkPhase"/> (docs/GOTHWAITE_PVP_MODE.md section 8): they are appended after
/// the existing phases, so no existing index moves, and they show in the minute profile by name when they were measured.
/// </summary>
[TestFixture]
public class UT_BotThinkProfilerGothwaitePhases
{
    [Test]
    public void TheGothwaitePhasesAreAppendedAfterTheExistingOnes()
    {
        Assert.Multiple(() =>
        {
            Assert.That((int)BotThinkPhase.StableRouteSearchSlice, Is.EqualTo(33), "the existing phases keep their indices");
            Assert.That((int)BotThinkPhase.GothwaitePvpDriver, Is.EqualTo((int)BotThinkPhase.StableRouteSearchSlice + 1));
            Assert.That((int)BotThinkPhase.GothwaitePvpScan, Is.EqualTo((int)BotThinkPhase.StableRouteSearchSlice + 2));
            Assert.That((int)BotThinkPhase.GothwaitePvpFormer, Is.EqualTo((int)BotThinkPhase.StableRouteSearchSlice + 3));
            // Task 138 appended the formation-follow tick after them.
            Assert.That((int)BotThinkPhase.FormationFollowTick, Is.EqualTo((int)BotThinkPhase.GothwaitePvpFormer + 1));
            Assert.That(Enum.GetValues<BotThinkPhase>().Last(), Is.EqualTo(BotThinkPhase.FormationFollowTick));
        });
    }

    [Test]
    public void TheGothwaitePhasesShowInTheMinuteProfileByName()
    {
        BotThinkProfiler.Swap(0);
        using (BotThinkProfiler.Measure(BotThinkPhase.GothwaitePvpDriver)) { }
        using (BotThinkProfiler.Measure(BotThinkPhase.GothwaitePvpScan)) { }
        using (BotThinkProfiler.Measure(BotThinkPhase.GothwaitePvpFormer)) { }

        string line = BotThinkProfiler.Describe(BotThinkProfiler.Swap(60_000), 60_000).First();

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("GothwaitePvpDriver=").And.Contain("GothwaitePvpScan=").And.Contain("GothwaitePvpFormer="));
        });
    }
}
