using System;
using System.Collections.Generic;
using System.Globalization;

// A class of vessel: how big she is, how many hands she carries, what she
// holds, what she costs and how she handles. The whole fleet is read out of
// `boats.yml` at launch (BoatClasses.Parse) — add a block to that file and
// the boat exists, in the harbours, on the water and under guns.
//
// Handling is a MULTIPLIER over BoatConfig, never an absolute: the [Tweak]
// sliders stay the one place the boat's feel is tuned, and `medium` — all
// 1.0 — is the boat the game has always had. No Godot types: Main reads the
// file text and hands it to Parse.
public sealed class BoatClass
{
    public string Name { get; }
    public float HullLength { get; }
    public float HullWidth { get; }
    public int Sails { get; }        // masts on the model
    public int Crew { get; }         // fixed by the hull; rations and wages follow it
    public int Cargo { get; }        // units the hold takes
    public int Cost { get; }         // buy and sell price alike (docs/harbour.md)
    public float SailThrust { get; } // x BoatConfig.SailThrustScale
    public float OarThrust { get; }  // x BoatConfig.OarThrustScale
    public float Turn { get; }       // x BoatConfig.TurnRate
    public int GunsBow { get; }
    public int GunsStern { get; }
    public int GunsPerSide { get; }

    public int GunCount => GunsBow + GunsStern + 2 * GunsPerSide;

    // What the rig is worth on this hull, whichever way she is being driven.
    public float ThrustFactor(bool sailsUp) => sailsUp ? SailThrust : OarThrust;

    public BoatClass(string name, float hullLength, float hullWidth, int sails, int crew, int cargo, int cost,
        float sailThrust, float oarThrust, float turn, int gunsBow, int gunsStern, int gunsPerSide)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("a boat needs a name");
        string Where(string key) => $"boats.yml: {name}: {key}";
        foreach (var (key, v) in new[] { ("hullLength", hullLength), ("hullWidth", hullWidth),
                                         ("sailThrust", sailThrust), ("oarThrust", oarThrust), ("turn", turn) })
            if (!(v > 0)) throw new ArgumentException($"{Where(key)} must be more than 0, got {v}");
        if (sails < 0) throw new ArgumentException($"{Where("sails")} cannot be negative, got {sails}");
        if (crew < 1) throw new ArgumentException($"{Where("crew")} must be at least 1, got {crew}");
        if (cargo < 1) throw new ArgumentException($"{Where("cargo")} must be at least 1, got {cargo}");
        if (cost < 0) throw new ArgumentException($"{Where("cost")} cannot be negative, got {cost}");
        foreach (var (key, n) in new[] { ("gunsBow", gunsBow), ("gunsStern", gunsStern), ("gunsPerSide", gunsPerSide) })
            if (n < 0) throw new ArgumentException($"{Where(key)} cannot be negative, got {n}");
        Name = name; HullLength = hullLength; HullWidth = hullWidth; Sails = sails;
        Crew = crew; Cargo = cargo; Cost = cost;
        SailThrust = sailThrust; OarThrust = oarThrust; Turn = turn;
        GunsBow = gunsBow; GunsStern = gunsStern; GunsPerSide = gunsPerSide;
    }
}

// The fleet as the file describes it. `All` is set once by Main, the way
// GameConfig.Current is, and everything that needs a vessel looks one up by
// name here.
public static class BoatClasses
{
    public const string Path = "res://boats.yml";
    // The hull the player is given, and the one every AI boat sails. Names,
    // not indices, so boats.yml can be reordered freely.
    public const string StartingName = "small";
    public const string AiName = "medium";

    public static IReadOnlyList<BoatClass> All { get; set; } = Array.Empty<BoatClass>();

    public static BoatClass ByName(string name)
    {
        foreach (var b in All) if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) return b;
        var known = new List<string>();
        foreach (var b in All) known.Add(b.Name);
        throw new Exception($"boats.yml has no boat called '{name}' (it has {string.Join(", ", known)})");
    }

    public static BoatClass Starting => ByName(StartingName);
    public static BoatClass Ai => ByName(AiName);

    // A list of blocks: `- name: x` opens one, indented `key: value` lines
    // fill it. `#` comments and blank lines are dropped, as in
    // game-config.yml. Every key is required — the file, not the code, is
    // where a boat's numbers live.
    public static IReadOnlyList<BoatClass> Parse(string text)
    {
        var boats = new List<BoatClass>();
        Dictionary<string, string>? block = null;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Close()
        {
            if (block == null) return;
            boats.Add(Build(block));
            block = null;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.TrimEnd();
            if (line.Trim().Length == 0 || line.Trim() == "---") continue;
            bool opens = line.TrimStart().StartsWith("- ");
            if (opens) { Close(); block = new Dictionary<string, string>(); line = line.TrimStart()[2..]; }
            if (block == null)
                throw new Exception($"boats.yml: '{raw.Trim()}' comes before any boat — every boat starts with '- name: ...'");
            line = line.Trim();
            int colon = line.IndexOf(':');
            if (colon <= 0) throw new Exception($"boats.yml: cannot parse line '{raw.Trim()}'");
            block[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        Close();

        if (boats.Count == 0) throw new Exception("boats.yml: no boats in the file");
        foreach (var b in boats)
            if (!names.Add(b.Name)) throw new Exception($"boats.yml: two boats are called '{b.Name}'");
        foreach (var needed in new[] { StartingName, AiName })
            if (!names.Contains(needed))
                throw new Exception($"boats.yml: the '{needed}' boat is required — the player starts in "
                                  + $"'{StartingName}' and every AI boat sails a '{AiName}'");
        return boats;
    }

    private static BoatClass Build(Dictionary<string, string> block)
    {
        if (!block.TryGetValue("name", out var name))
            throw new Exception("boats.yml: a boat has no 'name' (every boat starts with '- name: ...')");
        string Where(string key) => $"boats.yml: {name}: ";
        string Raw(string key)
        {
            if (!block.TryGetValue(key, out var v)) throw new Exception($"{Where(key)}missing '{key}'");
            return v;
        }
        int Int(string key)
        {
            if (!int.TryParse(Raw(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                throw new Exception($"{Where(key)}{key} '{Raw(key)}' is not a whole number");
            return n;
        }
        float Num(string key)
        {
            if (!float.TryParse(Raw(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                throw new Exception($"{Where(key)}{key} '{Raw(key)}' is not a number");
            return f;
        }
        foreach (var key in block.Keys)
            if (Array.IndexOf(Keys, key) < 0)
                throw new Exception($"{Where(key)}'{key}' is not a boat key (expected {string.Join(", ", Keys)})");
        return new BoatClass(name, Num("hullLength"), Num("hullWidth"), Int("sails"), Int("crew"),
            Int("cargo"), Int("cost"), Num("sailThrust"), Num("oarThrust"), Num("turn"),
            Int("gunsBow"), Int("gunsStern"), Int("gunsPerSide"));
    }

    private static readonly string[] Keys =
    {
        "name", "hullLength", "hullWidth", "sails", "crew", "cargo", "cost",
        "sailThrust", "oarThrust", "turn", "gunsBow", "gunsStern", "gunsPerSide",
    };
}
