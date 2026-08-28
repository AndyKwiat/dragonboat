using Godot;
using System;
using System.Collections.Generic;

// Closable, screen-centred panel describing the clicked city: a few facts,
// the hiring hall (large cities only), then the city's market — one row per
// product it trades, with buy/sell buttons. Sized as a fraction of the window
// within min/max bounds, scrolling only once the content is taller than the whole screen.
public partial class CityPopup : PanelContainer
{
    private Player player = null!;
    private GameTime time = null!;
    private StoryEvents storyEvents = null!;
    private WorldPrices world = null!;
    private Loans loans = null!;
    private Monasteries religion = null!;
    // Set by Main: opens the bank window over this panel (medium/large
    // cities), or the monastery's (the small cities that have a house).
    public Action<City>? OpenBank;
    public Action<City>? OpenMonastery;
    private ScrollContainer scroll = null!;
    private VBoxContainer content = null!;
    private Label title = null!, facts = null!, crewCount = null!, purse = null!;
    private Control crewHeading = null!, crewSection = null!;
    private Control bankHeading = null!, bankSection = null!;
    private Label bankStatus = null!;
    private Button bankButton = null!;
    private Control monasteryHeading = null!, monasterySection = null!;
    private Label monasteryStatus = null!;
    private Button monasteryButton = null!;
    private string monkDeliveredNote = "";
    private Control rumoursHeading = null!;
    private VBoxContainer rumours = null!;
    private VBoxContainer vipSection = null!;
    private string vipDeliveredNote = "";
    private VBoxContainer market = null!;
    private Button addCrew = null!, removeCrew = null!;
    private City? current;
    private readonly List<Action> refreshers = new();
    private int layoutFrames;   // frames left to re-measure the content height
    private Vector2 lastView;

    public bool IsOpen => Visible;

