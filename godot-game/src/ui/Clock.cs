using Godot;

// Top-left overlay: the game date and time, then what drains as it passes —
// rice, purse, honour, boat, cargo. Dims at night; rice goes red when low,
// money when in debt, honour when negative.
public partial class Clock : PanelContainer
{
    private Label stamp = null!, rice = null!, money = null!, honour = null!, piety = null!, crew = null!, cargo = null!,
        vips = null!, monks = null!, debts = null!;
    private GameTime time = null!;
    private Rations rations = null!;
    private Player player = null!;
    private Loans loans = null!;
    private Monasteries religion = null!;
    private int lastLoanVersion = -1, lastLoanDay = int.MinValue;
    private bool? lastNight, lastLowRice, lastInDebt, lastDishonoured, lastImpious;

    public void Init(GameTime time, Rations rations, Player player, Loans loans, Monasteries religion)
    {
        this.time = time; this.rations = rations; this.player = player; this.loans = loans;
        this.religion = religion;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", Fonts.Panel(new Color(0, 0, 0, 0.55f), 3, 10, 6));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        int px = Config.ClockFontPx;
        stamp = Fonts.MakeLabel("", Fonts.MonoBold, px, Config.DayColor);
        rice = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, Config.DayColor);
        money = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, Config.DayColor);
        honour = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, Config.DayColor);
        piety = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, Config.DayColor);
        crew = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, Config.DayColor);
        cargo = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, Config.DayColor);
        // One line per VIP aboard: where they are bound, what landing them
        // today is worth and how long before they jump ship.
        vips = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, new Color("#7fe3ff"));
        vips.Visible = false;
        // One line per outstanding bank loan, under the VIPs: what is owed
        // (interest included), where, and how long is left.
        debts = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, Config.BankDebtColor);
        debts.Visible = false;
        // One line per monk aboard, under the VIPs: where he must be landed
        // and how long is left.
        monks = Fonts.MakeLabel("", Fonts.MonoBold, px - 4, Config.PietyColor);
        monks.Visible = false;
        foreach (var l in new[] { stamp, rice, money, honour, piety, crew, cargo, vips, monks, debts }) box.AddChild(l);
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
        bool impious = player.Piety < 0;

        SetText(stamp, time.Format());
        SetText(rice, $"rice  {units}");
        SetText(money, $"money {player.Money}");
        SetText(honour, $"honour {player.Honour}");
        SetText(piety, $"piety {player.Piety}");
        // The boat is the crew and the hold both: her class names the line.
        SetText(crew, $"boat  {player.Boat.Name} ({player.CrewCount} crew)");
        SetText(cargo, $"cargo {player.TotalUnits()}/{player.CargoCapacity()}");
        var lines = "";
        foreach (var vip in player.VipPassengers)
        {
            int days = vip.DaysAboard(Days.Of(time.TotalMinutes));
            lines += (lines == "" ? "" : "\n") +
                $"VIP delivery - {vip.Destination.Name} (+{Vips.SpeedHonourFor(days, Config.Vips) + Config.VipDeliveryHonour}, " +
                $"{Config.Vips.AbandonDays - days}d left)";
        }
        SetText(vips, lines);
        vips.Visible = lines != "";

        var monkLines = "";
        foreach (var monk in religion.Aboard)
            monkLines += (monkLines == "" ? "" : "\n") +
                $"MONK delivery - {monk.Destination.Name} (due in {monk.DaysLeft(Days.Of(time.TotalMinutes))}d)";
        SetText(monks, monkLines);
        monks.Visible = monkLines != "";

        // Loans only change at a repayment, a new loan or a midnight, so the
        // lines are rebuilt on those, not every frame.
        int today = Days.Of(time.TotalMinutes);
        if (loans.Version != lastLoanVersion || today != lastLoanDay)
        {
            lastLoanVersion = loans.Version; lastLoanDay = today;
            var debtLines = "";
            bool overdue = false;
            foreach (var loan in loans.All)
            {
                int left = loan.DaysLeft(today);
                if (left < 0) overdue = true;
                debtLines += (debtLines == "" ? "" : "\n") +
                    $"BANK LOAN {Loans.ShortAmount(loan.Owed)} - {loan.City.Name.ToUpperInvariant()} " +
                    (left >= 0 ? $"(due in {left}d)" : $"({-left}d OVERDUE)");
            }
            SetText(debts, debtLines);
            debts.Visible = debtLines != "";
            Fonts.SetColor(debts, overdue ? Config.LowColor : Config.BankDebtColor);
        }

        if (night != lastNight || lowRice != lastLowRice || inDebt != lastInDebt
            || dishonoured != lastDishonoured || impious != lastImpious)
        {
            var normal = night ? Config.NightColor : Config.DayColor;
            Fonts.SetColor(stamp, normal);
            Fonts.SetColor(crew, normal);
            Fonts.SetColor(cargo, normal);
            // A warning outranks the night dimming.
            Fonts.SetColor(rice, lowRice ? Config.LowColor : normal);
            Fonts.SetColor(money, inDebt ? Config.LowColor : normal);
            Fonts.SetColor(honour, dishonoured ? Config.LowColor : normal);
            Fonts.SetColor(piety, impious ? Config.LowColor : normal);
            lastNight = night; lastLowRice = lowRice; lastInDebt = inDebt;
            lastDishonoured = dishonoured; lastImpious = impious;
        }
    }

    private static void SetText(Label l, string t) { if (l.Text != t) l.Text = t; }
}
