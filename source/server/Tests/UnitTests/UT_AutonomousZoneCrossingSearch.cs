using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public class UT_AutonomousZoneCrossingSearch
{
    private const ushort World = 1;
    private const ushort Dungeon = 22;

    private static readonly DbZonePoint EntranceA = Edge(World, Dungeon, 10_000, 10_000, 1_000, 1_000);
    private static readonly DbZonePoint EntranceB = Edge(World, Dungeon, 60_000, 60_000, 50_000, 50_000);

    [Test]
    public void CellsBehindDifferentEntrancesResolveTheirOwnCrossing()
    {
        DbZonePoint[] edges = [EntranceA, EntranceB];

        // Cell near B that the catalog admits only through A (the far entrance),
        // and a cell admitted only through B. Planned in either order, each
        // cell gets its own entrance; nothing from the first cell leaks over.
        DbZonePoint onlyA = AutonomousZoneCrossingSearch.FindFirstCrossing(edges, World, Dungeon, 49_000, 49_000,
            point => point == EntranceA);
        DbZonePoint onlyB = AutonomousZoneCrossingSearch.FindFirstCrossing(edges, World, Dungeon, 2_000, 2_000,
            point => point == EntranceB);
        DbZonePoint unrestricted = AutonomousZoneCrossingSearch.FindFirstCrossing(edges, World, Dungeon, 49_000, 49_000,
            null);

        Assert.Multiple(() =>
        {
            Assert.That(onlyA, Is.SameAs(EntranceA));
            Assert.That(onlyB, Is.SameAs(EntranceB));
            Assert.That(unrestricted, Is.SameAs(EntranceB), "Nearest entrance to the goal when unrestricted");
        });
    }

    [Test]
    public void AnUnreachableCellDoesNotHideTheOthers()
    {
        DbZonePoint[] edges = [EntranceA, EntranceB];
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousZoneCrossingSearch.FindFirstCrossing(edges, World, Dungeon, 5_000, 5_000,
                _ => false), Is.Null);
            Assert.That(AutonomousZoneCrossingSearch.FindFirstCrossing(edges, World, Dungeon, 5_000, 5_000,
                point => point == EntranceA), Is.SameAs(EntranceA));
        });
    }

    [Test]
    public void MultiHopRouteReturnsTheFirstCrossing()
    {
        DbZonePoint toHub = Edge(World, 2, 100, 100, 200, 200);
        DbZonePoint hubToDungeon = Edge(2, Dungeon, 300, 300, 1_000, 1_000);
        Assert.That(AutonomousZoneCrossingSearch.FindFirstCrossing([hubToDungeon, toHub], World, Dungeon,
            1_000, 1_000, null), Is.SameAs(toHub));
    }

    [Test]
    public void QuarantinedDirectEdgeIsSkipped()
    {
        DbZonePoint[] edges = [EntranceA, EntranceB];
        Assert.That(AutonomousZoneCrossingSearch.FindFirstCrossing(edges, World, Dungeon, 1_000, 1_000, null,
            point => point == EntranceA, (10_000, 10_000)), Is.SameAs(EntranceB));
    }

    private static DbZonePoint Edge(ushort source, ushort target, int sourceX, int sourceY, int targetX, int targetY) =>
        new()
        {
            SourceRegion = source, TargetRegion = target,
            SourceX = sourceX, SourceY = sourceY, TargetX = targetX, TargetY = targetY,
        };
}