    public void Init(Player player, GameTime time, StoryEvents storyEvents, WorldPrices world, Loans loans, Monasteries religion)
    {
        this.player = player; this.time = time; this.storyEvents = storyEvents; this.world = world;
        this.loans = loans;
        this.religion = religion;
        int px = Config.PopupFontPx;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", Fonts.Panel(new Color(12 / 255f, 12 / 255f, 14 / 255f, 0.94f), 8, 24, 20, new Color("#666666")));

        scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(content);

        // Header: title + close.
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        title = Fonts.MakeLabel("", Fonts.UiBold, Config.PopupTitleFontPx, Colors.White);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var close = new Button { Text = "✕", Flat = true, TooltipText = "Close (Esc)" };
        close.AddThemeFontOverride("font", Fonts.Mono);
        close.AddThemeFontSizeOverride("font_size", 18);
        close.AddThemeColorOverride("font_color", new Color("#aaaaaa"));
        close.AddThemeColorOverride("font_hover_color", Colors.White);
        close.Pressed += Hide;
        header.AddChild(title);
        header.AddChild(close);
        content.AddChild(header);
        content.AddChild(Spacer(10));

        facts = Fonts.MakeLabel("", Fonts.Mono, px - 2, new Color("#bbbbbb"));
        content.AddChild(facts);

        // VIP deliveries: the just-delivered reward note and/or the city's
        // live offer with its Accept button, rebuilt per open (BuildVip).
        vipSection = new VBoxContainer();
        vipSection.AddThemeConstantOverride("separation", 4);
        content.AddChild(vipSection);

        // Rumours: word from the nearest markets, at the top so the player
        // hears the news the moment they step ashore.
        rumoursHeading = Heading("Rumours");
        content.AddChild(rumoursHeading);
        rumours = new VBoxContainer();
        rumours.AddThemeConstantOverride("separation", 4);
        content.AddChild(rumours);

        // Crew: hiring hall, large cities only (hidden everywhere else).
        crewHeading = Heading("Crew");
        content.AddChild(crewHeading);
        crewSection = new HBoxContainer();
        ((HBoxContainer)crewSection).AddThemeConstantOverride("separation", 14);
        crewCount = Fonts.MakeLabel("", Fonts.Mono, px, new Color("#dddddd"));
        crewCount.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var crewActions = new HBoxContainer();
        crewActions.AddThemeConstantOverride("separation", 6);
        addCrew = MakeButton("Add", () => player.ChangeCrew(1));
        removeCrew = MakeButton("Remove", () => player.ChangeCrew(-1));
        crewActions.AddChild(addCrew);
        crewActions.AddChild(removeCrew);
        crewSection.AddChild(crewCount);
        crewSection.AddChild(crewActions);
        content.AddChild(crewSection);

        // Bank: medium and large cities only (hidden everywhere else). The
        // window itself is a second popup over this one — see ui/BankPopup.cs.
        bankHeading = Heading("Bank");
        content.AddChild(bankHeading);
        bankSection = new HBoxContainer();
        ((HBoxContainer)bankSection).AddThemeConstantOverride("separation", 14);
        bankStatus = Fonts.MakeLabel("", Fonts.Mono, px, new Color("#dddddd"));
        bankStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bankButton = Fonts.MakeButton("Bank", () => { if (current != null) OpenBank?.Invoke(current); }, 74);
        bankSection.AddChild(bankStatus);
        bankSection.AddChild(bankButton);
        content.AddChild(bankSection);

        // Monastery: the small cities that have a house. Its window is a
        // second popup over this one — see ui/MonasteryPopup.cs.
        monasteryHeading = Heading("Monastery");
        content.AddChild(monasteryHeading);
        monasterySection = new HBoxContainer();
        ((HBoxContainer)monasterySection).AddThemeConstantOverride("separation", 14);
        monasteryStatus = Fonts.MakeLabel("", Fonts.Mono, px, new Color("#dddddd"));
        monasteryStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        monasteryButton = Fonts.MakeButton("Monastery", () => { if (current != null) OpenMonastery?.Invoke(current); }, 100);
        monasterySection.AddChild(monasteryStatus);
        monasterySection.AddChild(monasteryButton);
        content.AddChild(monasterySection);

        content.AddChild(Heading("Market"));
        market = new VBoxContainer();
        market.AddThemeConstantOverride("separation", 0);
        content.AddChild(market);

        content.AddChild(Spacer(16));
        content.AddChild(Rule(new Color("#444444")));
        content.AddChild(Spacer(10));
        purse = Fonts.MakeLabel("", Fonts.MonoBold, px, new Color("#ffd166"));
        content.AddChild(purse);
    }

    // The shared panel furniture lives in Fonts; a button here also refreshes
    // the panel after it has done its work.
    private static Control Spacer(float h) => Fonts.Spacer(h);
    private static Control Rule(Color c) => Fonts.Rule(c);
    private static Control Heading(string text) => Fonts.Heading(text);
    private static void SetEnabled(Button b, bool enabled) => Fonts.SetEnabled(b, enabled);

    private Button MakeButton(string label, Action onClick) => Fonts.MakeButton(label, () =>
    {
        onClick();
        Refresh();
    });

    public void Refresh()
    {
        foreach (var r in refreshers) r();
        var c = Config.Crew;
        crewCount.Text =
            $"{player.CrewCount} aboard (max {c.Max})   " +
            $"speed x{Crew.SpeedMultiplier(player.CrewCount, c):F2}   " +
            $"cargo {player.TotalUnits()}/{player.CargoCapacity()} ({c.CargoPerCrew}/hand)";
        // Dismissing a hand is refused when their share of the hold is in use.
        SetEnabled(addCrew, player.CanChangeCrew(1));
        SetEnabled(removeCrew, player.CanChangeCrew(-1));
        removeCrew.TooltipText = player.CrewCount > c.Min && !player.CanChangeCrew(-1)
            ? "Not enough hold space without them — sell some cargo first" : "";
        RefreshBank();
        RefreshMonastery();
        purse.Text = $"money {player.Money}    honour {player.Honour}    " +
                     $"cargo {player.TotalUnits()}/{player.CargoCapacity()} units (cost {player.CargoValue()})";
        Fonts.SetColor(purse, player.Money < 0 ? new Color("#ff8b7a") : new Color("#ffd166"));
    }

