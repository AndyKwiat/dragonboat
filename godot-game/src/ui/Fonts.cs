using Godot;

// System fonts standing in for the web version's `monospace` and
// `system-ui` stacks, plus small helpers for the styling the DOM did in CSS.
public static class Fonts
{
    private static readonly string[] MonoNames = { "Menlo", "Monaco", "Consolas", "Courier New", "monospace" };
    private static readonly string[] UiNames = { "Helvetica Neue", "Helvetica", "Arial", "sans-serif" };

    // Glyphs placed at quarter-pixel positions and unhinted, so text that
    // tracks the moving world (city labels) glides instead of shimmering —
    // the same as browser text on macOS.
    private static SystemFont Make(string[] names, int weight) => new()
    {
        FontNames = names,
        FontWeight = weight,
        SubpixelPositioning = TextServer.SubpixelPositioning.OneQuarter,
        Hinting = TextServer.Hinting.None,
    };

    public static readonly SystemFont Mono = Make(MonoNames, 400);
    public static readonly SystemFont MonoBold = Make(MonoNames, 700);
    public static readonly SystemFont Ui = Make(UiNames, 400);
    public static readonly SystemFont UiBold = Make(UiNames, 700);
    public static readonly SystemFont UiSemiBold = Make(UiNames, 600);

    public static Label MakeLabel(string text, Font font, int sizePx, Color color)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", sizePx);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    public static void SetColor(Label l, Color c) => l.AddThemeColorOverride("font_color", c);

    // rgba(0,0,0,.55) box with rounded corners and padding, like the overlays.
    public static StyleBoxFlat Panel(Color bg, float radius, float padX, float padY, Color? border = null)
    {
        var sb = new StyleBoxFlat { BgColor = bg };
        sb.SetCornerRadiusAll((int)radius);
        sb.ContentMarginLeft = padX; sb.ContentMarginRight = padX;
        sb.ContentMarginTop = padY; sb.ContentMarginBottom = padY;
        if (border is Color b) { sb.BorderColor = b; sb.SetBorderWidthAll(1); }
        return sb;
    }

    // Panel furniture shared by the popups (city market, bank).
    public static Control Spacer(float h) =>
        new Control { CustomMinimumSize = new Vector2(0, h), MouseFilter = Control.MouseFilterEnum.Ignore };

    public static Control Rule(Color c) =>
        new ColorRect { Color = c, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore };

    // A section heading: some air, the title, a hairline.
    public static Control Heading(string text)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 0);
        box.AddChild(Spacer(18));
        box.AddChild(MakeLabel(text, UiSemiBold, Config.PopupFontPx + 4, Colors.White));
        box.AddChild(Spacer(6));
        box.AddChild(Rule(new Color("#444444")));
        box.AddChild(Spacer(8));
        return box;
    }

    // The popups' flat dark button.
    public static Button MakeButton(string label, System.Action onClick, float minWidth = 58)
    {
        var b = new Button { Text = label, CustomMinimumSize = new Vector2(minWidth, 0) };
        var normal = Panel(new Color("#23262c"), 4, 10, 4, new Color("#666666"));
        var hover = Panel(new Color("#2b2f36"), 4, 10, 4, new Color("#777777"));
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", normal);
        b.AddThemeStyleboxOverride("disabled", normal);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeFontOverride("font", Ui);
        b.AddThemeFontSizeOverride("font_size", Config.PopupFontPx - 2);
        b.AddThemeColorOverride("font_color", new Color("#eeeeee"));
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_pressed_color", Colors.White);
        b.AddThemeColorOverride("font_disabled_color", new Color("#eeeeee"));
        b.Pressed += onClick;
        return b;
    }

    // A disabled button is dimmed and stops offering the hand cursor.
    public static void SetEnabled(Button b, bool enabled)
    {
        b.Disabled = !enabled;
        b.Modulate = new Color(1, 1, 1, enabled ? 1f : 0.35f);
        b.MouseDefaultCursorShape = enabled ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow;
    }
}
