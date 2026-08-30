using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DragonboatGame.Tests;

public class NavMeshTests
{
    private static NavMesh Build(TerrainMap map, int cellSize = 1, int clearance = 0, bool offMapIsLand = false) =>
        NavMeshBuilder.Build(map, cellSize, clearance, offMapIsLand);

    // Every navigable cell is covered by exactly one node, and every node is
    // a power-of-two square aligned to its own size. Asserted on every
    // fixture below through this one helper.
    private static void AssertPartitions(NavMesh nav)
    {
        var covered = new HashSet<(int, int)>();
        foreach (var n in nav.Nodes)
        {
            Assert.True(n.Size > 0 && (n.Size & (n.Size - 1)) == 0, $"node size {n.Size} is not a power of two");
            Assert.True(n.X % n.Size == 0 && n.Y % n.Size == 0, $"node ({n.X},{n.Y}) is not aligned to its size {n.Size}");
            for (int y = n.Y; y < n.Y + n.Size; y++)
                for (int x = n.X; x < n.X + n.Size; x++)
                    Assert.True(covered.Add((x, y)), $"cell ({x},{y}) is in more than one node");
        }
        Assert.Equal(nav.NavigableCells, covered.Count);
        Assert.Equal(nav.NavigableCells, nav.Nodes.Sum(n => n.Size * n.Size));
        foreach (var (x, y) in covered) Assert.True(nav.NodeAt(x, y) >= 0);
    }

    // ---- degenerate worlds -------------------------------------------------

    [Fact]
    public void OpenWaterCollapsesToASingleNode()
    {
        var nav = Build(TerrainFixture.OpenSea(16));
        var node = Assert.Single(nav.Nodes);
        Assert.Equal(16, node.Size);
        Assert.Equal(0, node.X);
        Assert.Equal(1, nav.BodyCount);
        Assert.Empty(nav.NeighboursOf(0).ToArray());
        AssertPartitions(nav);
    }

    [Fact]
    public void AllLandHasNoMesh()
    {
        var nav = Build(TerrainFixture.FromAscii("####", "####", "####", "####"));
        Assert.Empty(nav.Nodes);
        Assert.Equal(0, nav.BodyCount);
        Assert.Equal(0, nav.NavigableCells);
        Assert.Equal(-1, nav.NodeAtWorld(2, 2));
    }

    [Fact]
    public void RejectsACellSizeItCannotTile()
    {
        var sea = TerrainFixture.OpenSea(16);
        Assert.ThrowsAny<Exception>(() => NavMeshBuilder.Build(sea, 3, 0));    // not a power of two
        Assert.ThrowsAny<Exception>(() => NavMeshBuilder.Build(sea, 0, 0));
        Assert.ThrowsAny<Exception>(() => NavMeshBuilder.Build(sea, 32, 0));   // bigger than the map
        Assert.ThrowsAny<Exception>(() => NavMeshBuilder.Build(sea, 1, -1));   // negative clearance
    }

    // ---- clearance ---------------------------------------------------------

