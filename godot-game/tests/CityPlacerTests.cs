using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DragonboatGame.Tests;

public class CityPlacerTests
{
    private static List<string> Names(int n) => Enumerable.Range(0, n).Select(i => $"City{i}").ToList();

    // A big coastline map so 20 cities fit with the real spacing.
    private static TerrainMap BigCoast() => TerrainFixture.Coastline(8192);

    // The 20-city mix the tests place: 2 large, 6 medium, 12 small.
    private static List<string> Mix(int large = 2, int medium = 6, int small = 12) =>
        CityPlacer.SizesFor(large, medium, small);

    // The mix comes from the config, not from a ratio in the code.
    [Fact]
    public void SizesAreExactlyWhatWasAskedFor()
    {
        var sizes = CityPlacer.SizesFor(large: 2, medium: 6, small: 12);
        Assert.Equal(20, sizes.Count);
        Assert.Equal(2, sizes.Count(s => s == "large"));
        Assert.Equal(6, sizes.Count(s => s == "medium"));
        Assert.Equal(12, sizes.Count(s => s == "small"));
        // Large first: the biggest footprint gets the pick of the coast.
        Assert.Equal(new[] { "large", "large", "medium" }, sizes.Take(3));
        // A size can be left out entirely.
        Assert.Equal(new[] { "small", "small" }, CityPlacer.SizesFor(0, 0, 2));
        Assert.Empty(CityPlacer.SizesFor(0, 0, 0));
    }

    [Fact]
    public void SizesComeStraightFromTheGameConfig()
    {
        var config = new GameConfig("map1.terrain", smallCities: 5, mediumCities: 3, largeCities: 1, currentDensity: 0, priceEvents: 0, vipEvents: 0, hostileBoats: 0);
        var sizes = CityPlacer.SizesFor(config);
        Assert.Equal(9, sizes.Count);
        Assert.Equal(1, sizes.Count(s => s == "large"));
        Assert.Equal(3, sizes.Count(s => s == "medium"));
        Assert.Equal(5, sizes.Count(s => s == "small"));
    }

    [Fact]
    public void FootprintSizesAre60_100_140()
    {
        Assert.Equal(60, CityPlacer.CellsFor("small"));
        Assert.Equal(100, CityPlacer.CellsFor("medium"));
        Assert.Equal(140, CityPlacer.CellsFor("large"));
    }

    [Fact]
    public void PlacesTheRequestedNumberOfCitiesWithUniqueNames()
    {
        var cities = CityPlacer.Place(BigCoast(), Mix(), Names(100), new Random(1));
        Assert.Equal(20, cities.Count);
        Assert.Equal(20, cities.Select(c => c.Name).Distinct().Count());
        Assert.All(cities, c => Assert.Contains(c.Name, Names(100)));
    }

    [Fact]
    public void NamesAreDealtWithoutRepeatsEvenWhenThePoolIsShort()
    {
        var names = CityPlacer.DealNames(Names(3), 7, new Random(4));
        Assert.Equal(7, names.Count);
        Assert.Equal(7, names.Distinct().Count());
    }

    [Fact]
    public void CitiesAreStampedAsCityCellsWithTheRightFootprint()
    {
        var map = BigCoast();
        var cities = CityPlacer.Place(map, Mix(), Names(100), new Random(2));
        foreach (var c in cities)
        {
            Assert.Equal(CityPlacer.CellsFor(c.Size), c.Cells);
            int n = c.Cells, x0 = (int)c.X - n / 2, y0 = (int)c.Y - n / 2;
            for (int y = y0; y < y0 + n; y++)
                for (int x = x0; x < x0 + n; x++)
                    Assert.Equal(TerrainMap.City, map.TerrainAt(x, y));
            // Nothing outside the footprint became a city cell for this one.
            Assert.NotEqual(TerrainMap.City, map.TerrainAt(x0 - 1, y0 - 1));
        }
    }

    [Fact]
    public void EveryCityTouchesWater()
    {
        var map = BigCoast();
        foreach (var c in CityPlacer.Place(map, Mix(), Names(100), new Random(3)))
            Assert.True(CityPlacer.TouchesWater(map, c), $"{c.Name} is landlocked");
    }

