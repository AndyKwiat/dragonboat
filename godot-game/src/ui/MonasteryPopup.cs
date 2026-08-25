using Godot;
using System;

// The monastery window: a second popup over the city market panel, the same
// shape as the bank's (small cities with a house — see Monasteries). Give to
// the poor for piety, once a week per house, and take the brothers' errand
// carrying a monk to a large city. Rules: src/religion/Monasteries.cs.
public partial class MonasteryPopup : PanelContainer
{
    private Player player = null!;
    private GameTime time = null!;
    private Monasteries religion = null!;
    private VBoxContainer content = null!;
    private Label title = null!, creed = null!, almsStatus = null!, amount = null!, errand = null!, purse = null!;
    private HSlider slider = null!;
    private Control almsRow = null!;
    private Button giveButton = null!, acceptButton = null!;
    private Monastery? current;
    private int staged;
    private int layoutFrames;
    private Vector2 lastView;

    public Action<string>? Announce;
    public Action? Changed;

    public bool IsOpen => Visible;

    public void Init(Player player, GameTime time, Monasteries religion)
    {
        this.player = player; this.time = time; this.religion = religion;
        int px = Config.PopupFontPx;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", Fonts.Panel(new Color(14 / 255f, 12 / 255f, 20 / 255f, 0.98f), 8, 24, 20, new Color("#8f7fc0")));

        content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 0);
        AddChild(content);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        title = Fonts.MakeLabel("", Fonts.UiBold, Config.PopupTitleFontPx - 4, Config.PietyColor);
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
        content.AddChild(Fonts.Spacer(10));

        creed = Fonts.MakeLabel("", Fonts.Mono, px - 3, new Color("#aaaaaa"));
        creed.AutowrapMode = TextServer.AutowrapMode.Word;
        creed.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.AddChild(creed);

        content.AddChild(Fonts.Heading("Alms for the poor"));
        almsStatus = Fonts.MakeLabel("", Fonts.MonoBold, px, new Color("#dddddd"));
        almsStatus.AutowrapMode = TextServer.AutowrapMode.Word;
        almsStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.AddChild(almsStatus);

