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
}