    // The whole point of the expanded-block trick: a channel loses exactly
    // `clearance` cells off each bank, no more and no less.
    [Theory]
    [InlineData(4, 0)]   // width == 2K: nothing left
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(7, 3)]
    public void ClearanceErodesExactlyOneBankWidthFromEachSide(int width, int expectedBand)
    {
        var nav = Build(TerrainFixture.Channel(32, width, top: 8), cellSize: 1, clearance: 2);
        Assert.Equal(32 * expectedBand, nav.NavigableCells);
        if (expectedBand == 0) Assert.Empty(nav.Nodes);
        else
        {
            // The surviving band sits centred in the channel.
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    Assert.Equal(y >= 10 && y < 8 + width - 2, nav.NodeAt(x, y) >= 0);
        }
        AssertPartitions(nav);
    }

    [Fact]
    public void NoClearanceKeepsTheWholeChannel()
    {
        var nav = Build(TerrainFixture.Channel(32, 5, top: 8), cellSize: 1, clearance: 0);
        Assert.Equal(32 * 5, nav.NavigableCells);
        AssertPartitions(nav);
    }

    // ---- the alignment cliff (intended behaviour, and why the cell is small)

    // A nav cell is navigable only if ALL of it is, so a channel that does
    // not line up with the nav grid can vanish even though a boat would fit.
    // This is the reason NavConfig.CellSize is 8 and not 16 — see docs/nav.md.
    [Fact]
    public void ChannelMustAlignWithTheNavGrid()
    {
        var aligned = Build(TerrainFixture.Channel(64, 8, top: 8), cellSize: 8, clearance: 0);
        Assert.Equal(8, aligned.NavigableCells);   // one row of 8 nav cells
        Assert.Equal(1, aligned.BodyCount);

        var offset = Build(TerrainFixture.Channel(64, 8, top: 9), cellSize: 8, clearance: 0);
        Assert.Empty(offset.Nodes);
        Assert.Equal(0, offset.NavigableCells);
    }

    // ---- water bodies ------------------------------------------------------

    [Fact]
    public void LandSplitsTheWaterIntoSeparateBodies()
    {
        var nav = Build(TerrainFixture.FromAscii(
            "..##..",
            "..##..",
            "..##..",
            "..##..",
            "..##..",
            "..##.."));
        Assert.Equal(2, nav.BodyCount);
        Assert.False(nav.SameBody(0.5f, 0.5f, 4.5f, 0.5f));
        Assert.True(nav.SameBody(0.5f, 0.5f, 1.5f, 5.5f));
        Assert.Equal(nav.NavigableCells, nav.BodySize(0) + nav.BodySize(1));
        AssertPartitions(nav);
    }

    // 4-connected, not 8: a boat cannot slip through a diagonal gap.
    [Fact]
    public void PoolsTouchingOnlyAtACornerAreDifferentBodies()
    {
        var nav = Build(TerrainFixture.FromAscii(
            "..##",
            "..##",
            "##..",
            "##.."));
        Assert.Equal(2, nav.BodyCount);
        Assert.False(nav.SameBody(0.5f, 0.5f, 3.5f, 3.5f));
    }

    // No node straddles two bodies, and no edge joins them.
    [Fact]
    public void NodesAndEdgesStayWithinOneBody()
    {
        var nav = Build(TerrainFixture.FromAscii(
            "..##..",
            "..##..",
            "..##..",
            "..##..",
            "..##..",
            "..##.."));
        for (int n = 0; n < nav.Nodes.Count; n++)
            foreach (int other in nav.NeighboursOf(n))
                Assert.Equal(nav.Nodes[n].Body, nav.Nodes[other].Body);
    }

    // ---- adjacency ---------------------------------------------------------

    [Fact]
    public void AdjacencyIsSymmetricWithNoSelfLinksOrDuplicates()
    {
        var nav = Build(TerrainFixture.FromAscii(
            "........",
            "..####..",
            "..####..",
            "........",
            "...##...",
            "........",
            "........",
            "........"));
        for (int n = 0; n < nav.Nodes.Count; n++)
        {
            var mine = nav.NeighboursOf(n).ToArray();
            Assert.Equal(mine.Length, mine.Distinct().Count());
            Assert.DoesNotContain(n, mine);
            foreach (int other in mine) Assert.Contains(n, nav.NeighboursOf(other).ToArray());
        }
    }

    // A big leaf against a run of small ones: its degree is the number of
    // distinct nodes along the edge, counted once each however many cells of
    // the edge they cover.
    [Fact]
    public void ABigNodeCountsEachNeighbourAlongItsEdgeOnce()
    {
        // Left half is open 4x4 water (one leaf); the right column is broken
        // up by land, so several small leaves meet that leaf's right edge.
        var nav = Build(TerrainFixture.FromAscii(
            "....#...",
            "....#...",
            "........",
            "....#...",
            "########",
            "########",
            "########",
            "########"));
        int big = nav.Nodes.Select((n, i) => (n, i)).First(t => t.n.Size == 4).i;
        var neighbours = nav.NeighboursOf(big).ToArray();
        Assert.Equal(neighbours.Length, neighbours.Distinct().Count());
        foreach (int other in neighbours) Assert.NotEqual(big, other);
    }

    [Fact]
    public void CornerTouchingNodesAreNotNeighbours()
    {
        var nav = Build(TerrainFixture.FromAscii(
            "..##",
            "..##",
            "##..",
            "##.."));
        for (int n = 0; n < nav.Nodes.Count; n++)
            foreach (int other in nav.NeighboursOf(n))
                Assert.Equal(nav.Nodes[n].Body, nav.Nodes[other].Body);
        // Nothing in one body links to the other, and each body is separate.
        Assert.Equal(2, nav.BodyCount);
    }

    // ---- lookups -----------------------------------------------------------

    [Fact]
    public void NodeAtRoundTripsAndIsMinusOneOnLand()
    {
        var map = TerrainFixture.FromAscii(
            "....",
            ".##.",
            ".##.",
            "....");
        var nav = Build(map);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                int node = nav.NodeAt(x, y);
                if (map.IsWater(x, y)) Assert.True(nav.Nodes[node].Contains(x, y));
                else Assert.Equal(-1, node);
            }
        Assert.Equal(-1, nav.NodeAt(-1, 0));
        Assert.Equal(-1, nav.NodeAt(0, 4));
    }

    [Fact]
    public void WorldCoordinatesScaleByTheCellSize()
    {
        var nav = Build(TerrainFixture.OpenSea(16), cellSize: 8);
        Assert.Equal(2, nav.CellsX);
        var node = Assert.Single(nav.Nodes);
        Assert.Equal(2, node.Size);
        nav.WorldRect(0, out float x0, out float y0, out float x1, out float y1);
        Assert.Equal(0, x0);
        Assert.Equal(16, x1);
        Assert.Equal(16, y1 - y0);
        nav.Centre(0, out float cx, out float cy);
        Assert.Equal(8, cx);
        Assert.Equal(8, cy);
    }

    [Fact]
    public void NearestNodeFindsAWayOntoTheMeshFromLand()
    {
        var nav = Build(TerrainFixture.FromAscii(
            "........",
            "........",
            "..####..",
            "..####..",
            "..####..",
            "..####..",
            "........",
            "........"));
        // Standing in the middle of the island: no node here, one close by.
        Assert.Equal(-1, nav.NodeAtWorld(4.5f, 4.5f));
        Assert.True(nav.NearestNode(4.5f, 4.5f, 8f) >= 0);
        // ...but not if we refuse to look that far.
        Assert.Equal(-1, nav.NearestNode(4.5f, 4.5f, 0.5f));
        // On the water it is simply the node you are standing on.
        int here = nav.NodeAtWorld(0.5f, 0.5f);
        Assert.Equal(here, nav.NearestNode(0.5f, 0.5f, 0f));
    }

    // ---- off-map policy ----------------------------------------------------

    // TerrainMap answers "water" off the map, which would make the border
    // navigable and invite a pathfinder to sail off the edge of the world.
    [Fact]
    public void OffMapCountsAsLandUnlessAskedOtherwise()
    {
        var sea = TerrainFixture.OpenSea(16);
        var open = NavMeshBuilder.Build(sea, 1, 1, offMapIsLand: false);
        Assert.Equal(16 * 16, open.NavigableCells);
        Assert.Single(open.Nodes);

        var walled = NavMeshBuilder.Build(sea, 1, 1, offMapIsLand: true);
        Assert.Equal(14 * 14, walled.NavigableCells);
        Assert.Equal(-1, walled.NodeAt(0, 0));
        Assert.True(walled.NodeAt(1, 1) >= 0);
        AssertPartitions(walled);
    }

    // ---- ordering against city placement -----------------------------------

    // CityPlacer.Stamp writes City tiles into map.Data, so a city can close a
    // creek: the nav mesh has to be built after the cities are placed.
    [Fact]
    public void CityTilesBlockTheNavMesh()
    {
        var before = Build(TerrainFixture.OpenSea(8));
        var map = TerrainFixture.OpenSea(8);
        for (int y = 2; y < 6; y++)
            for (int x = 2; x < 6; x++)
                map.Data[y * 8 + x] = TerrainMap.City;
        var after = Build(map);
        Assert.Equal(64, before.NavigableCells);
        Assert.Equal(64 - 16, after.NavigableCells);
        Assert.Equal(-1, after.NodeAt(3, 3));
    }

    // ---- determinism -------------------------------------------------------

    [Fact]
    public void BuildingTwiceGivesTheSameMesh()
    {
        var map = TerrainFixture.FromAscii(
            "..#.....",
            "..#..##.",
            ".....##.",
            "###.....",
            "........",
            ".##...#.",
            ".##...#.",
            "........");
        var a = Build(map);
        var b = Build(map);
        Assert.Equal(a.Nodes.Count, b.Nodes.Count);
        for (int i = 0; i < a.Nodes.Count; i++)
        {
            Assert.Equal(a.Nodes[i].X, b.Nodes[i].X);
            Assert.Equal(a.Nodes[i].Y, b.Nodes[i].Y);
            Assert.Equal(a.Nodes[i].Size, b.Nodes[i].Size);
            Assert.Equal(a.Nodes[i].Body, b.Nodes[i].Body);
            Assert.Equal(a.NeighboursOf(i).ToArray(), b.NeighboursOf(i).ToArray());
        }
    }

    // ---- the real bench map ------------------------------------------------

    // The regression that matters: the map the AI is developed on must stay a
    // single connected body at the shipped defaults. If a repaint or a knob
    // change severs the river network, this is what says so.
    [Fact]
    public void ThePathingMapIsOneConnectedBody()
    {
        var map = TerrainMap.Parse(System.IO.File.ReadAllBytes(GameConfigTests.RepoFile("pathingTest.terrain")));
        var nav = NavMeshBuilder.Build(map, NavConfig.CellSize, NavConfig.ClearanceCells);
        Assert.Equal(1, nav.BodyCount);
        Assert.InRange(nav.NavigableCells, 1800, 2600);
        Assert.InRange(nav.Nodes.Count, 600, 1600);
        AssertPartitions(nav);
    }

    // ...and the negative sibling that documents why the cell is 8: at 16 the
    // same rivers fall apart.
    [Fact]
    public void ACoarserCellWouldShatterThePathingMap()
    {
        var map = TerrainMap.Parse(System.IO.File.ReadAllBytes(GameConfigTests.RepoFile("pathingTest.terrain")));
        var nav = NavMeshBuilder.Build(map, 16, NavConfig.ClearanceCells);
        Assert.True(nav.BodyCount > 1, "cell 16 was expected to sever the river network");
    }

    [Fact]
    public void DescribeReportsTheNumbersThatMatter()
    {
        var nav = Build(TerrainFixture.OpenSea(16), cellSize: 8);
        var text = NavMeshBuilder.Describe(nav);
        Assert.Contains("cell 8", text);
        Assert.Contains("1 water body", text);
        Assert.Contains("1 nodes", text);
    }
}
