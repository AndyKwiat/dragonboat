using System;
using System.Collections.Generic;
using System.Globalization;

// game-config.yml (repo root): which terrain file to load and how many
// cities to generate. No Godot types — Main reads the file text and hands
// it to Parse. Every key is required: the file, not the code, is where
// these numbers live. The YAML subset is flat `key: value` lines with `#`
// comments, which is all the file needs.
public sealed class GameConfig
{
    public const string Path = "res://game-config.yml";

    public string TerrainFile { get; }
    public int CityCount { get; }

    // Set once by Main; systems that scale with the city count read it.
    public static GameConfig Current { get; set; } = null!;

    public GameConfig(string terrainFile, int cityCount)
    {
        if (string.IsNullOrWhiteSpace(terrainFile)) throw new ArgumentException("terrainFile is empty");
        if (cityCount < 1) throw new ArgumentException($"cityCount must be at least 1, got {cityCount}");
        TerrainFile = terrainFile; CityCount = cityCount;
    }

    // A count tuned for a 100-city world, scaled to this config's city
    // count (rounded, never below 1).
    public int ScaledByCities(int per100Cities) =>
        Math.Max(1, (int)Math.Round(per100Cities * CityCount / 100.0));

    public static GameConfig Parse(string text)
    {
        var values = ParseFlatYaml(text);
        if (!values.TryGetValue("terrainFile", out var terrain)) throw new Exception("game-config.yml: missing 'terrainFile'");
        if (!values.TryGetValue("cityCount", out var count)) throw new Exception("game-config.yml: missing 'cityCount'");
        if (!int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cities))
            throw new Exception($"game-config.yml: cityCount '{count}' is not an integer");
        return new GameConfig(terrain, cities);
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
