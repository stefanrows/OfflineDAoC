using System;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_KeepApproachNavigation
{
    private sealed class Mesh : PathfindingMgrBase
    {
        public bool Closed=true;
        public PathfindingStatus Alternative=PathfindingStatus.PartialPathFound;
        public int Alternatives;
        public Vector3? Floor;
        public override bool IsAvailable => true;
        public override bool HasNavmesh(Zone zone) => true;
        public override Vector3? GetClosestPoint(Zone zone, Vector3 point, float x, float y, float z, EDtPolyFlags[] filters) => Floor;
        public override PathfindingResult GetPathStraight(Zone zone,Vector3 start,Vector3 end,EDtPolyFlags[] filters,Span<WrappedPathfindingNode> nodes)
        {
            if((filters[1]&EDtPolyFlags.BlockingDoor)!=0)
            {
                Alternatives++;
                nodes[0]=new(end-new Vector3(20,0,0),EDtPolyFlags.Walk);
                return new(Alternative,1);
            }
            nodes[0]=new(Vector3.Zero,Closed?EDtPolyFlags.BlockingDoor:EDtPolyFlags.Door);
            nodes[1]=new(end,EDtPolyFlags.Walk);
            return new(PathfindingStatus.PathFound,2);
        }
    }
    [Test]
    public void EnemyClosedGateCannotBeValidatedAsAnInteriorApproachEvenTwentyUnitsShort()
    {
        var mesh=new Mesh();var nav=new AutonomousKeepApproachNavigation(mesh,[]);
        var nodes=new WrappedPathfindingNode[8];
        var result=nav.GetPathStraight(null,new(-100,0,0),new(100,0,0),nav.DefaultFilters,nodes);
        Assert.That(result.Status,Is.EqualTo(PathfindingStatus.NoPathFound));
        Assert.That(result.NodeCount,Is.Zero);
    }
    [Test]
    public void OwnGateUsesNativeFriendlyDoorRouteAndAnOpenedGateAllowsEveryone()
    {
        var mesh=new Mesh();var nodes=new WrappedPathfindingNode[8];
        var defender=new AutonomousKeepApproachNavigation(mesh,[Vector3.Zero]);
        Assert.That(defender.GetPathStraight(null,new(-100,0,0),new(100,0,0),defender.DefaultFilters,nodes).Status,Is.EqualTo(PathfindingStatus.PathFound));
        Assert.That(mesh.Alternatives,Is.Zero);
        mesh.Closed=false;
        var attacker=new AutonomousKeepApproachNavigation(mesh,[]);
        Assert.That(attacker.GetPathStraight(null,new(-100,0,0),new(100,0,0),attacker.DefaultFilters,nodes).Status,Is.EqualTo(PathfindingStatus.PathFound));
        Assert.That(mesh.Alternatives,Is.Zero);
    }
    [Test]
    public void CompleteRouteAroundClosedGateIsAllowed()
    {
        var mesh=new Mesh { Alternative=PathfindingStatus.PathFound };
        var nav=new AutonomousKeepApproachNavigation(mesh,[]);
        Assert.That(nav.GetPathStraight(null,new(-100,0,0),new(100,0,0),nav.DefaultFilters,new WrappedPathfindingNode[8]).Status,Is.EqualTo(PathfindingStatus.PathFound));
        Assert.That(mesh.Alternatives,Is.EqualTo(1));
    }
    [TestCase(36, true)]
    [TestCase(64, true)]
    [TestCase(65, false)]
    [TestCase(900, false)]
    public void KeepPlanningAcceptsNativeStartToleranceWithoutMovingTheActor(float downward, bool expected)
    {
        Vector3 actor = new(100, 100, 1000);
        var mesh = new Mesh { Floor = actor - new Vector3(0, 0, downward) };
        var zone = (Zone)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Zone));
        bool accepted = AutonomousKeepApproachNavigation.TryPlanningOrigin(mesh, zone, actor, out var origin);
        Assert.That(accepted, Is.EqualTo(expected));
        Assert.That(origin, Is.EqualTo(expected ? mesh.Floor.Value : actor));
    }

    [Test]
    public void KeepPlanningCannotNormalizeAcrossWallsOrUseInvalidFloors()
    {
        var zone = (Zone)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Zone));
        Vector3 actor = new(100, 100, 1000);
        foreach (var floor in new[] { actor + new Vector3(3, 0, 0), new Vector3(float.NaN, 100, 1000) })
        {
            Assert.That(AutonomousKeepApproachNavigation.TryPlanningOrigin(new Mesh { Floor = floor }, zone, actor, out _), Is.False);
        }
    }

}
