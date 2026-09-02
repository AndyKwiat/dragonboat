using Godot;

// Top-centre, right under the oars bar: how the crew feel about all this.
// The bar fills amber and sours towards red as it empties, and says what it
// is costing — under half morale the crew work the boat slower.
// Rules: crew/Morale.cs; layout notes in docs/ui.md.
public partial class MoraleBar : PanelContainer
{
    private Label caption = null!;
    private ColorRect track = null!, fill = null!;
    private CrewMorale morale = null!;

    public void Init(CrewMorale morale)
    {
        this.morale = morale;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", Fonts.Panel(new Color(0, 0, 0, 0.55f), 3, 10, 6));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        caption = Fonts.MakeLabel("", Fonts.MonoBold, Config.StaminaFontPx, Config.DayColor);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        // Clipped, so the caption's own length can't make this panel a
        // different width from the stamina bar it hangs under.
        caption.ClipText = true;
        box.AddChild(caption);
        track = new ColorRect
        {
            Color = Config.StaminaTrackColor,
            CustomMinimumSize = new Vector2(Config.StaminaBarPx.X, Config.StaminaBarPx.Y),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        fill = new ColorRect { Color = Config.MoraleFullColor, MouseFilter = MouseFilterEnum.Ignore };
        track.AddChild(fill);
        box.AddChild(track);
        AddChild(box);
    }

    // `below` is the bottom edge of the stamina bar and `width` its width:
    // this bar hangs off it, and the two panels line up.
    public void Update(Vector2 view, float below, float width)
    {
        CustomMinimumSize = new Vector2(width, 0);
        float level = morale.Level;
        float speed = morale.Speed;
        caption.Text = speed < 1
            ? $"MORALE {Mathf.RoundToInt(level * 100)}%   SPEED {Mathf.RoundToInt(speed * 100)}%"
            : $"MORALE {Mathf.RoundToInt(level * 100)}%";
        fill.Size = new Vector2(Config.StaminaBarPx.X * level, Config.StaminaBarPx.Y);
        fill.Color = Config.MoraleLowColor.Lerp(Config.MoraleFullColor, level);
        Fonts.SetColor(caption, speed < 1 ? Config.MoraleLowColor : Config.DayColor);
        Position = new Vector2(Mathf.Round((view.X - Size.X) / 2), below + Config.MoraleBarGapPx);
    }
}