    // The bank row: what this city's bank is owed, or what it would lend.
    public void RefreshBank()
    {
        if (current == null || !bankSection.Visible) return;
        var loan = loans.LoanAt(current);
        int today = Days.Of(time.TotalMinutes);
        if (loan != null)
        {
            int left = loan.DaysLeft(today);
            bankStatus.Text = left >= 0
                ? $"you owe {loan.Owed}, due in {left} day{(left == 1 ? "" : "s")}"
                : $"you owe {loan.Owed} — {-left} day{(left == -1 ? "" : "s")} OVERDUE";
            Fonts.SetColor(bankStatus, left >= 0 ? Config.BankDebtColor : Config.LowColor);
        }
        else
        {
            int limit = loans.LimitFor(current, player.Honour);
            bankStatus.Text = limit > 0 ? $"will lend up to {limit} against your honour" : "will lend you nothing";
            Fonts.SetColor(bankStatus, limit > 0 ? new Color("#dddddd") : new Color("#888888"));
        }
    }

    // The monastery row: whether the poor will take a gift today, and
    // whether the brothers have an errand waiting.
    public void RefreshMonastery()
    {
        if (current == null || !monasterySection.Visible) return;
        var house = religion.At(current);
        if (house == null) return;
        int today = Days.Of(time.TotalMinutes);
        int wait = house.DaysUntilGiveAllowed(today, Config.Religion);
        var bits = wait == 0 ? "alms welcome today" : $"alms again in {wait} day{(wait == 1 ? "" : "s")}";
        if (house.Errand != null) bits += "   a monk seeks passage";
        monasteryStatus.Text = bits;
        Fonts.SetColor(monasteryStatus, house.Errand != null || wait == 0 ? Config.PietyColor : new Color("#888888"));
    }

    // The nearest other-city events, as one line each; hearing a rumour is
    // what discovers the event, so it shows on the map from now on — even
    // when its own city was never visited.
    private void BuildRumours(City city)
    {
        foreach (var child in rumours.GetChildren()) child.QueueFree();
        var heard = storyEvents.RumoursFor(city, Config.RumourCount);
        rumoursHeading.Visible = rumours.Visible = heard.Count > 0;
        foreach (var ev in heard)
        {
            ev.Discovered = true;
            bool dear = ev is ProductPriceStoryEvent p && p.Shortage;
            var line = Fonts.MakeLabel($"“{ev.Description}”", Fonts.Ui, Config.PopupFontPx - 1,
                ev is VipDeliveryStoryEvent ? new Color("#7fe3ff") : dear ? Config.MarketDearColor : Config.MarketCheapColor);
            line.AutowrapMode = TextServer.AutowrapMode.Word;
            line.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            rumours.AddChild(line);
        }
    }

