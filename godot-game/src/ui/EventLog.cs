using Godot;
using System.Collections.Generic;

// Bottom-left feed of world news: things that happen to the player while
// they are out sailing and no panel is open — a late loan costing honour, a
// loan about to fall due. Lines fade out after Config.EventLogSeconds and
// the oldest are dropped past Config.EventLogMaxLines.
public partial class EventLog : PanelContainer
{
    private sealed class Line
    {
        public Label Label = null!;
        public float Left;   // real seconds before it fades away
    }

    private VBoxContainer box = null!;
    private readonly List<Line> lines = new();

    public void Init()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", Fonts.Panel(new Color(0, 0, 0, 0.55f), 3, 10, 6));
        box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        AddChild(box);
        Visible = false;
    }

    public void Add(string text, bool bad = false)
    {
        var label = Fonts.MakeLabel(text, Fonts.MonoBold, Config.EventLogFontPx,
            bad ? Config.LowColor : Config.BankDebtColor);
        box.AddChild(label);
        lines.Add(new Line { Label = label, Left = Config.EventLogSeconds });
        while (lines.Count > Config.EventLogMaxLines) Drop(0);
        Visible = true;
    }

    public void Add(IEnumerable<NewsEvent> news)
    {
        foreach (var e in news) Add(e.Text, e.Bad);
    }

    private void Drop(int i)
    {
        lines[i].Label.QueueFree();
        lines.RemoveAt(i);
    }

    // Real seconds, not game time: the feed empties even while moored.
    public void Update(float dt, Vector2 view)
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            line.Left -= dt;
            if (line.Left <= 0) { Drop(i); continue; }
            // Fade over the last second.
            line.Label.Modulate = new Color(1, 1, 1, Mathf.Min(1f, line.Left));
        }
        if (lines.Count == 0) { Visible = false; return; }
        Visible = true;
        Position = new Vector2(8, view.Y - Size.Y - 8);
    }
}
