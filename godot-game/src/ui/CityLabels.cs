using Godot;
using System;
using System.Collections.Generic;

// Overlay drawing a name label for every city on screen, plus the
// hit-testing for hover/click: a hit is "within CityHitRadiusPx of the city
// centre, OR inside the label box padded by CityHitPadPx". Labels hang off
// the top of the city block. Under the name, discovered price intel: the
// few products the player knows are furthest off base price, and any
// supply shock they have heard about (see RefreshIntel).
public partial class CityLabels : Control
{
    private sealed class Entry
    {
        public City City = null!;
        public Label Label = null!;
        public RichTextLabel Intel = null!;
        public bool HasIntel;
        public float HalfWidthPx, HeightPx, MinZoom;
        public float IntelWidthPx = Config.CityIntelBoxPx;
        public bool Visible, Hovered;
        public Vector2 Screen;
        public Color Color;
    }

    private readonly List<Entry> entries = new();
    private StoryEvents storyEvents = null!;
    private WorldPrices world = null!;
    private GameTime time = null!;
    private Loans loans = null!;
    private Monasteries religion = null!;
    private Player player = null!;
    // Debug (P): every price in every city, discovered or not.
    private bool debugPrices;
    // -1 = every product; otherwise an index into GameData.Products and only
    // that product is listed ([ / ] step through, P leaves the mode).
    private int debugProduct = -1;
    public bool DebugPrices => debugPrices;
    public Product? DebugProduct => debugProduct < 0 ? null : GameData.Products[debugProduct];

