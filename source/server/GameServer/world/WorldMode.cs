using System;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// The world of the running process, read once from the save's marker before
/// the server rules are created (GameServer.ValidateWorldModel). Rules, bots,
/// keeps and launcher-facing code ask this helper instead of a server
/// property: the world belongs to the save and never changes at run time.
/// Before startup (tools, unit tests) the world is Camlann, so code that never
/// sets it keeps the Camlann behaviour.
/// </summary>
public static class WorldMode
{
    private static int _current = (int)WorldModelKind.Camlann;
    private static int _initialized;

    public static WorldModelKind Current => (WorldModelKind)Volatile.Read(ref _current);

    public static bool IsRvr => Current == WorldModelKind.Realm;

    public static bool IsCamlann => Current == WorldModelKind.Camlann;

    /// <summary>
    /// Fixes the world for this process. A second call with the same world is
    /// harmless; a different world or an unsupported value throws, because a
    /// running server never switches worlds.
    /// </summary>
    public static void Initialize(WorldModelKind world)
    {
        if (world == WorldModelKind.Unsupported)
            throw new ArgumentOutOfRangeException(nameof(world), "An unsupported world model cannot be started.");

        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 1)
        {
            if (Current != world)
                throw new InvalidOperationException($"The world is already {Current}; it cannot change to {world} at run time.");
            return;
        }

        Volatile.Write(ref _current, (int)world);
    }

    /// <summary>
    /// The PvP client trick that shows allied NPC-backed actors and pets in the
    /// viewer's (or a dummy) guild so TAB skips them. Camlann needs it because
    /// every player shows red; the RvR world shows realms with colour handling 0
    /// and the real realm byte, as upstream, so the trick is off there.
    /// </summary>
    public static bool UsesGuildIdMask(EGameServerType serverType) =>
        UsesGuildIdMask(Current, serverType);

    public static bool UsesGuildIdMask(WorldModelKind world, EGameServerType serverType) =>
        serverType == EGameServerType.GST_PvP && world != WorldModelKind.Realm;

    /// <summary>
    /// Whether two livings may group, share a guild, guild alliance or
    /// battlegroup, or trade in the current world. Camlann allows every pair;
    /// the RvR world requires the same realm. Staff accounts (privilege above
    /// player) are exempt, as the upstream commands allow.
    /// </summary>
    public static bool AllowsRealmPair(GameLiving source, GameLiving target) =>
        AllowsRealmPair(Current, source, target);

    public static bool AllowsRealmPair(WorldModelKind world, GameLiving source, GameLiving target)
    {
        if (source == null || target == null)
            return false;
        if (world != WorldModelKind.Realm)
            return true;
        if (IsStaff(source) || IsStaff(target))
            return true;
        return source.Realm == target.Realm;
    }

    private static bool IsStaff(GameLiving living) =>
        living is GamePlayer player && player.Client?.Account?.PrivLevel > (uint)ePrivLevel.Player;

    /// <summary>
    /// Test-only: run a block under the given world and restore the previous
    /// world (and the startup latch) afterwards. Never call from server code.
    /// </summary>
    public static IDisposable OverrideForTests(WorldModelKind world)
    {
        var scope = new TestScope(Volatile.Read(ref _current), Volatile.Read(ref _initialized));
        Volatile.Write(ref _current, (int)world);
        Volatile.Write(ref _initialized, 0);
        return scope;
    }

    private sealed class TestScope : IDisposable
    {
        private readonly int _previousWorld;
        private readonly int _previousInitialized;
        private bool _disposed;

        public TestScope(int previousWorld, int previousInitialized)
        {
            _previousWorld = previousWorld;
            _previousInitialized = previousInitialized;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Volatile.Write(ref _current, _previousWorld);
            Volatile.Write(ref _initialized, _previousInitialized);
        }
    }
}
