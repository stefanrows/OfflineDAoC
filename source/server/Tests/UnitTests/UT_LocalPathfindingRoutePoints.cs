using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{

    [TestFixture]
    public class UT_LocalPathfindingRoutePoints
    {
        [Test]
        public void MergeCoincidentNodesHandlesEmptyAndSinglePointRoutes()
        {
            WrappedPathfindingNode[] nodes = new WrappedPathfindingNode[1];

            Assert.That(LocalPathfindingMgr.MergeCoincidentNodes(nodes, 0), Is.Zero);

            nodes[0] = new(Vector3.One, EDtPolyFlags.Walk);
            Assert.That(LocalPathfindingMgr.MergeCoincidentNodes(nodes, 1), Is.EqualTo(1));
            Assert.That(nodes[0], Is.EqualTo(new WrappedPathfindingNode(Vector3.One, EDtPolyFlags.Walk)));
        }

        [Test]
        public void MergeCoincidentNodesCombinesFlagsAndKeepsFinalEndpoint()
        {
            WrappedPathfindingNode[] nodes =
            [
                new(Vector3.Zero, EDtPolyFlags.Walk),
                new(new(0.25f, 0, 0), EDtPolyFlags.Door),
                new(new(0.5f, 0, 0), EDtPolyFlags.BlockingDoor),
                new(new(0.5f, 0, 0), EDtPolyFlags.Swim)
            ];

            int count = LocalPathfindingMgr.MergeCoincidentNodes(nodes, nodes.Length);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(nodes[0], Is.EqualTo(new WrappedPathfindingNode(
                Vector3.Zero, EDtPolyFlags.Walk | EDtPolyFlags.Door | EDtPolyFlags.BlockingDoor)));
            Assert.That(nodes[1], Is.EqualTo(new WrappedPathfindingNode(new(0.5f, 0, 0), EDtPolyFlags.Swim)));
        }
    }

}