    public void Init(List<City> cities, StoryEvents storyEvents, WorldPrices world, GameTime time,
        Loans loans, Monasteries religion, Player player)
    {
        this.storyEvents = storyEvents; this.world = world; this.time = time; this.loans = loans;
        this.religion = religion; this.player = player;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var city in cities)
        {
            var style = Config.CityLabel[city.Size];
            var font = style.Heavy ? Fonts.UiBold : style.Bold ? Fonts.UiSemiBold : Fonts.Ui;
            var label = Fonts.MakeLabel(city.Name, font, style.FontPx, style.Color);
            label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
            label.AddThemeConstantOverride("shadow_offset_y", 1);
            label.AddThemeConstantOverride("shadow_outline_size", 2);
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.5f));
            label.AddThemeConstantOverride("outline_size", 2);
            label.Visible = false;
            AddChild(label);
            var intel = new RichTextLabel
            {
                BbcodeEnabled = true, FitContent = true, ScrollActive = false,
                MouseFilter = MouseFilterEnum.Ignore, Visible = false,
                CustomMinimumSize = new Vector2(Config.CityIntelBoxPx, 0),
            };
            intel.AddThemeFontOverride("normal_font", Fonts.Ui);
            intel.AddThemeFontSizeOverride("normal_font_size", Config.CityIntelFontPx);
            intel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
            intel.AddThemeConstantOverride("shadow_offset_y", 1);
            intel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.5f));
            intel.AddThemeConstantOverride("outline_size", 2);
            AddChild(intel);
            entries.Add(new Entry
            {
                City = city, Label = label, Intel = intel,
                HalfWidthPx = font.GetStringSize(city.Name, HorizontalAlignment.Left, -1, style.FontPx).X / 2,
                HeightPx = style.FontPx * 1.3f,
                MinZoom = style.MinZoom,
                Color = style.Color,
            });
        }
    }

    // What the player knows about a city's prices, one line per product. Now
    // that every market's price is its own (distance from the producers sets
    // where it rests, live stock swings it around that), listing all nine
    // would bury the label — so a discovered city shows only the products
    // furthest from what they fetch across the world (WorldPrices.Of),
    // most extreme first, and only those past CityIntelMinDeviation. A discovered supply shock always shows,
    // with its days left, even where the city itself was never visited. Call
    // when knowledge or the events change (popup opened, midnight pass), not
    // per frame.
    public void RefreshIntel()
    {
        var terms = Config.Market;
        foreach (var e in entries)
        {
            var lines = new List<string>();
            if (debugPrices)
            {
                // Everything, discovered or not: price, % of the world
                // average, (P) where this city makes the product.
                float widest = 0;
                foreach (var id in e.City.Products)
                {
                    var product = GameData.ProductById(id);
                    if (product == null) continue;
                    if (debugProduct >= 0 && product != GameData.Products[debugProduct]) continue;
                    var q = Market.QuoteFor(e.City, product, terms);
                    var read = Market.Read(q.SellPrice, world.Of(product));
                    var text = $"{product.Name} {q.SellPrice} ({read.Percent}%)" + (q.Produces ? " (P)" : "");
                    widest = Math.Max(widest, Fonts.Ui.GetStringSize(text, HorizontalAlignment.Left, -1, Config.CityIntelFontPx).X);
                    lines.Add($"[center]{text}[/center]");
                }
                StyleIntel(e.Intel, debug: true);
                // The panel hugs the text: as wide as the widest line (plus
                // padding), as tall as the rows actually shown.
                e.IntelWidthPx = widest + 2 * DebugPanelPadX + 2;
                e.Intel.CustomMinimumSize = new Vector2(e.IntelWidthPx, 0);
                e.HasIntel = lines.Count > 0;
                e.Intel.Text = string.Join("\n", lines);
                e.Intel.ResetSize();
                if (!e.HasIntel) e.Intel.Visible = false;
                continue;
            }
            var scored = new List<(double Off, string Line)>();
            foreach (var id in e.City.Products)
            {
                var product = GameData.ProductById(id);
                if (product == null) continue;
                var ev = storyEvents.PriceEventFor(e.City, id);
                bool known = ev != null && ev.Discovered;
                if (!known && !(e.City.Discovered && ev == null)) continue;
                var q = Market.QuoteFor(e.City, product, terms);
                var read = Market.Read(q.SellPrice, world.Of(product));
                double off = Math.Abs(read.Percent / 100.0 - 1);
                if (!known && off < Config.CityIntelMinDeviation) continue;
                var color = read.Dear ? Config.MarketDearColor : Config.MarketCheapColor;
                var tail = known ? $" {ev!.DaysLeftAt(time.TotalMinutes)}d" : "";
                scored.Add((known ? double.MaxValue : off,
                    $"[center][color=#{color.ToHtml(false)}]{product.Name} {q.SellPrice} {(read.Dear ? "▲" : "▼")}{tail}[/color][/center]"));
            }
            StyleIntel(e.Intel, debug: false);
            e.IntelWidthPx = Config.CityIntelBoxPx;
            e.Intel.CustomMinimumSize = new Vector2(e.IntelWidthPx, 0);
            e.Intel.ResetSize();
            scored.Sort((a, b) => b.Off.CompareTo(a.Off));
            for (int i = 0; i < scored.Count && i < Config.CityIntelMaxLines; i++) lines.Add(scored[i].Line);
            // Under the price intel: a debt tag, so you know from the deck
            // that this city's bank is waiting for its money.
            if (loans.LoanAt(e.City) is Loan loan)
            {
                int left = loan.DaysLeft(Days.Of(time.TotalMinutes));
                var debtColor = left >= 0 ? Config.BankDebtColor : Config.LowColor;
                lines.Add($"[center][color=#{debtColor.ToHtml(false)}]◆ OWE {Loans.ShortAmount(loan.Owed)} " +
                          (left >= 0 ? $"({left}d)" : $"({-left}d LATE)") + "[/color][/center]");
            }
            // Passengers aboard bound for this city, so sailing past is a
            // reminder to put in and land them.
            int today = Days.Of(time.TotalMinutes);
            int vips = 0;
            foreach (var vip in player.VipPassengers) if (vip.Destination == e.City) vips++;
            if (vips > 0)
                lines.Add(Tag("#7fe3ff", vips == 1 ? "▲ VIP TO LAND HERE" : $"▲ {vips} VIPs TO LAND HERE"));
            if (religion.MonkFor(e.City) is MonkMission monk)
                lines.Add(Tag($"#{Config.PietyColor.ToHtml(false)}",
                    $"✦ MONK TO LAND HERE ({monk.DaysLeft(today)}d)"));
            // A monastery is a landmark: it shows even on a city the player
            // has never called at, so it can be spotted from the deck.
            if (religion.HasMonastery(e.City))
                lines.Add(Tag($"#{Config.PietyColor.ToHtml(false)}", "✦ MONASTERY"));
            e.HasIntel = lines.Count > 0;
            e.Intel.Text = string.Join("\n", lines);
            if (!e.HasIntel) e.Intel.Visible = false;
        }
    }

    // The debug listing is plain white on a translucent black panel, no
    // outline or shadow — easier to read than the coloured intel lines.
    private const float DebugPanelPadX = 6, DebugPanelPadY = 3;

    private static void StyleIntel(RichTextLabel intel, bool debug)
    {
        intel.AddThemeColorOverride("default_color", Colors.White);
        intel.AddThemeConstantOverride("outline_size", debug ? 0 : 2);
        intel.AddThemeConstantOverride("shadow_offset_y", debug ? 0 : 1);
        intel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, debug ? 0f : 0.9f));
        if (debug) intel.AddThemeStyleboxOverride("normal", Fonts.Panel(new Color(0, 0, 0, 0.6f), 3, DebugPanelPadX, DebugPanelPadY));
        else intel.RemoveThemeStyleboxOverride("normal");
    }

    // P: flip the debug price listing on or off and redraw every label.
    public void ToggleDebugPrices()
    {
        debugPrices = !debugPrices;
        debugProduct = -1;
        RefreshIntel();
    }

    // [ / ] while the debug listing is up: the first press narrows it to a
    // single product, later presses step through the catalogue (wrapping).
    public void StepDebugProduct(int dir)
    {
        if (!debugPrices || GameData.Products.Count == 0) return;
        int n = GameData.Products.Count;
        debugProduct = debugProduct < 0 ? (dir > 0 ? 0 : n - 1) : ((debugProduct + dir) % n + n) % n;
        RefreshIntel();
    }

    private static string Tag(string colorHtml, string text) =>
        $"[center][color={colorHtml}]{text}[/color][/center]";

    private void SetHovered(Entry e, bool hovered)
    {
        if (e.Hovered == hovered) return;
        e.Hovered = hovered;
        Fonts.SetColor(e.Label, hovered ? Config.CityHoverColor : e.Color);
    }

    /// Positions visible labels and returns the city under the cursor, if any.
    public City? Update(CameraState state, Vector2 view, Vector2 cursorPx, CameraRig rig)
    {
        const float margin = 200; // px of slack so labels don't pop at the edges
        float cityTop = Config.TerrainHeights[TerrainMap.City];
        Entry? best = null;
        float bestDistance = float.PositiveInfinity;

        foreach (var e in entries)
        {
            bool onScreen = rig.WorldToScreen(e.City.X, e.City.Y, cityTop, out var p);
            bool visible = onScreen && state.Zoom >= e.MinZoom &&
                p.X > -margin && p.X < view.X + margin && p.Y > -margin && p.Y < view.Y + margin;
            if (visible != e.Visible)
            {
                e.Label.Visible = visible;
                e.Visible = visible;
            }
            if (e.Intel.Visible != (visible && e.HasIntel)) e.Intel.Visible = visible && e.HasIntel;
            if (!visible) { SetHovered(e, false); continue; }

            e.Screen = p;
            e.Label.Position = new Vector2(p.X - e.HalfWidthPx, p.Y + Config.CityLabelOffsetPx);
            e.Intel.Position = new Vector2(p.X - e.IntelWidthPx / 2, p.Y + Config.CityLabelOffsetPx + e.HeightPx);

            float dx = cursorPx.X - p.X, dy = cursorPx.Y - p.Y;
            bool inCircle = dx * dx + dy * dy <= Config.CityHitRadiusPx * Config.CityHitRadiusPx;
            bool inLabel = Mathf.Abs(dx) <= e.HalfWidthPx + Config.CityHitPadPx &&
                dy >= Config.CityLabelOffsetPx - Config.CityHitPadPx &&
                dy <= Config.CityLabelOffsetPx + e.HeightPx + Config.CityHitPadPx;
            if (inCircle || inLabel)
            {
                float distance = dx * dx + dy * dy;
                if (distance < bestDistance) { bestDistance = distance; best = e; }
            }
        }
        foreach (var e in entries) SetHovered(e, e == best);
        return best?.City;
    }
}
