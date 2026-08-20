using Godot;
using System.Collections.Generic;

// The combat prototype's slider panel: one row per [Tweak] field (see
// core/Tweaks.cs), grouped, built by reflection so adding a knob is just
// adding an attribute. T toggles it. "Dump" saves every value moved off its
// code default as C# lines to tweaks.dump in the project root (and the
// console and clipboard) — hand that file to Claude to fold into the config.
public partial class TweakPanel : PanelContainer
{
    public const string DumpPath = "res://tweaks.dump";
    private const int RowPx = 11;
    private readonly List<(Tweaks.Entry entry, Label value, Control input)> rows = new();
    private ScrollContainer scroll = null!;
    private int top;

    // Save the values moved off their defaults to DumpPath, overwriting the
    // last dump; also to the console and clipboard for a quick look.
    public void Dump()
    {
        var code = Tweaks.DumpChanged();
        var body = code.Length == 0 ? "// no tweaks changed from their code defaults" : code;
        GD.Print(body);
        DisplayServer.ClipboardSet(body);
        using var f = FileAccess.Open(DumpPath, FileAccess.ModeFlags.Write);
        if (f == null) { GD.PushWarning($"tweaks: could not write {DumpPath}"); return; }
        f.StoreString("// tweaks.dump — values moved off their code defaults, written by the\n" +
                      "// tweak panel's dump button. Ask Claude to fold these into the config.\n" +
                      body + "\n");
        GD.Print($"tweaks saved to {ProjectSettings.GlobalizePath(DumpPath)}");
    }

    public void Init(int topPx)
    {
        top = topPx;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", Fonts.Panel(new Color(0, 0, 0, 0.6f), 4, 10, 8));
        scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(box);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        var title = Fonts.MakeLabel("tweaks [T]", Fonts.MonoBold, RowPx + 1, new Color("#9cf"));
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        var dump = new Button { Text = "dump", FocusMode = FocusModeEnum.None, TooltipText = "save changed values as C# to tweaks.dump (and console/clipboard)" };
        dump.AddThemeFontOverride("font", Fonts.Mono);
        dump.AddThemeFontSizeOverride("font_size", RowPx);
        dump.Pressed += Dump;
        head.AddChild(dump);
        box.AddChild(head);

        string? group = null;
        foreach (var e in Tweaks.All)
        {
            if (e.Group != group)
            {
                group = e.Group;
                var h = Fonts.MakeLabel(group, Fonts.MonoBold, RowPx, new Color("#9cf"));
                h.AddThemeConstantOverride("margin_top", 4);
                box.AddChild(h);
            }
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            var name = Fonts.MakeLabel(e.Name, Fonts.Mono, RowPx, new Color("#dddddd"));
            name.CustomMinimumSize = new Vector2(120, 0);
            name.TooltipText = $"{e.Owner}.{e.Name}";
            name.MouseFilter = MouseFilterEnum.Pass;
            row.AddChild(name);
            var value = Fonts.MakeLabel(e.Format(), Fonts.Mono, RowPx, Colors.White);
            value.CustomMinimumSize = new Vector2(44, 0);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            Control input;
            if (e.IsBool)
            {
                var cb = new CheckBox { ButtonPressed = e.Get() >= 0.5f, FocusMode = FocusModeEnum.None };
                cb.CustomMinimumSize = new Vector2(110, 0);
                cb.Toggled += on => { e.Set(on ? 1 : 0); value.Text = e.Format(); };
                input = cb;
            }
            else
            {
                var slider = new HSlider
                {
                    MinValue = e.Min, MaxValue = e.Max, Step = e.Step, Value = e.Get(),
                    FocusMode = FocusModeEnum.None,
                    CustomMinimumSize = new Vector2(110, 0),
                    SizeFlagsVertical = SizeFlags.ShrinkCenter,
                };
                slider.ValueChanged += v => { e.Set((float)v); value.Text = e.Format(); };
                input = slider;
            }
            row.AddChild(input);
            row.AddChild(value);
            box.AddChild(row);
            rows.Add((e, value, input));
        }
        SetAnchorsPreset(LayoutPreset.TopLeft);
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (Visible) Refresh();
    }

    // Pull current values into the controls (after --set, or code changing them).
    public void Refresh()
    {
        foreach (var (e, value, input) in rows)
        {
            if (input is HSlider s) s.SetValueNoSignal(e.Get());
            else if (input is CheckBox cb) cb.SetPressedNoSignal(e.Get() >= 0.5f);
            value.Text = e.Format();
        }
    }

    public void Update(Vector2 view)
    {
        if (!Visible) return;
        float maxH = view.Y - top - 12;
        scroll.CustomMinimumSize = new Vector2(0, Mathf.Min(maxH, ((Control)scroll.GetChild(0)).Size.Y + 4));
        Position = new Vector2(8, top);
    }
}
