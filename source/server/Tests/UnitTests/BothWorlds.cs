using System;
using System.Collections.Generic;
using DOL.GS;
using DOL.GS.ServerRules;

namespace DOL.UnitTests;

/// <summary>
/// Pattern for tests that must hold in both worlds (RvR mode, spec section 9.2).
/// Parametrise a test over <see cref="All"/> and wrap its body in
/// <see cref="Use"/>; build the rules with <see cref="Rules"/> so the test runs
/// against the class the server would create for that world:
/// <code>
/// [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
/// public void Something(WorldModelKind world)
/// {
///     using var _ = BothWorlds.Use(world);
///     IServerRules rules = BothWorlds.Rules(world);
///     ...
/// }
/// </code>
/// Fixtures that use it should be NonParallelizable: the world is process-wide.
/// </summary>
internal static class BothWorlds
{
    public static IEnumerable<WorldModelKind> All
    {
        get
        {
            yield return WorldModelKind.Camlann;
            yield return WorldModelKind.Realm;
        }
    }

    /// <summary>Sets the process world for the block and restores it on dispose.</summary>
    public static IDisposable Use(WorldModelKind world) => WorldMode.OverrideForTests(world);

    /// <summary>The rules class the server creates for this world (GameType=PvP).</summary>
    public static IServerRules Rules(WorldModelKind world) =>
        ScriptMgr.CreateServerRules(EGameServerType.GST_PvP, world);

    /// <summary>Picks the expected answer for the world under test.</summary>
    public static T Expect<T>(WorldModelKind world, T camlann, T realm) =>
        world == WorldModelKind.Realm ? realm : camlann;
}
