using Godot;
using System;
using System.Collections.Generic;

// Closable, screen-centred panel describing the clicked city: a few facts,
// the hiring hall (large cities only), then the city's market — one row per
// product it trades, with buy/sell buttons. Sized as a fraction of the window
// within min/max bounds, scrolling when the content is taller than 80vh.
public partial class CityPopup : PanelContainer
{
    private static readonly int[] Steps = { 1, 10 };

    private Player player = null!;
    private GameTime time = null!;
    private ScrollContainer scroll = null!;
    private VBoxContainer content = null!;
    private Label title = null!, facts = null!, crewCount = null!, purse = null!;
    private Control crewHeading = null!, crewSection = null!;
    private VBoxContainer market = null!;
    private Button addCrew = null!, removeCrew = null!;
    private City? current;
    private readonly List<Action> refreshers = new();
    private int layoutFrames;   // frames left to re-measure the content height
    private Vector2 lastView;

    public bool IsOpen => Visible;

    public void Init(Player player, GameTime time)
    {
        this.player = player; this.time = time;
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

        // Crew: hiring hall, large cities only (hidden everywhere else).
        crewHeading = Heading("Crew");
        content.AddChild(crewHeading);
        crewSection = new HBoxContainer();
        ((HBoxContainer)crewSection).AddThemeConstantOverride("separation", 14);
        crewCount = Fonts.MakeLabel("", Fonts.Mono, px, new Color("#dddddd"));
        crewCount.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var crewActions = new HBoxContainer();
        crewActions.AddThemeConstantOverride("separation", 6);
        // Free, and no haggling involved, so these don't cost the player any time.
        addCrew = MakeButton("Add", () => player.ChangeCrew(1), costsTime: false);
        removeCrew = MakeButton("Remove", () => player.ChangeCrew(-1), costsTime: false);
        crewActions.AddChild(addCrew);
        crewActions.AddChild(removeCrew);
        crewSection.AddChild(crewCount);
        crewSection.AddChild(crewActions);
        content.AddChild(crewSection);

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

    private static Control Spacer(float h) => new Control { CustomMinimumSize = new Vector2(0, h), MouseFilter = MouseFilterEnum.Ignore };
    private static Control Rule(Color c) => new ColorRect { Color = c, CustomMinimumSize = new Vector2(0, 1), MouseFilter = MouseFilterEnum.Ignore };

    private Control Heading(string text)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 0);
        box.AddChild(Spacer(18));
        box.AddChild(Fonts.MakeLabel(text, Fonts.UiSemiBold, Config.PopupFontPx + 4, Colors.White));
        box.AddChild(Spacer(6));
        box.AddChild(Rule(new Color("#444444")));
        box.AddChild(Spacer(8));
        return box;
    }

    private Button MakeButton(string label, Action onClick, bool costsTime = true)
    {
        var b = new Button { Text = label, CustomMinimumSize = new Vector2(58, 0) };
        var normal = Fonts.Panel(new Color("#23262c"), 4, 10, 4, new Color("#666666"));
        var hover = Fonts.Panel(new Color("#2b2f36"), 4, 10, 4, new Color("#777777"));
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", normal);
        b.AddThemeStyleboxOverride("disabled", normal);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeFontOverride("font", Fonts.Ui);
        b.AddThemeFontSizeOverride("font_size", Config.PopupFontPx - 2);
        b.AddThemeColorOverride("font_color", new Color("#eeeeee"));
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_pressed_color", Colors.White);
        b.AddThemeColorOverride("font_disabled_color", new Color("#eeeeee"));
        b.Pressed += () =>
        {
            onClick();
            // Haggling takes time: every market button that actually does
            // something costs the player ten minutes of the day.
            if (costsTime) time.Advance(Config.MinutesPerTransaction);
            Refresh();
        };
        return b;
    }

    private static void SetEnabled(Button b, bool enabled)
    {
        b.Disabled = !enabled;
        b.Modulate = new Color(1, 1, 1, enabled ? 1f : 0.35f);
        b.MouseDefaultCursorShape = enabled ? CursorShape.PointingHand : CursorShape.Arrow;
    }

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
        purse.Text = $"money {player.Money}    cargo {player.TotalUnits()}/{player.CargoCapacity()} units";
        Fonts.SetColor(purse, player.Money < 0 ? new Color("#ff8b7a") : new Color("#ffd166"));
    }

    private void BuildMarket(City city)
    {
        foreach (var child in market.GetChildren()) child.QueueFree();
        refreshers.Clear();
        int px = Config.PopupFontPx;

        foreach (var quote in Market.MarketFor(city))
        {
            var product = quote.Product;
            // A local price quirk colours the whole row: red = this city
            // charges over the odds, green = a bargain.
            bool dear = quote.PriceMultiplier > 1;
            bool quirky = quote.PriceMultiplier != 1;
            var tint = !quirky ? Config.MarketNormalColor : dear ? Config.MarketDearColor : Config.MarketCheapColor;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 14);

            var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            info.AddThemeConstantOverride("separation", 0);
            var name = Fonts.MakeLabel(quirky
                ? $"{product.Name}  {(dear ? "▲" : "▼")} {Math.Round(quote.PriceMultiplier * 100)}%"
                : product.Name, Fonts.UiSemiBold, px, tint);
            name.MouseFilter = MouseFilterEnum.Stop;
            name.TooltipText = quirky ? $"{city.Name} trades {product.Name} at {quote.PriceMultiplier}x the usual price" : "";
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

            var actions = new HBoxContainer();
            actions.AddThemeConstantOverride("separation", 6);
            var buyButtons = new List<Button>();
            var sellButtons = new List<Button>();
            foreach (int n in Steps)
            {
                var b = MakeButton($"Buy {n}", () => player.Buy(product.Id, n, quote.BuyPrice));
                buyButtons.Add(b);
                actions.AddChild(b);
            }
            foreach (int n in Steps)
            {
                var b = MakeButton($"Sell {n}", () => player.Sell(product.Id, n, quote.SellPrice));
                sellButtons.Add(b);
                actions.AddChild(b);
            }

            row.AddChild(info);
            row.AddChild(prices);
            row.AddChild(held);
            row.AddChild(actions);
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
                // Selling is capped by what's aboard, buying by the free space;
                // the purse never blocks a trade. A partial buy is fine, so the
                // button only greys out when the hold is completely full.
                int space = player.CargoSpace();
                for (int i = 0; i < Steps.Length; i++)
                {
                    SetEnabled(sellButtons[i], have >= Steps[i]);
                    SetEnabled(buyButtons[i], space > 0);
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
        title.Text = city.Name;
        facts.Text = $"{"coords",-8}{Mathf.RoundToInt(city.X)}, {Mathf.RoundToInt(city.Y)}\n{"size",-8}{city.Size}";
        // Only the large cities have a hiring hall.
        bool hiring = city.Size == "large";
        crewHeading.Visible = hiring;
        crewSection.Visible = hiring;
        if (city != current)
        {
            current = city;
            BuildMarket(city);
        }
        Refresh();
        Visible = true;
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