    // The city's VIP offer (description + Accept) and, on arrival, the
    // reward note for passengers just delivered here.
    private void BuildVip(City city)
    {
        foreach (var child in vipSection.GetChildren()) child.QueueFree();
        var kids = new List<Control>();
        foreach (var (text, color) in new[] { (vipDeliveredNote, new Color("#ffd166")), (monkDeliveredNote, Config.PietyColor) })
        {
            if (text == "") continue;
            var note = Fonts.MakeLabel(text, Fonts.UiSemiBold, Config.PopupFontPx, color);
            note.AutowrapMode = TextServer.AutowrapMode.Word;
            note.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            kids.Add(note);
        }
        if (storyEvents.VipEventFor(city) is VipDeliveryStoryEvent offer)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 14);
            var desc = Fonts.MakeLabel(
                $"An important passenger seeks passage to {offer.Destination.Name} " +
                $"({Config.VipDeliveryMoney} money and {Config.VipDeliveryHonour} honour on arrival, plus up to " +
                $"{Config.Vips.SpeedHonour} more for a fast passage — they jump ship after {Config.Vips.AbandonDays} days)",
                Fonts.Ui, Config.PopupFontPx - 1, new Color("#7fe3ff"));
            desc.AutowrapMode = TextServer.AutowrapMode.Word;
            desc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var accept = MakeButton("Accept", () =>
            {
                player.VipPassengers.Add(new VipPassenger
                {
                    Destination = offer.Destination,
                    PickedUpDay = Days.Of(time.TotalMinutes),
                });
                storyEvents.Accept(offer);
                Rebuild();
            });
            accept.TooltipText = $"Take the passenger aboard, bound for {offer.Destination.Name}";
            row.AddChild(desc);
            row.AddChild(accept);
            kids.Add(row);
        }
        vipSection.Visible = kids.Count > 0;
        if (kids.Count > 0)
        {
            vipSection.AddChild(Spacer(10));
            foreach (var k in kids) vipSection.AddChild(k);
        }
    }

    // The names, not the ids, of what this city makes — for the facts line.
    private static List<string> ProducedNames(City city)
    {
        var names = new List<string>();
        foreach (var id in city.Produces) names.Add(GameData.ProductById(id)?.Name ?? id);
        return names;
    }

    private void BuildMarket(City city)
    {
        foreach (var child in market.GetChildren()) child.QueueFree();
        refreshers.Clear();
        int px = Config.PopupFontPx;
        var terms = Config.Market;

        foreach (var quote in Market.MarketFor(city, GameData.Products, terms))
        {
            var product = quote.Product;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 14);

            var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            info.AddThemeConstantOverride("separation", 0);
            // Name line: the product, its price against the world base price,
            // and — for a good this city makes itself — the producer mark.
            var nameRow = new HBoxContainer();
            nameRow.AddThemeConstantOverride("separation", 8);
            var name = Fonts.MakeLabel(product.Name, Fonts.UiSemiBold, px, Config.MarketNormalColor);
            name.MouseFilter = MouseFilterEnum.Stop;
            nameRow.AddChild(name);
            if (quote.Produces)
            {
                var mark = Fonts.MakeLabel("◆ PRODUCES", Fonts.MonoBold, px - 4, Config.MarketProducerColor);
                mark.MouseFilter = MouseFilterEnum.Stop;
                mark.TooltipText = $"{city.Name} makes {product.Name} — the cheapest it gets anywhere near here";
                mark.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                nameRow.AddChild(mark);
            }
            info.AddChild(nameRow);
            var desc = Fonts.MakeLabel(product.Description, Fonts.Ui, px - 3, new Color("#999999"));
            desc.AutowrapMode = TextServer.AutowrapMode.Word;
            desc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            info.AddChild(desc);
            // Debug readout: the two numbers every price in this row comes out
            // of — the market's live stock level against its size, and what
            // the city settles at when that level is 0.
            var debug = Fonts.MakeLabel("", Fonts.Mono, px - 4, Config.MarketDebugColor);
            info.AddChild(debug);

            var prices = Fonts.MakeLabel("", Fonts.Mono, px - 2, new Color("#cfd6e4"));
            prices.HorizontalAlignment = HorizontalAlignment.Right;

            var held = Fonts.MakeLabel("", Fonts.Mono, px - 1, new Color("#9fe8a0"));
            held.CustomMinimumSize = new Vector2(96, 0);
            held.HorizontalAlignment = HorizontalAlignment.Right;

            // The trade slider: centre = no deal, left = sell a fraction of
            // the stock aboard, right = buy a fraction of the most the hold,
            // the purse and the market itself allow. All of the arithmetic —
            // the price ladder, the limits, the exact refund on the way back
            // to centre, the hold's book value — belongs to the deal
            // (trading/MarketDeal.cs), which anchors itself on the market and
            // the hold as they are right now. That anchor is why the rows are
            // rebuilt whenever anything moves them behind the row's back.
            var deal = new MarketDeal(city, product, player, terms);
            var slider = new HSlider
            {
                MinValue = -1, MaxValue = 1, Step = 0.001, Value = 0,
                TickCount = 3,
                CustomMinimumSize = new Vector2(170, 0),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            var net = Fonts.MakeLabel("", Fonts.Mono, px - 2, new Color("#666666"));
            net.CustomMinimumSize = new Vector2(96, 0);
            net.HorizontalAlignment = HorizontalAlignment.Right;

            slider.ValueChanged += v => { if (deal.MoveTo(deal.UnitsAt(v))) Refresh(); };
            // Snap the knob to what was actually dealt: the limits may be 0,
            // or have shifted while other products were being slid around.
            slider.DragEnded += _ => slider.SetValueNoSignal(deal.Fraction);

            row.AddChild(info);
            row.AddChild(prices);
            row.AddChild(held);
            row.AddChild(slider);
            row.AddChild(net);
            // Give: only where the city is paying well over the odds for it —
            // donate every unit held for no money, one honour point apiece.
            // Rebuild the rows after, so the sliders re-anchor on the new
            // holdings and the new stock level.
            Button? give = null;
            if (quote.SellPrice >= world.Of(product) * Config.MarketGiveMinRatio)
            {
                give = MakeButton("Give", () =>
                {
                    player.GiveAway(product.Id);
                    Rebuild();
                });
                row.AddChild(give);
            }
            var wrap = new VBoxContainer();
            wrap.AddThemeConstantOverride("separation", 0);
            wrap.AddChild(Spacer(8));
            wrap.AddChild(row);
            wrap.AddChild(Spacer(8));
            wrap.AddChild(Rule(new Color("#2a2a2e")));
            market.AddChild(wrap);

            refreshers.Add(() =>
            {
                // Prices are re-read every refresh, not baked into the row:
                // one unit changing hands moves them.
                var q = Market.QuoteFor(city, product, terms);
                // How that reads against the rest of the world today — the
                // same rule the map labels use (Market.Read).
                var read = Market.Read(q.SellPrice, world.Of(product));
                var tint = Config.MarketTint(read);
                // A live supply shock rides along as a days-left tag, so you
                // can tell a passing panic from the city's standing price.
                var ev = storyEvents.PriceEventFor(city, product.Id);
                int daysLeft = ev?.DaysLeftAt(time.TotalMinutes) ?? 0;
                name.Text = read.OffAverage
                    ? $"{product.Name}  {(read.Dear ? "▲" : "▼")} {read.Percent}%{(ev != null ? $"  ({daysLeft}d)" : "")}"
                    : product.Name;
                Fonts.SetColor(name, tint);
                var against = $"{read.Percent}% of the {Math.Round(read.WorldAverage)} {product.Name} fetches across the world";
                name.TooltipText = ev != null
                    ? $"{city.Name} trades it at {against} — a {(ev.Shortage ? "shortage" : "glut")} with {daysLeft} day{(daysLeft == 1 ? "" : "s")} left in it"
                    : $"{city.Name} trades it at {against}";
                prices.Text = $"buy {q.BuyPrice}\nsell {q.SellPrice}";
                Fonts.SetColor(prices, read.OffAverage ? tint : new Color("#cfd6e4"));
                debug.Text = $"supply {q.Supply}/{q.MarketSize}   local {Math.Ceiling(city.LocalBaseOf(product.Id))}" +
                             $"   world {Math.Round(read.WorldAverage)}";

                // What's aboard, and what it cost a unit — the number to hold
                // this city's sell price against.
                int have = player.Held(product.Id);
                held.Text = have > 0 ? $"have {have}\npaid {Math.Round(player.AverageCost(product.Id))}" : "have 0";
                Fonts.SetColor(held, have > 0 ? new Color("#9fe8a0") : new Color("#666666"));
                // The staged deal: units, money, and — for a sale — whether
                // this call was worth making.
                net.Text = deal.Units == 0 ? ""
                    : deal.Units > 0 ? $"buy {deal.Units}\n-{deal.Cost}"
                    : $"sell {-deal.Units}\n+{-deal.Cost}\n{(deal.Profit >= 0 ? "+" : "-")}{Math.Abs(deal.Profit)} profit";
                Fonts.SetColor(net, deal.Units > 0 || deal.Profit < 0 ? Config.MarketDearColor : Config.MarketCheapColor);
                if (give != null)
                {
                    SetEnabled(give, have > 0);
                    give.TooltipText = have > 0 ? $"Give all {have} away: +{Player.GiveAwayHonour(have)} honour, no money" : "Nothing aboard to give";
                }
            });
        }

        if (refreshers.Count == 0)
        {
            var empty = Fonts.MakeLabel("This city trades nothing.", Fonts.Ui, px, new Color("#888888"));
            var wrap = new VBoxContainer();
            wrap.AddChild(Spacer(8)); wrap.AddChild(empty); wrap.AddChild(Spacer(8));
            market.AddChild(wrap);
        }
    }

    public void Show(City city)
    {
        // Seeing the market is what discovers the city and its live events.
        city.Discovered = true;
        storyEvents.DiscoverAt(city);
        title.Text = city.Name;
        facts.Text = $"{"coords",-8}{Mathf.RoundToInt(city.X)}, {Mathf.RoundToInt(city.Y)}\n{"size",-8}{city.Size}\n" +
                     $"{"makes",-8}{(city.Produces.Count > 0 ? string.Join(", ", ProducedNames(city)) : "nothing")}\n" +
                     $"{"market",-8}{city.MarketSize} units either way";
        // Only the large cities have a hiring hall; only medium and large
        // ones have a bank.
        bool hiring = city.Size == "large";
        crewHeading.Visible = hiring;
        crewSection.Visible = hiring;
        bool banking = Loans.HasBank(city);
        bankHeading.Visible = banking;
        bankSection.Visible = banking;
        bool monastic = religion.HasMonastery(city);
        monasteryHeading.Visible = monastic;
        monasterySection.Visible = monastic;
        // Arriving is what completes a VIP delivery: passengers bound here
        // step ashore, and the popup opens on the reward note.
        // Landing a monk pays like a VIP delivery, in piety.
        var monks = religion.DeliverMonks(city);
        player.Piety += monks.Piety;
        monkDeliveredNote = monks.Count == 0 ? "" :
            $"Landed {(monks.Count == 1 ? "a monk" : $"{monks.Count} monks")} safely — +{monks.Piety} piety";
        var delivered = player.DeliverVips(city, Days.Of(time.TotalMinutes));
        vipDeliveredNote = delivered.Count == 0 ? "" :
            $"Delivered {(delivered.Count == 1 ? "an important passenger" : $"{delivered.Count} important passengers")} — " +
            $"+{delivered.Money} money, +{delivered.TotalHonour} honour" +
            (delivered.SpeedHonour > 0 ? $" ({delivered.Honour} for the passage, {delivered.SpeedHonour} for the speed of it)" : "");
        BuildVip(city);
        if (city != current)
        {
            current = city;
            BuildRumours(city);
            BuildMarket(city);
        }
        Refresh();
        Visible = true;
        layoutFrames = 3;
    }

    // Prices and days-left tags are baked into the market rows, so a story
    // event rollover (midnight) needs the rows rebuilt, not just refreshed.
    public void Rebuild()
    {
        if (current == null || !Visible) return;
        storyEvents.DiscoverAt(current);   // events that just rolled in while watching
        BuildVip(current);
        BuildRumours(current);
        BuildMarket(current);
        Refresh();
        layoutFrames = 3;
    }

    public new void Hide()
    {
        Visible = false;
        current = null;
    }

    // Size and centre the panel: width from the window, height to fit the
    // content up to the max. Wrapped labels only know their height after a
    // layout pass, so re-measure for a few frames after showing / resizing.
    public void Update(Vector2 view)
    {
        if (!Visible) return;
        if (view != lastView) { lastView = view; layoutFrames = 3; }
        if (layoutFrames <= 0) return;
        layoutFrames--;
        float padX = 24, padY = 20;
        float w = Mathf.Clamp(view.X * Config.PopupWidthVw / 100, Config.PopupMinWidthPx, Config.PopupMaxWidthPx);
        float maxH = view.Y * Config.PopupMaxHeightVh / 100;
        float innerW = w - 2 * padX;
        content.CustomMinimumSize = new Vector2(innerW, 0);
        float contentH = content.GetCombinedMinimumSize().Y;
        float h = Mathf.Min(contentH + 2 * padY, maxH);
        scroll.CustomMinimumSize = new Vector2(innerW, h - 2 * padY);
        Size = new Vector2(w, h);
        Position = new Vector2((view.X - w) / 2, (view.Y - h) / 2);
    }
}
