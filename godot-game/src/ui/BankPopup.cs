using Godot;
using System;

// The bank window: a second popup, centred over the city market panel it was
// opened from (medium and large cities only — see Loans.HasBank). Borrow
// against your honour, or repay the loan this city is holding; the rules,
// limits and late penalties are all in src/banking/Loans.cs.
public partial class BankPopup : PanelContainer
{
    private Player player = null!;
    private GameTime time = null!;
    private Loans loans = null!;
    private VBoxContainer content = null!;
    private Label title = null!, terms = null!, status = null!, amount = null!, purse = null!;
    private HSlider slider = null!;
    private Control borrowRow = null!;
    private Button borrowButton = null!, repayButton = null!;
    private City? current;
    private int staged;          // the amount the slider is offering to lend
    private int layoutFrames;
    private Vector2 lastView;

    // Set by Main: something changed that the world outside has to redraw
    // (the market panel's purse, the city labels' debt tags, the event log).
    public Action<string>? Announce;
    public Action? Changed;

    public bool IsOpen => Visible;

    public void Init(Player player, GameTime time, Loans loans)
    {
        this.player = player; this.time = time; this.loans = loans;
        int px = Config.PopupFontPx;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", Fonts.Panel(new Color(16 / 255f, 14 / 255f, 10 / 255f, 0.98f), 8, 24, 20, new Color("#a08040")));

        content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 0);
        AddChild(content);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        title = Fonts.MakeLabel("", Fonts.UiBold, Config.PopupTitleFontPx - 4, new Color("#ffd166"));
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

        terms = Fonts.MakeLabel("", Fonts.Mono, px - 3, new Color("#aaaaaa"));
        terms.AutowrapMode = TextServer.AutowrapMode.Word;
        terms.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.AddChild(terms);

        content.AddChild(Fonts.Heading("Your account here"));
        status = Fonts.MakeLabel("", Fonts.MonoBold, px, new Color("#dddddd"));
        status.AutowrapMode = TextServer.AutowrapMode.Word;
        status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.AddChild(status);