        content.AddChild(Fonts.Spacer(14));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        slider = new HSlider
        {
            MinValue = 0, MaxValue = 1, Step = 0.001, Value = 0,
            CustomMinimumSize = new Vector2(200, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        slider.ValueChanged += _ => { Stage(); Refresh(); };
        amount = Fonts.MakeLabel("", Fonts.MonoBold, px, Config.PietyColor);
        amount.CustomMinimumSize = new Vector2(200, 0);
        amount.HorizontalAlignment = HorizontalAlignment.Right;
        giveButton = Fonts.MakeButton("Give", Give, 80);
        row.AddChild(slider);
        row.AddChild(amount);
        row.AddChild(giveButton);
        almsRow = row;
        content.AddChild(almsRow);

        content.AddChild(Fonts.Heading("The brothers' errand"));
        var errandRow = new HBoxContainer();
        errandRow.AddThemeConstantOverride("separation", 14);
        errand = Fonts.MakeLabel("", Fonts.Ui, px - 1, new Color("#dddddd"));
        errand.AutowrapMode = TextServer.AutowrapMode.Word;
        errand.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        acceptButton = Fonts.MakeButton("Accept", Accept, 80);
        errandRow.AddChild(errand);
        errandRow.AddChild(acceptButton);
        content.AddChild(errandRow);

        content.AddChild(Fonts.Spacer(16));
        content.AddChild(Fonts.Rule(new Color("#444444")));
        content.AddChild(Fonts.Spacer(10));
        purse = Fonts.MakeLabel("", Fonts.MonoBold, px, new Color("#ffd166"));
        content.AddChild(purse);
    }

    private int MaxGift => religion.MaxGift(player.Money);

    // The slider's fraction as a whole GiveStep between the smallest gift
    // and what the purse can bear.
    private void Stage()
    {
        int max = MaxGift, min = Config.Religion.GiveMin, step = Config.Religion.GiveStep;
        if (max < min) { staged = 0; return; }
        staged = min + (int)Math.Round(slider.Value * (max - min) / step) * step;
        staged = Math.Min(staged, max);
    }

    private void Give()
    {
        if (current == null || staged < Config.Religion.GiveMin || player.Money < staged) return;
        int piety = religion.Give(current, staged, Days.Of(time.TotalMinutes));
        if (piety <= 0) return;
        player.Money -= staged;
        player.Piety += piety;
        Announce?.Invoke($"Gave {staged} to the poor of {current.City.Name} — piety +{piety}");
        Changed?.Invoke();
        Refresh();
    }

    private void Accept()
    {
        if (current == null) return;
        var monk = religion.Accept(current, Days.Of(time.TotalMinutes));
        if (monk == null) return;
        Announce?.Invoke($"A monk of {monk.From.Name} is aboard, bound for {monk.Destination.Name} " +
                         $"— {Config.Religion.MonkTermDays} days to land him");
        Changed?.Invoke();
        Refresh();
    }

    public void Refresh()
    {
        if (current == null) return;
        var t = Config.Religion;
        int today = Days.Of(time.TotalMinutes);
        creed.Text =
            $"A house of monks. Alms of {t.GiveMin}–{t.GiveMax} buy piety worth a {t.GivePietyDivisor}th of the gift, " +
            $"and each house will take them only once every {t.GiveCooldownDays} days.";

        int wait = current.DaysUntilGiveAllowed(today, t);
        bool canGive = wait == 0;
        almsRow.Visible = canGive;
        if (!canGive)
        {
            almsStatus.Text = $"you gave here already — the poor will take no more for {wait} day{(wait == 1 ? "" : "s")}";
            Fonts.SetColor(almsStatus, new Color("#888888"));
        }
        else if (MaxGift < t.GiveMin)
        {
            almsStatus.Text = $"the smallest gift the brothers will take is {t.GiveMin} — your purse is too light";
            Fonts.SetColor(almsStatus, new Color("#888888"));
        }
        else
        {
            almsStatus.Text = $"the poor of {current.City.Name} will take a gift today";
            Fonts.SetColor(almsStatus, new Color("#dddddd"));
        }
        bool offerable = canGive && MaxGift >= t.GiveMin;
        amount.Text = offerable ? $"give {staged} → piety +{religion.PietyFor(staged)}" : "nothing to give";
        Fonts.SetEnabled(giveButton, offerable && staged >= t.GiveMin);
        giveButton.TooltipText = offerable
            ? $"Give {staged} to the poor for {religion.PietyFor(staged)} piety"
            : "";

        if (current.Errand is MonkMission offer)
        {
            errand.Text = $"A monk seeks passage to {offer.Destination.Name} — {t.MonkPiety} piety if he lands " +
                          $"within {t.MonkTermDays} days, {t.MonkPenalty} piety lost if he does not.";
            Fonts.SetColor(errand, Config.PietyColor);
            acceptButton.Visible = true;
            Fonts.SetEnabled(acceptButton, true);
        }
        else
        {
            var carrying = CarryingFromHere();
            errand.Text = carrying != null
                ? $"Their brother is already aboard, bound for {carrying.Destination.Name} — " +
                  $"{carrying.DaysLeft(today)} day{(carrying.DaysLeft(today) == 1 ? "" : "s")} left to land him."
                : "The brothers have no errand for you today.";
            Fonts.SetColor(errand, new Color("#888888"));
            acceptButton.Visible = false;
        }
        purse.Text = $"money {player.Money}    piety {player.Piety}";
        layoutFrames = 3;
    }

    private MonkMission? CarryingFromHere()
    {
        if (current == null) return null;
        foreach (var m in religion.Aboard) if (m.From == current.City) return m;
        return null;
    }

    public void Show(City city)
    {
        if (religion.At(city) is not Monastery house) return;
        current = house;
        title.Text = $"Monastery of {city.Name}";
        staged = Config.Religion.GiveMin;
        slider.SetValueNoSignal(0);
        Refresh();
        Visible = true;
        layoutFrames = 3;
    }

    public new void Hide()
    {
        Visible = false;
        current = null;
    }

    public void Update(Vector2 view)
    {
        if (!Visible) return;
        if (view != lastView) { lastView = view; layoutFrames = 3; }
        if (layoutFrames <= 0) return;
        layoutFrames--;
        float padX = 24, padY = 20;
        float w = Mathf.Min(Config.BankPopupWidthPx, view.X - 40);
        content.CustomMinimumSize = new Vector2(w - 2 * padX, 0);
        float h = Mathf.Min(content.GetCombinedMinimumSize().Y + 2 * padY, view.Y - 40);
        Size = new Vector2(w, h);
        Position = new Vector2((view.X - w) / 2, (view.Y - h) / 2);
    }
}
