using System;
using System.Collections.Generic;
using Xunit;

namespace DragonboatGame.Tests;

// The staged position a market row's slider moves (src/trading/MarketDeal.cs):
// the anchoring, the limits, the exact refund, the cost basis and the profit.
// This logic used to live in closures inside the Godot popup, where none of
// it could be exercised — these are the cases that were being taken on trust.
public class MarketDealTests
{
    private static MarketTerms Terms => Economy.Terms;

    private static (City, Product, Economy.Trader) Setup(string size = "small", double localBase = 15,
        int held = 0, double paid = 0, int money = 1_000_000, int space = 1000)
    {
        var p = new Product { Id = "WOOL", Name = "Wool", BasePrice = 15 };
        var city = Economy.Market(size, p.Id, localBase);
        var trader = new Economy.Trader { Money = money, Space = space };
        if (held > 0) trader.Put(p.Id, held, paid);
        return (city, p, trader);
    }

    // -- the round trip, which is the whole point of anchoring --------------

    [Fact]
    public void SlidingOutAndBackCostsNothingAtAll()
    {
        var (city, p, trader) = Setup(held: 40, paid: 480);
        int money0 = trader.Money;
        double basis0 = trader.BasisOf(p.Id);
        int held0 = trader.Held(p.Id), supply0 = city.SupplyOf(p.Id);
        var deal = new MarketDeal(city, p, trader, Terms);
        // Wander all over the range, buying and selling, then come home.
        foreach (int stop in new[] { 5, 30, -10, 17, -40, 0, 23, -3 }) deal.MoveTo(stop);
        deal.MoveTo(0);
        Assert.Equal(money0, trader.Money);
        Assert.Equal(basis0, trader.BasisOf(p.Id), 6);
        Assert.Equal(held0, trader.Held(p.Id));
        Assert.Equal(supply0, city.SupplyOf(p.Id));
    }

    // Undoing a buy must refund what it cost, NOT sell it back at the city's
    // lower buying price — otherwise nudging the slider would bleed money.
    [Fact]
    public void UndoingABuyIsNotASale()
    {
        var (city, p, trader) = Setup();
        int money0 = trader.Money;
        var deal = new MarketDeal(city, p, trader, Terms);
        deal.MoveTo(10);
        Assert.True(trader.Money < money0);          // it really was bought
        deal.MoveTo(0);
        Assert.Equal(money0, trader.Money);          // and really was refunded
        Assert.Equal(0, trader.Held(p.Id));
    }

    [Fact]
    public void EachStepIsPricedOffTheAnchorNotTheLastStep()
    {
        var (city, p, trader) = Setup();
        var direct = new MarketDeal(city, p, trader, Terms);
        direct.MoveTo(25);
        int paidInOneGo = 1_000_000 - trader.Money;

        var (city2, p2, trader2) = Setup();
        var stepped = new MarketDeal(city2, p2, trader2, Terms);
        for (int n = 1; n <= 25; n++) stepped.MoveTo(n);
        Assert.Equal(paidInOneGo, 1_000_000 - trader2.Money);
    }

    // -- the limits --------------------------------------------------------

    [Fact]
    public void TheMarketIsNeverDrainedOrGluttedPastItsSize()
    {
        var (city, p, trader) = Setup();
        var deal = new MarketDeal(city, p, trader, Terms);
        Assert.Equal(city.MarketSize, deal.MaxBuy());     // supply 0: a whole market's worth
        deal.MoveTo(9999);
        Assert.Equal(city.MarketSize, deal.Units);
        Assert.Equal(-city.MarketSize, city.SupplyOf(p.Id));
    }

    [Fact]
    public void ASaleIsCappedByTheHoldAndByWhatTheCityCanAbsorb()
    {
        var (city, p, trader) = Setup(held: 5, paid: 60);
        var deal = new MarketDeal(city, p, trader, Terms);
        Assert.Equal(5, deal.MaxSell());                  // only 5 aboard
        deal.MoveTo(-9999);
        Assert.Equal(-5, deal.Units);

        // A city already near its glut will not take a full hold.
        var (city2, p2, trader2) = Setup(held: 100, paid: 1200);
        city2.Supply[p2.Id] = city2.MarketSize - 3;
        var deal2 = new MarketDeal(city2, p2, trader2, Terms);
        Assert.Equal(3, deal2.MaxSell());
        deal2.MoveTo(-100);
        Assert.Equal(-3, deal2.Units);
        Assert.Equal(city2.MarketSize, city2.SupplyOf(p2.Id));
    }

    [Fact]
    public void BuyingIsCappedByThePurseAndTheHold()
    {
        var (city, p, trader) = Setup(money: 100);
        var deal = new MarketDeal(city, p, trader, Terms);
        deal.MoveTo(60);
        Assert.True(trader.Money >= 0, "the purse went negative");
        Assert.True(deal.Units < 60);

        var (city2, p2, trader2) = Setup(space: 7);
        var deal2 = new MarketDeal(city2, p2, trader2, Terms);
        deal2.MoveTo(60);
        Assert.Equal(7, deal2.Units);
    }