        // Borrowing: the slider picks the sum (whole Config.Bank.Step
        // hundreds up to the credit limit), the button hands it over.
        content.AddChild(Fonts.Spacer(14));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        slider = new HSlider
        {
            MinValue = 0, MaxValue = 1, Step = 0.001, Value = 0,
            CustomMinimumSize = new Vector2(220, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        slider.ValueChanged += _ => { Stage(); Refresh(); };
        amount = Fonts.MakeLabel("", Fonts.MonoBold, px, new Color("#ffd166"));
        amount.CustomMinimumSize = new Vector2(180, 0);
        amount.HorizontalAlignment = HorizontalAlignment.Right;
        borrowButton = Fonts.MakeButton("Borrow", Borrow, 80);
        row.AddChild(slider);
        row.AddChild(amount);
        row.AddChild(borrowButton);
        borrowRow = row;
        content.AddChild(borrowRow);

        content.AddChild(Fonts.Spacer(14));
        var repayRow = new HBoxContainer();
        repayRow.AddThemeConstantOverride("separation", 14);
        repayRow.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        repayButton = Fonts.MakeButton("Repay in full", Repay, 130);
        repayRow.AddChild(repayButton);
        content.AddChild(repayRow);

        content.AddChild(Fonts.Spacer(16));
        content.AddChild(Fonts.Rule(new Color("#444444")));
        content.AddChild(Fonts.Spacer(10));
        purse = Fonts.MakeLabel("", Fonts.MonoBold, px, new Color("#ffd166"));
        content.AddChild(purse);
    }

    private int Limit => current == null ? 0 : loans.LimitFor(current, player.Honour);

    // The slider's fraction as a whole-Step sum.
    private void Stage()
    {
        int limit = Limit;
        int step = Config.Bank.Step;
        staged = limit <= 0 ? 0 : (int)Math.Round(slider.Value * limit / step) * step;
        staged = Math.Min(staged, limit);
    }

    private void Borrow()
    {
        if (current == null || staged <= 0) return;
        var loan = loans.Borrow(current, staged, player.Honour, Loans.DayOf(time.TotalMinutes));
        if (loan == null) return;
        player.Money += loan.Principal;
        Announce?.Invoke($"Borrowed {loan.Principal} in {loan.City.Name} — {loan.Owed} due in {Config.Bank.TermDays} days");
        Changed?.Invoke();
        slider.SetValueNoSignal(0);
        staged = 0;
        Refresh();
    }

    private void Repay()
    {
        if (current == null) return;
        var loan = loans.LoanAt(current);
        if (loan == null || player.Money < loan.Owed) return;
        player.Money -= loan.Owed;
        int earned = loans.Repay(loan, Loans.DayOf(time.TotalMinutes));
        player.Honour += earned;
        Announce?.Invoke($"Loan repaid in full in {loan.City.Name} — {loan.Owed}" +
                         (earned > 0 ? $", on time: +{earned} honour" : ", late"));
        Changed?.Invoke();
        Refresh();
    }

    public void Refresh()
    {
        if (current == null) return;
        var loan = loans.LoanAt(current);
        int today = Loans.DayOf(time.TotalMinutes);
        int cap = current.Size == "large" ? Config.Bank.MaxLarge : Config.Bank.MaxMedium;
        terms.Text =
            $"{current.Size} bank — lends up to {cap}, or {Config.Bank.HonourMultiplier}x your honour, whichever is less.\n" +
            $"Repaid in full plus {Math.Round(Config.Bank.Interest * 100)}% after {Config.Bank.TermDays} days, " +
            $"which earns honour worth a {Config.Bank.OnTimeHonourDivisor}th of the loan. " +
            $"A day late costs honour equal to a tenth of the loan instead, and again every {Config.Bank.LatePenaltyEveryDays} days after that.";

        if (loan != null)
        {
            int left = loan.DaysLeft(today);
            status.Text =
                $"borrowed {loan.Principal}   owed {loan.Owed}\n" +
                (left >= 0
                    ? $"due in {left} day{(left == 1 ? "" : "s")} — paying up in time is worth +{loans.OnTimeHonourFor(loan, today)} honour"
                    : $"{-left} day{(left == -1 ? "" : "s")} OVERDUE — honour {-Math.Max(1, loan.Principal / Config.Bank.LatePenaltyDivisor)} every {Config.Bank.LatePenaltyEveryDays} days");
            Fonts.SetColor(status, left >= 0 ? Config.BankDebtColor : Config.LowColor);
        }
        else
        {
            int limit = Limit;
            status.Text = limit > 0
                ? $"no loan outstanding — honour {player.Honour} buys you credit of {limit}"
                : $"no loan outstanding — honour {player.Honour} buys you no credit here";
            Fonts.SetColor(status, new Color("#dddddd"));
        }

        // One loan per city: you can borrow or repay, never both at once.
        borrowRow.Visible = loan == null;
        repayButton.Visible = loan != null;
        if (loan == null)
        {
            amount.Text = staged > 0 ? $"{staged} → repay {loans.OwedFor(staged)}" : "borrow nothing";
            Fonts.SetEnabled(borrowButton, staged > 0);
            slider.Editable = Limit > 0;
            borrowButton.TooltipText = staged > 0
                ? $"Take {staged} now; {loans.OwedFor(staged)} falls due in {Config.Bank.TermDays} days"
                : "Slide to choose how much to borrow";
        }
        else
        {
            bool canPay = player.Money >= loan.Owed;
            int earned = loans.OnTimeHonourFor(loan, today);
            repayButton.Text = $"Repay {loan.Owed}";
            Fonts.SetEnabled(repayButton, canPay);
            repayButton.TooltipText = !canPay ? "Not enough money aboard — it is all or nothing"
                : earned > 0 ? $"Clear the debt in full, on time: +{earned} honour"
                : "Clear the debt in full — too late for the honour";
        }
        purse.Text = $"money {player.Money}    honour {player.Honour}";
        layoutFrames = 3;
    }

    public void Show(City city)
    {
        if (!Loans.HasBank(city)) return;
        current = city;
        title.Text = $"Bank of {city.Name}";
        staged = 0;
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

    // Centred like the market panel, a little narrower so it reads as sitting
    // on top of it.
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