    [Fact]
    public void CitiesAreSpacedApartAndClearOfTheMapEdge()
    {
        var map = BigCoast();
        var cities = CityPlacer.Place(map, Mix(), Names(100), new Random(5));
        foreach (var c in cities)
        {
            int half = c.Cells / 2, margin = CityPlacer.MapEdgeMargin;
            Assert.InRange(c.X, half + margin, map.Width - half - margin);
            Assert.InRange(c.Y, half + margin, map.Height - half - margin);
            foreach (var other in cities)
            {
                if (ReferenceEquals(c, other)) continue;
                float dx = c.X - other.X, dy = c.Y - other.Y;
                Assert.True(MathF.Sqrt(dx * dx + dy * dy) >= CityPlacer.MinSpacing,
                    $"{c.Name} and {other.Name} are too close");
            }
        }
    }

    // Off-map reads as water, so a map that is land right up to the border
    // has no coast at all — the border must not pass as one.
    [Fact]
    public void TheMapBorderIsNotACoast()
    {
        int size = 1024;
        var tiles = new byte[size * size];
        Array.Fill(tiles, (byte)TerrainMap.Grass);
        var allLand = new TerrainMap(size, size, tiles);
        Assert.Empty(CityPlacer.ShuffledCoast(allLand, new Random(9)));
        Assert.ThrowsAny<Exception>(() => CityPlacer.Place(allLand, Mix(1, 0, 0), Names(10), new Random(9)));
    }

    [Fact]
    public void CitiesStayClearOfTheEdgeOnTheRealMap()
    {
        var map = TerrainMap.Parse(System.IO.File.ReadAllBytes(GameConfigTests.RepoFile("map1.terrain")));
        foreach (var c in CityPlacer.Place(map, Mix(), Names(100), new Random(11)))
        {
            int half = c.Cells / 2, margin = CityPlacer.MapEdgeMargin;
            Assert.InRange(c.X, half + margin, map.Width - half - margin);
            Assert.InRange(c.Y, half + margin, map.Height - half - margin);
        }
    }

    [Fact]
    public void FailsLoudlyWhenTheMapHasNoCoast()
    {
        var allWater = new TerrainMap(256, 256, new byte[256 * 256]);
        Assert.ThrowsAny<Exception>(() => CityPlacer.Place(allWater, Mix(1, 0, 0), Names(10), new Random(6)));
    }

    [Fact]
    public void BoatStartIsOpenWaterOffTheCityShore()
    {
        var map = BigCoast();
        var city = CityPlacer.Place(map, Mix(1, 0, 0), Names(10), new Random(7))[0];
        var start = CityPlacer.WaterStartNear(map, city, clearance: 30);
        Assert.NotNull(start);
        var (x, y, heading) = start!.Value;
        // Open water: the clearance box around the spot is all water.
        for (int j = -30; j <= 30; j++)
            for (int i = -30; i <= 30; i++)
                Assert.True(map.IsWater((int)x + i, (int)y + j));
        // Close to the city — this coast is straight, so within a few hundred cells.
        Assert.True(MathF.Abs(x - city.X) + MathF.Abs(y - city.Y) < city.Cells * 3);
        // Pointing away from the city, out to sea (this coast has water to the west).
        Assert.Equal(MathF.PI, MathF.Abs(heading), 3);
    }

    [Fact]
    public void CityNamesFileHas100UniqueNames()
    {
        var names = CityPlacer.ParseNames(System.IO.File.ReadAllText(GameConfigTests.RepoFile("data/city-names.json")));
        Assert.Equal(100, names.Count);
        Assert.Equal(100, names.Distinct().Count());
        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
    }

    [Fact]
    public void PlacesCitiesOnTheRealMap()
    {
        var map = TerrainMap.Parse(System.IO.File.ReadAllBytes(GameConfigTests.RepoFile("map1.terrain")));
        var cities = CityPlacer.Place(map, Mix(), CityPlacer.ParseNames(
            System.IO.File.ReadAllText(GameConfigTests.RepoFile("data/city-names.json"))), new Random(8));
        Assert.Equal(20, cities.Count);
        Assert.All(cities, c => Assert.True(CityPlacer.TouchesWater(map, c)));
        Assert.Contains(cities, c => c.Size == "large" && CityPlacer.WaterStartNear(map, c, 30) != null);
    }
}
