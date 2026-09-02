using System;
using Xunit;

namespace DragonboatGame.Tests;

// boats.yml: the fleet the whole game is built out of (src/boat/BoatClass.cs).
public class BoatClassTests
{
    private const string Two = @"
- name: small
  hullLength: 8
  hullWidth: 3
  sails: 1
  crew: 1
  cargo: 200
  cost: 1000
  sailThrust: 0.8
  oarThrust: 0.85
  turn: 1.15
  gunsBow: 1
  gunsStern: 0
  gunsPerSide: 0

- name: medium   # a comment, and a blank line above
  hullLength: 12
  hullWidth: 4
  sails: 2
  crew: 10
  cargo: 1200
  cost: 10000
  sailThrust: 1.0
  oarThrust: 1.0
  turn: 1.0
  gunsBow: 1
  gunsStern: 0
  gunsPerSide: 3
";

    [Fact]
    public void ReadsABlockPerBoat()
    {
        var boats = BoatClasses.Parse(Two);
        Assert.Equal(2, boats.Count);
        var small = boats[0];
        Assert.Equal("small", small.Name);
        Assert.Equal(8, small.HullLength);
        Assert.Equal(1, small.Sails);
        Assert.Equal(1, small.Crew);
        Assert.Equal(200, small.Cargo);
        Assert.Equal(1000, small.Cost);
        Assert.Equal(1.15f, small.Turn);
        Assert.Equal(1, small.GunCount);
        Assert.Equal(7, boats[1].GunCount);   // 1 bow + 3 a side
    }

    [Fact]
    public void TheRigFactorFollowsTheSailState()
    {
        var small = BoatClasses.Parse(Two)[0];
        Assert.Equal(0.8f, small.ThrustFactor(true));
        Assert.Equal(0.85f, small.ThrustFactor(false));
    }

    [Fact]
    public void TheRepoFileHasEveryBoatTheGameNeeds()
    {
        var boats = BoatClasses.Parse(System.IO.File.ReadAllText(GameConfigTests.RepoFile("boats.yml")));
        BoatClasses.All = boats;
        Assert.Equal(3, boats.Count);
        // The medium boat is the game's original vessel and must stay it:
        // every handling number is a multiplier, and hers are all 1.
        var medium = BoatClasses.Ai;
        Assert.Equal(1f, medium.SailThrust);
        Assert.Equal(1f, medium.OarThrust);
        Assert.Equal(1f, medium.Turn);
        Assert.Equal(12, medium.HullLength);
        Assert.Equal(4, medium.HullWidth);
        Assert.Equal(7, medium.GunCount);
        Assert.Equal(16, BoatClasses.ByName("large").GunCount);
        Assert.Equal(1, BoatClasses.Starting.Crew);
    }

    [Fact]
    public void ByNameSaysWhatItHasWhenItCannotFindIt()
    {
        BoatClasses.All = BoatClasses.Parse(Two);
        Assert.Equal("medium", BoatClasses.ByName("MEDIUM").Name);   // case-insensitive
        Assert.Contains("small, medium", Assert.Throws<Exception>(() => BoatClasses.ByName("galleon")).Message);
    }

    [Theory]
    [InlineData("cargo")]
    [InlineData("crew")]
    [InlineData("gunsPerSide")]
    [InlineData("hullWidth")]
    public void EveryKeyIsRequired(string key)
    {
        var without = Two.Replace($"  {key}: ", "  gunwales: ");
        Assert.ThrowsAny<Exception>(() => BoatClasses.Parse(without));
    }

    [Theory]
    [InlineData("  crew: 0", "at least 1")]
    [InlineData("  cargo: -5", "at least 1")]
    [InlineData("  turn: 0", "more than 0")]
    [InlineData("  gunsBow: -1", "cannot be negative")]
    [InlineData("  cargo: lots", "not a whole number")]
    public void RejectsNonsenseValues(string line, string says)
    {
        string key = line.Trim().Split(':')[0];
        var text = Two.Replace($"  {key}: 1\n", line + "\n").Replace($"  {key}: 200\n", line + "\n")
                      .Replace($"  {key}: 1.15\n", line + "\n");
        Assert.Contains(says, Assert.ThrowsAny<Exception>(() => BoatClasses.Parse(text)).Message);
    }

    [Fact]
    public void TheBoatsTheCodeNamesMustBeInTheFile()
    {
        var onlyMedium = Two[Two.IndexOf("- name: medium")..];
        Assert.Contains("small", Assert.ThrowsAny<Exception>(() => BoatClasses.Parse(onlyMedium)).Message);
    }

    [Fact]
    public void RefusesTwoBoatsOfTheSameName()
    {
        Assert.Contains("two boats", Assert.ThrowsAny<Exception>(() => BoatClasses.Parse(Two + Two)).Message);
    }

    [Fact]
    public void RefusesAKeyThatBelongsToNoBoat()
    {
        Assert.Contains("before any boat",
            Assert.ThrowsAny<Exception>(() => BoatClasses.Parse("hullLength: 12\n" + Two)).Message);
    }
}
