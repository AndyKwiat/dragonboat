using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

// Live-tunable values. A tunable is a public static float/int/bool field on
// a registered static class, marked with [Tweak(group, min, max, step)]. The
// defaults are the values in code — that is the source of truth; the registry
// only lets you move them at runtime (the T panel, see ui/TweakPanel.cs, or
// `--set=Name=Value` on the command line) to find values worth committing.
//
//   public static class CombatConfig {
//       [Tweak("Cannon", 5, 100, 1)] public static float Gravity = 30f;
//   }
//   Tweaks.Register(typeof(CombatConfig));
//
// Names are the bare field names, so keep them unique across registered
// classes. `Tweaks.Dump()` prints every current value as a C# line, ready to
// paste back over the default.
[AttributeUsage(AttributeTargets.Field)]
public sealed class TweakAttribute : Attribute
{
    public readonly string Group;
    public readonly float Min, Max, Step;
    public TweakAttribute(string group, float min, float max, float step)
    {
        Group = group; Min = min; Max = max; Step = step;
    }
}

public static class Tweaks
{
    public sealed class Entry
    {
        public readonly string Group, Name, Owner;
        public readonly float Min, Max, Step;
        private readonly FieldInfo info;

        public readonly float Default;   // the value in code at Register time

        public Entry(FieldInfo field, TweakAttribute a)
        {
            info = field;
            Group = a.Group; Name = field.Name; Owner = field.DeclaringType!.Name;
            Min = a.Min; Max = a.Max; Step = a.Step;
            Default = Get();
        }

        public bool IsChanged => Get() != Default;

        public bool IsBool => info.FieldType == typeof(bool);

        public float Get()
        {
            var v = info.GetValue(null)!;
            return v switch { float f => f, int i => i, bool b => b ? 1 : 0, _ => 0 };
        }

        public void Set(float v)
        {
            if (info.FieldType == typeof(float)) info.SetValue(null, v);
            else if (info.FieldType == typeof(int)) info.SetValue(null, (int)MathF.Round(v));
            else if (info.FieldType == typeof(bool)) info.SetValue(null, v >= 0.5f);
        }

        public string Format()
        {
            if (IsBool) return Get() >= 0.5f ? "on" : "off";
            float v = Get();
            int decimals = Step >= 1 ? 0 : Step >= 0.1f ? 1 : 2;
            return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        // The line to paste back into the config file.
        public string AsCode()
        {
            string value = info.FieldType == typeof(float)
                ? Get().ToString("0.0###", CultureInfo.InvariantCulture) + "f"
                : info.FieldType == typeof(bool) ? (Get() >= 0.5f ? "true" : "false")
                : ((int)Get()).ToString(CultureInfo.InvariantCulture);
            return $"{Owner}.{Name} = {value};";
        }
    }

    public static readonly List<Entry> All = new();
    private static readonly Dictionary<string, Entry> byName = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(params Type[] types)
    {
        foreach (var t in types)
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var a = f.GetCustomAttribute<TweakAttribute>();
                if (a == null) continue;
                if (f.IsInitOnly || f.IsLiteral)
                    throw new InvalidOperationException($"{t.Name}.{f.Name}: [Tweak] fields must be plain static (not const/readonly)");
                if (byName.ContainsKey(f.Name))
                    throw new InvalidOperationException($"duplicate tweak name {f.Name} ({t.Name} and {byName[f.Name].Owner})");
                var e = new Entry(f, a);
                All.Add(e);
                byName[f.Name] = e;
            }
    }

    public static Entry? Find(string name) => byName.TryGetValue(name, out var e) ? e : null;

    // "Name=Value" — for the --set command-line flag. Returns false if unknown.
    public static bool Apply(string assignment)
    {
        int eq = assignment.IndexOf('=');
        if (eq < 0) return false;
        var e = Find(assignment[..eq].Trim());
        if (e == null) return false;
        string raw = assignment[(eq + 1)..].Trim();
        if (e.IsBool && (raw == "true" || raw == "on")) { e.Set(1); return true; }
        if (e.IsBool && (raw == "false" || raw == "off")) { e.Set(0); return true; }
        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return false;
        e.Set(v);
        return true;
    }

    public static IEnumerable<string> Groups() => All.Select(e => e.Group).Distinct();

    public static string Dump() => string.Join("\n", All.Select(e => e.AsCode()));

    // Only the values moved off their code defaults — what a dump is for.
    public static string DumpChanged() => string.Join("\n", All.Where(e => e.IsChanged).Select(e => e.AsCode()));

    // One "Owner.Name = value;" line as Dump writes them (comments and blank
    // lines are skipped) — how a tweaks.dump is applied back at launch.
    // Returns false only for an assignment-shaped line that didn't apply.
    public static bool ApplyDumpedLine(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line.StartsWith("//")) return true;
        int eq = line.IndexOf('=');
        if (eq < 0) return false;
        string name = line[..eq].Trim();
        int dot = name.LastIndexOf('.');
        if (dot >= 0) name = name[(dot + 1)..];
        string raw = line[(eq + 1)..].Trim().TrimEnd(';').Trim();
        if (raw.Length > 1 && (raw.EndsWith("f") || raw.EndsWith("F")) && char.IsDigit(raw[^2]))
            raw = raw[..^1];
        return Apply($"{name}={raw}");
    }
}
