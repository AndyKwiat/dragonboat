using Godot;

// System fonts standing in for the web version's `monospace` and
// `system-ui` stacks, plus small helpers for the styling the DOM did in CSS.
public static class Fonts
{
    public static readonly SystemFont Mono = new()
    {
        FontNames = new[] { "Menlo", "Monaco", "Consolas", "Courier New", "monospace" },
    };
    public static readonly SystemFont MonoBold = new()
    {
        FontNames = new[] { "Menlo", "Monaco", "Consolas", "Courier New", "monospace" },
        FontWeight = 700,
    };
    public static readonly SystemFont Ui = new()
    {
        FontNames = new[] { "Helvetica Neue", "Helvetica", "Arial", "sans-serif" },
    };
    public static readonly SystemFont UiBold = new()
    {
        FontNames = new[] { "Helvetica Neue", "Helvetica", "Arial", "sans-serif" },
        FontWeight = 700,
    };
    public static readonly SystemFont UiSemiBold = new()
    {
        FontNames = new[] { "Helvetica Neue", "Helvetica", "Arial", "sans-serif" },
        FontWeight = 600,
    };

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
