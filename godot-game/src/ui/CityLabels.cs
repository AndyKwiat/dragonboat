using Godot;
using System.Collections.Generic;

// Overlay drawing a name label for every city on screen, plus the
// hit-testing for hover/click: a hit is "within CityHitRadiusPx of the city
// centre, OR inside the label box padded by CityHitPadPx". Labels hang off
// the top of the city block. Under the name, discovered price intel: one
// line per product the player knows trades off base price (see RefreshIntel).
public partial class CityLabels : Control
{
    private sealed class Entry
    {
        public City City = null!;
        public Label Label = null!;
        public RichTextLabel Intel = null!;
        public bool HasIntel;
        public float HalfWidthPx, HeightPx, MinZoom;
        public bool Visible, Hovered;
        public Vector2 Screen;
        public Color Color;
    }

    private readonly List<Entry> entries = new();
    private StoryEvents storyEvents = null!;
    private GameTime time = null!;
    private Loans loans = null!;

    public void Init(List<City> cities, StoryEvents storyEvents, GameTime time, Loans loans)
    {
        this.storyEvents = storyEvents; this.time = time; this.loans = loans;
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

    // What the player knows about a city's prices, one line per product:
    // a discovered story event shows its price and days left; otherwise a
    // discovered city shows its permanent off-base (structural) prices. An
    // undiscovered event on a discovered city stays hidden — the player
    // hasn't seen that market since it started. Call when knowledge or the
    // events change (popup opened, midnight pass), not per frame.
    public void RefreshIntel()
    {
        foreach (var e in entries)
        {
            var lines = new List<string>();
            foreach (var id in e.City.Products)
            {
                var product = GameData.ProductById(id);
                if (product == null) continue;
                var ev = storyEvents.PriceEventFor(e.City, id);
                Quote q;
                string tail = "";
                if (ev != null && ev.Discovered)
                {
                    q = Market.QuoteFor(e.City, product, ev);
                    tail = $" {ev.DaysLeftAt(time.TotalMinutes)}d";
                }
                else if (e.City.Discovered && ev == null)
                {
                    q = Market.QuoteFor(e.City, product);
                    if (q.SellPrice == product.BasePrice) continue;
                }
                else continue;
                bool dear = q.SellPrice > product.BasePrice;
                var color = dear ? Config.MarketDearColor : Config.MarketCheapColor;
                lines.Add($"[center][color=#{color.ToHtml(false)}]{product.Name} {q.SellPrice} {(dear ? "▲" : "▼")}{tail}[/color][/center]");
            }
            // Under the price intel: a debt tag, so you know from the deck
            // that this city's bank is waiting for its money.
            if (loans.LoanAt(e.City) is Loan loan)
            {
                int left = loan.DaysLeft(Loans.DayOf(time.TotalMinutes));
                var debtColor = left >= 0 ? Config.BankDebtColor : Config.LowColor;
                lines.Add($"[center][color=#{debtColor.ToHtml(false)}]◆ OWE {Loans.ShortAmount(loan.Owed)} " +
                          (left >= 0 ? $"({left}d)" : $"({-left}d LATE)") + "[/color][/center]");
            }
            e.HasIntel = lines.Count > 0;
            e.Intel.Text = string.Join("\n", lines);
            if (!e.HasIntel) e.Intel.Visible = false;
        }
    }

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
            e.Intel.Position = new Vector2(p.X - Config.CityIntelBoxPx / 2, p.Y + Config.CityLabelOffsetPx + e.HeightPx);

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
