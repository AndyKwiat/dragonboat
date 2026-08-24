using Godot;

// Top-left overlay: the game date and time, then what drains as it passes —
// rice, purse, honour, crew, cargo. Dims at night; rice goes red when low,
// money when in debt, honour when negative.
public partial class Clock : PanelContainer
{
    private Label stamp = null!, rice = null!, money = null!, honour = null!, crew = null!, cargo = null!, vips = null!;
    private GameTime time = null!;
    private Rations rations = null!;
    private Player player = null!;
    private bool? lastNight, lastLowRice, lastInDebt, lastDishonoured;

    public void Init(GameTime time, Rations rations, Player player)
    {
        this.time = time; this.rations = rations; this.player = player;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", Fonts.Panel(new Color(0, 0, 0, 0.55f), 3, 10, 6));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        int px = Config.ClockFontPx;
        stamp = Fonts.MakeLabel("", Fonts.MonoBold, px, Config.DayColor);
        rice = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, Config.DayColor);
        money = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, Config.DayColor);
        honour = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, Config.DayColor);
        crew = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, Config.DayColor);
        cargo = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, Config.DayColor);
        // One line per VIP aboard: "VIP delivery - <city>".
        vips = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, new Color("#7fe3ff"));
        vips.Visible = false;
        foreach (var l in new[] { stamp, rice, money, honour, crew, cargo, vips }) box.AddChild(l);
        AddChild(box);
        Position = new Vector2(8, 8);
    }

    public void Update()
    {
        bool night = time.IsNight;
        int units = rations.Remaining;
        bool lowRice = units < Config.RationsLowUnits;
        bool inDebt = player.Money < 0;
        bool dishonoured = player.Honour < 0;

        SetText(stamp, time.Format());
        SetText(rice, $"rice  {units}");
        SetText(money, $"money {player.Money}");
        SetText(honour, $"honour {player.Honour}");
        SetText(crew, $"crew  {player.CrewCount}");
        SetText(cargo, $"cargo {player.TotalUnits()}/{player.CargoCapacity()}");
        var lines = "";
        foreach (var dest in player.VipPassengers)
            lines += (lines == "" ? "" : "\n") + $"VIP delivery - {dest.Name}";
        SetText(vips, lines);
        vips.Visible = lines != "";

        if (night != lastNight || lowRice != lastLowRice || inDebt != lastInDebt || dishonoured != lastDishonoured)
        {
            var normal = night ? Config.NightColor : Config.DayColor;
            Fonts.SetColor(stamp, normal);
            Fonts.SetColor(crew, normal);
            Fonts.SetColor(cargo, normal);
            // A warning outranks the night dimming.
            Fonts.SetColor(rice, lowRice ? Config.LowColor : normal);
            Fonts.SetColor(money, inDebt ? Config.LowColor : normal);
            Fonts.SetColor(honour, dishonoured ? Config.LowColor : normal);
            lastNight = night; lastLowRice = lowRice; lastInDebt = inDebt; lastDishonoured = dishonoured;
        }
    }

    private static void SetText(Label l, string t) { if (l.Text != t) l.Text = t; }
}
