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
    private Loans loans = null!;
    // Set by Main: opens the bank window over this panel (medium/large cities).
    public Action<City>? OpenBank;
    private ScrollContainer scroll = null!;
    private VBoxContainer content = null!;
    private Label title = null!, facts = null!, crewCount = null!, purse = null!;
    private Control crewHeading = null!, crewSection = null!;
    private Control bankHeading = null!, bankSection = null!;
    private Label bankStatus = null!;
    private Button bankButton = null!;
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

    public void Init(Player player, GameTime time, StoryEvents storyEvents, Loans loans)
    {
        this.player = player; this.time = time; this.storyEvents = storyEvents; this.loans = loans;
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
        purse.Text = $"money {player.Money}    honour {player.Honour}    cargo {player.TotalUnits()}/{player.CargoCapacity()} units";
        Fonts.SetColor(purse, player.Money < 0 ? new Color("#ff8b7a") : new Color("#ffd166"));
    }

    // The bank row: what this city's bank is owed, or what it would lend.
    public void RefreshBank()
    {
        if (current == null || !bankSection.Visible) return;
        var loan = loans.LoanAt(current);
        int today = Loans.DayOf(time.TotalMinutes);
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
            bool dear = ev is ProductPriceStoryEvent p && p.PriceMultiplier > 1;
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
        if (vipDeliveredNote != "")
        {
            var note = Fonts.MakeLabel(vipDeliveredNote, Fonts.UiSemiBold, Config.PopupFontPx, new Color("#ffd166"));
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
                $"({Config.VipDeliveryMoney} money, {Config.VipDeliveryHonour} honour on arrival)",
                Fonts.Ui, Config.PopupFontPx - 1, new Color("#7fe3ff"));
            desc.AutowrapMode = TextServer.AutowrapMode.Word;
            desc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var accept = MakeButton("Accept", () =>
            {
                player.VipPassengers.Add(offer.Destination);
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

    private void BuildMarket(City city)
    {
        foreach (var child in market.GetChildren()) child.QueueFree();
        refreshers.Clear();
        int px = Config.PopupFontPx;

        foreach (var quote in Market.MarketFor(city, storyEvents))
        {
            var product = quote.Product;
            // Colour the row by how the quoted sell price compares to the
            // product's base price: red = over the odds, green = a bargain.
            double ratio = quote.SellPrice / product.BasePrice;
            bool dear = quote.SellPrice > product.BasePrice;
            bool quirky = quote.SellPrice != product.BasePrice;
            var tint = !quirky ? Config.MarketNormalColor : dear ? Config.MarketDearColor : Config.MarketCheapColor;
            // A price driven by a story event carries its shelf life; the
            // structural small/medium discounts are permanent, so no tag.
            int daysLeft = quote.Event?.DaysLeftAt(time.TotalMinutes) ?? 0;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 14);

            var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            info.AddThemeConstantOverride("separation", 0);
            var name = Fonts.MakeLabel(quirky
                ? $"{product.Name}  {(dear ? "▲" : "▼")} {Math.Round(ratio * 100)}%{(quote.Event != null ? $"  ({daysLeft}d)" : "")}"
                : product.Name, Fonts.UiSemiBold, px, tint);
            name.MouseFilter = MouseFilterEnum.Stop;
            name.TooltipText =
                quote.Event != null ? $"{city.Name} trades {product.Name} at {Math.Round(ratio * 100)}% of the base price for another {daysLeft} day{(daysLeft == 1 ? "" : "s")}" :
                quirky ? $"{city.Name} trades {product.Name} at {Math.Round(ratio * 100)}% of the base price" : "";
            var desc = Fonts.MakeLabel(product.Description, Fonts.Ui, px - 3, new Color("#999999"));
            desc.AutowrapMode = TextServer.AutowrapMode.Word;
            desc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            info.AddChild(name);
            info.AddChild(desc);

            var prices = Fonts.MakeLabel($"buy {quote.BuyPrice}\nsell {quote.SellPrice}", Fonts.Mono, px - 2, quirky ? tint : new Color("#cfd6e4"));
            prices.HorizontalAlignment = HorizontalAlignment.Right;

            var held = Fonts.MakeLabel("", Fonts.Mono, px - 1, new Color("#9fe8a0"));
            held.CustomMinimumSize = new Vector2(72, 0);
            held.HorizontalAlignment = HorizontalAlignment.Right;

            // The trade slider: centre = no deal, left = sell a fraction of
            // the stock aboard, right = buy a fraction of the most the hold
            // and the purse allow. Deltas hit the player immediately, and a
            // leg is undone at the price it was struck (Player.Restage), so
            // sliding back to centre always refunds in full.
            int baseline = player.Held(product.Id);
            int delta = 0;
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

            // The most that fits AND can be paid for, as if this product's own
            // staged deal were first undone (other products' deals stand).
            int MaxBuy()
            {
                int space = player.CargoSpace() + delta;
                int money = player.Money + delta * (delta > 0 ? quote.BuyPrice : quote.SellPrice);
                return Math.Min(space, quote.BuyPrice > 0 ? money / quote.BuyPrice : space);
            }
            slider.ValueChanged += v =>
            {
                int target = v >= 0 ? (int)Math.Round(v * MaxBuy()) : -(int)Math.Round(-v * baseline);
                if (target == delta) return;
                player.Restage(product.Id, delta, target, quote.BuyPrice, quote.SellPrice);
                delta = target;
                Refresh();
            };
            // Snap the knob to what was actually dealt (the max may be 0, or
            // have shifted while other products were being slid around).
            slider.DragEnded += _ =>
            {
                int maxBuy = MaxBuy();
                slider.SetValueNoSignal(delta >= 0
                    ? (maxBuy > 0 ? (double)delta / maxBuy : 0)
                    : (baseline > 0 ? (double)delta / baseline : 0));
            };

            row.AddChild(info);
            row.AddChild(prices);
            row.AddChild(held);
            row.AddChild(slider);
            row.AddChild(net);
            // Give: only where the city pays over the odds — donate every
            // unit held for no money, one honour point apiece. Rebuild the
            // rows after, so the sliders' baselines match the new holdings.
            Button? give = null;
            if (quote.SellPrice > product.BasePrice)
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
                int have = player.Held(product.Id);
                held.Text = $"have {have}";
                Fonts.SetColor(held, have > 0 ? new Color("#9fe8a0") : new Color("#666666"));
                net.Text = delta == 0 ? ""
                    : delta > 0 ? $"buy {delta}\n-{delta * quote.BuyPrice}"
                    : $"sell {-delta}\n+{-delta * quote.SellPrice}";
                Fonts.SetColor(net, delta > 0 ? Config.MarketDearColor : Config.MarketCheapColor);
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
        facts.Text = $"{"coords",-8}{Mathf.RoundToInt(city.X)}, {Mathf.RoundToInt(city.Y)}\n{"size",-8}{city.Size}";
        // Only the large cities have a hiring hall; only medium and large
        // ones have a bank.
        bool hiring = city.Size == "large";
        crewHeading.Visible = hiring;
        crewSection.Visible = hiring;
        bool banking = Loans.HasBank(city);
        bankHeading.Visible = banking;
        bankSection.Visible = banking;
        // Arriving is what completes a VIP delivery: passengers bound here
        // step ashore, and the popup opens on the reward note.
        int delivered = player.DeliverVips(city);
        vipDeliveredNote = delivered == 0 ? "" :
            $"Delivered {(delivered == 1 ? "an important passenger" : $"{delivered} important passengers")} — " +
            $"+{delivered * Config.VipDeliveryMoney} money, +{delivered * Config.VipDeliveryHonour} honour";
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
