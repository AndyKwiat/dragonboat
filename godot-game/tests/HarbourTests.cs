using Xunit;

namespace DragonboatGame.Tests;

// The harbour: buying a boat is always a trade, and the deals it refuses
// matter as much as the ones it takes (src/harbour/Harbour.cs).
public class HarbourTests
{
    private static BoatClass Boat(string name, int cost, int cargo) =>
        new(name, 12, 4, 2, 10, cargo, cost, 1, 1, 1, 1, 0, 3);

    private static readonly BoatClass Small = Boat("small", 1000, 200);
    private static readonly BoatClass Medium = Boat("medium", 10000, 1200);
    private static readonly BoatClass Large = Boat("large", 80000, 10000);

    [Theory]
    [InlineData("small", false)]
    [InlineData("medium", true)]
    [InlineData("large", true)]
    public void OnlyTheBiggerPortsBuildBoats(string size, bool has) =>
        Assert.Equal(has, Harbour.HasHarbour(new City { Size = size }));

    [Fact]
    public void TradingUpCostsTheDifference()
    {
        var offer = Harbour.OfferOf(Medium, Small, money: 9000, cargoUnits: 0);
        Assert.Equal(10000, offer.Price);
        Assert.Equal(1000, offer.TradeIn);
        Assert.Equal(9000, offer.Net);
        Assert.True(offer.CanBuy);
    }

    [Fact]
    public void TradingDownPays()
    {
        var offer = Harbour.OfferOf(Small, Large, money: 0, cargoUnits: 0);
        Assert.Equal(-79000, offer.Net);
        Assert.True(offer.CanBuy);      // no money needed: the deal hands you some
    }

    [Fact]
    public void ThePurseHasToCoverTheDifferenceNotThePrice()
    {
        Assert.True(Harbour.OfferOf(Medium, Small, money: 9000, cargoUnits: 0).CanBuy);
        var short1 = Harbour.OfferOf(Medium, Small, money: 8999, cargoUnits: 0);
        Assert.False(short1.CanBuy);
        Assert.Contains("1 short", short1.Blocked);
    }

    [Fact]
    public void ASmallerHoldIsRefusedRatherThanLosingTheCargo()
    {
        Assert.True(Harbour.OfferOf(Small, Medium, money: 0, cargoUnits: 200).CanBuy);
        var full = Harbour.OfferOf(Small, Medium, money: 0, cargoUnits: 201);
        Assert.False(full.CanBuy);
        Assert.Contains("sell down to 200", full.Blocked);
    }

    [Fact]
    public void TheBoatYouAreInIsListedButNotForSale()
    {
        var offer = Harbour.OfferOf(Medium, Medium, money: 99999, cargoUnits: 0);
        Assert.True(offer.Owned);
        Assert.False(offer.CanBuy);
        Assert.Equal(0, offer.Net);
    }

    [Fact]
    public void EveryBoatInTheFileIsOnOffer()
    {
        BoatClasses.All = new[] { Small, Medium, Large };
        var offers = Harbour.OffersAt(Small, money: 0, cargoUnits: 0);
        Assert.Equal(3, offers.Count);
        Assert.Single(offers, o => o.Owned);
    }
}
