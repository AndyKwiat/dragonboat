using Godot;

// Top-centre: the sail state (SAILS / OARS, SPACE toggles) and how much
// pull the rowers have left (the wind is the bottom-left dial's job). The bar fills green and empties towards red;
// once spent it flashes, which is all that being spent costs for now.
// Rules: boat/Sailing.cs (CrewStamina); layout notes in docs/ui.md.
public partial class StaminaBar : PanelContainer
{
    private Label caption = null!;
    private ColorRect track = null!, fill = null!;
    private CrewStamina crew = null!;
    private float flashPhase;

    public void Init(CrewStamina crew)
    {
        this.crew = crew;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", Fonts.Panel(new Color(0, 0, 0, 0.55f), 3, 10, 6));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        caption = Fonts.MakeLabel("", Fonts.MonoBold, Config.StaminaFontPx, Config.DayColor);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(caption);
        // The bar: a dark track with the fill drawn over its left edge.
        track = new ColorRect
        {
            Color = Config.StaminaTrackColor,
            CustomMinimumSize = new Vector2(Config.StaminaBarPx.X, Config.StaminaBarPx.Y),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        fill = new ColorRect { Color = Config.StaminaFullColor, MouseFilter = MouseFilterEnum.Ignore };
        track.AddChild(fill);
        box.AddChild(track);
        AddChild(box);
    }

    public void Update(float dt, Vector2 view, bool sailsUp)
    {
        float level = crew.Level;
        string state = sailsUp ? "SAILS UP" : crew.Rowing ? "OARS - ROWING" : "OARS - RESTING";
        caption.Text = $"{state} [SPACE]   CREW {Mathf.RoundToInt(level * 100)}%";
        fill.Size = new Vector2(Config.StaminaBarPx.X * level, Config.StaminaBarPx.Y);
        // Empty: flash the (empty) bar's track so it can't be missed.
        flashPhase += dt * Config.StaminaFlashRadPerSec;
        if (crew.Exhausted)
        {
            float t = 0.5f + 0.5f * Mathf.Sin(flashPhase);
            track.Color = Config.StaminaTrackColor.Lerp(Config.StaminaEmptyColor, t);
        }
        else
        {
            track.Color = Config.StaminaTrackColor;
            fill.Color = Config.StaminaEmptyColor.Lerp(Config.StaminaFullColor, level);
        }
        Position = new Vector2(Mathf.Round((view.X - Size.X) / 2), 8);
    }
}