    // The limits are worked out as if this row's own deal were first undone,
    // so dragging one row back and forth never eats the others' headroom.
    [Fact]
    public void LimitsIgnoreThisRowsOwnStagedDeal()
    {
        var (city, p, trader) = Setup(space: 20);
        var deal = new MarketDeal(city, p, trader, Terms);
        Assert.Equal(20, deal.MaxBuy());
        deal.MoveTo(12);
        Assert.Equal(20, deal.MaxBuy());   // still 20, not 8
    }

    // -- the cost basis and the profit -------------------------------------

    [Fact]
    public void BuyingAddsWhatItCostToTheHoldsBookValue()
    {
        var (city, p, trader) = Setup(held: 10, paid: 100);
        var deal = new MarketDeal(city, p, trader, Terms);
        deal.MoveTo(6);
        Assert.Equal(100 + deal.Cost, trader.BasisOf(p.Id), 6);
        Assert.Equal(16, trader.Held(p.Id));
    }

    [Fact]
    public void SellingLeavesTheUntouchedUnitsShareOfTheBookValue()
    {
        var (city, p, trader) = Setup(held: 10, paid: 120);   // 12 apiece
        var deal = new MarketDeal(city, p, trader, Terms);
        deal.MoveTo(-4);
        Assert.Equal(48, deal.CostOfSold);                    // 4 x 12
        Assert.Equal(72, trader.BasisOf(p.Id), 6);            // 6 x 12 left
        Assert.Equal(6, trader.Held(p.Id));
        deal.MoveTo(-10);
        Assert.Equal(0, trader.BasisOf(p.Id), 6);             // nothing left, nothing owed
    }

    [Fact]
    public void ProfitIsRevenueLessWhatTheUnitsCost()
    {
        // Bought at 5 apiece, selling into a city that settles at 40.
        var (city, p, trader) = Setup(localBase: 40, held: 10, paid: 50);
        var deal = new MarketDeal(city, p, trader, Terms);
        deal.MoveTo(-10);
        Assert.Equal(-deal.Cost - deal.CostOfSold, deal.Profit);
        Assert.True(deal.Profit > 0, "a good voyage should show a profit");

        // The same cargo dumped where it is worth less than it cost.
        var (cheap, p2, trader2) = Setup(localBase: 3, held: 10, paid: 200);
        var loss = new MarketDeal(cheap, p2, trader2, Terms);
        loss.MoveTo(-10);
        Assert.True(loss.Profit < 0, "a bad voyage should show a loss");
    }

    [Fact]
    public void OnlySalesHaveAProfit()
    {
        var (city, p, trader) = Setup(held: 10, paid: 100);
        var deal = new MarketDeal(city, p, trader, Terms);
        Assert.Equal(0, deal.Profit);
        deal.MoveTo(5);
        Assert.Equal(0, deal.Profit);
        Assert.Equal(0, deal.CostOfSold);
    }

    // Buying then immediately selling back the same units cannot profit —
    // the whole reason a unit is priced one step along at a time.
    [Fact]
    public void YouCannotBuyLowAndSellHighInTheSameMarket()
    {
        for (int n = 1; n <= 40; n++)
        {
            var (city, p, trader) = Setup(localBase: 40);
            int money0 = trader.Money;
            var buy = new MarketDeal(city, p, trader, Terms);
            buy.MoveTo(n);
            // A fresh deal, as if the row had been rebuilt after the purchase.
            var sell = new MarketDeal(city, p, trader, Terms);
            sell.MoveTo(-n);
            Assert.True(trader.Money <= money0, $"turned a profit on {n} units in one market");
        }
    }

    // -- slider glue -------------------------------------------------------

    [Fact]
    public void TheKnobMapsOntoTheLimitsAndBackAgain()
    {
        var (city, p, trader) = Setup(held: 20, paid: 240, space: 100);
        var deal = new MarketDeal(city, p, trader, Terms);
        Assert.Equal(0, deal.UnitsAt(0));
        Assert.Equal(deal.MaxBuy(), deal.UnitsAt(1));
        Assert.Equal(-deal.MaxSell(), deal.UnitsAt(-1));

        deal.MoveTo(deal.UnitsAt(0.5));
        Assert.True(deal.Units > 0);
        Assert.Equal(0.5, deal.Fraction, 2);
        deal.MoveTo(0);
        Assert.Equal(0, deal.Fraction, 6);
        deal.MoveTo(-20);
        Assert.Equal(-1, deal.Fraction, 6);
    }

    [Fact]
    public void AKnobWithNothingToTradeStaysPut()
    {
        var (city, p, trader) = Setup(money: 0);          // no purse, nothing aboard
        var deal = new MarketDeal(city, p, trader, Terms);
        Assert.Equal(0, deal.MaxBuy());
        Assert.Equal(0, deal.MaxSell());
        Assert.False(deal.MoveTo(deal.UnitsAt(1)));
        Assert.False(deal.MoveTo(deal.UnitsAt(-1)));
        Assert.Equal(0, deal.Fraction);
    }
}
