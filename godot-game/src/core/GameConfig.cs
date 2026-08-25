using System;
using System.Collections.Generic;
using System.Globalization;

// game-config.yml (repo root): which terrain file to load, how many cities
// of each size to generate and how thick the currents run. No Godot types — Main reads the file text and hands
// it to Parse. Every key is required: the file, not the code, is where
// these numbers live. The YAML subset is flat `key: value` lines with `#`
// comments, which is all the file needs.
public sealed class GameConfig
{
    public const string Path = "res://game-config.yml";

    public string TerrainFile { get; }
    // How many cities of each size to place; the mix is the file's business,
    // not the code's (CityPlacer.SizesFor).
    public int SmallCities { get; }
    public int MediumCities { get; }
    public int LargeCities { get; }
    // The total, which is what the story-event counts scale by.
    public int CityCount => SmallCities + MediumCities + LargeCities;
    // Currents per map area: 100 is the baseline (CurrentsAtBaseDensity
    // arrows on an 8192² map), 200 twice as many, 10 a tenth, 0 none.
    public double CurrentDensity { get; }

    // Set once by Main; systems that scale with the city count read it.
    public static GameConfig Current { get; set; } = null!;

    // The baseline the current count is quoted at: CurrentsAtBaseDensity
    // arrows for a density of 100 on an 8192² map. (This lives here rather
    // than in Config.cs so GameConfig stays free of Godot types.)
    public const int CurrentsAtBaseDensity = 3600;
    public const double BaseCurrentDensity = 100;
    public const long BaseMapCells = 8192L * 8192L;

    public GameConfig(string terrainFile, int smallCities, int mediumCities, int largeCities, double currentDensity)
    {
        if (string.IsNullOrWhiteSpace(terrainFile)) throw new ArgumentException("terrainFile is empty");
        foreach (var (name, n) in new[] { ("smallCities", smallCities), ("mediumCities", mediumCities), ("largeCities", largeCities) })
            if (n < 0) throw new ArgumentException($"{name} cannot be negative, got {n}");
        if (smallCities + mediumCities + largeCities < 1)
            throw new ArgumentException("at least one city is needed (smallCities + mediumCities + largeCities)");
        if (currentDensity < 0 || double.IsNaN(currentDensity))
            throw new ArgumentException($"currentDensity must be 0 or more, got {currentDensity}");
        TerrainFile = terrainFile; CurrentDensity = currentDensity;
        SmallCities = smallCities; MediumCities = mediumCities; LargeCities = largeCities;
    }

    // How many current arrows a map of this many cells gets: linear in both
    // the density and the map's area, so the flow looks the same on any map.
    public int CurrentCount(long mapCells) => (int)Math.Round(
        CurrentsAtBaseDensity * (CurrentDensity / BaseCurrentDensity) * (mapCells / (double)BaseMapCells));

    // A count tuned for a 100-city world, scaled to this config's city
    // count (rounded, never below 1).
    public int ScaledByCities(int per100Cities) =>
        Math.Max(1, (int)Math.Round(per100Cities * CityCount / 100.0));

    public static GameConfig Parse(string text)
    {
        var values = ParseFlatYaml(text);
        if (!values.TryGetValue("terrainFile", out var terrain)) throw new Exception("game-config.yml: missing 'terrainFile'");
        // The size mix used to be a code constant behind a single cityCount;
        // it is now three counts in the file.
        if (values.ContainsKey("cityCount") && !values.ContainsKey("smallCities"))
            throw new Exception("game-config.yml: 'cityCount' is gone — give 'smallCities', 'mediumCities' and 'largeCities' instead");
        int Count(string key)
        {
            if (!values.TryGetValue(key, out var raw)) throw new Exception($"game-config.yml: missing '{key}'");
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                throw new Exception($"game-config.yml: {key} '{raw}' is not an integer");
            return n;
        }
        int small = Count("smallCities"), medium = Count("mediumCities"), large = Count("largeCities");
        if (!values.TryGetValue("currentDensity", out var density)) throw new Exception("game-config.yml: missing 'currentDensity'");
        if (!double.TryParse(density, NumberStyles.Float, CultureInfo.InvariantCulture, out double currents))
            throw new Exception($"game-config.yml: currentDensity '{density}' is not a number");
        return new GameConfig(terrain, small, medium, large, currents);
    }

    public static Dictionary<string, string> ParseFlatYaml(string text)
    {
        var values = new Dictionary<string, string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            if (line.Length == 0 || line == "---") continue;
            int colon = line.IndexOf(':');
            if (colon <= 0) throw new Exception($"game-config.yml: cannot parse line '{raw.Trim()}'");
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
                value = value[1..^1];
            values[key] = value;
        }
        return values;
    }
}
