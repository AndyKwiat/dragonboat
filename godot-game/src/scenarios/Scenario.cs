using System;
using System.Collections.Generic;

// A test scenario: a stripped-down world used to develop and verify things
// that are hard to see in the real game — right now the boat AI and its
// pathing (docs/ai.md). Definitions live in `scenarios/*.yml` and are picked
// with `--scenario=name`; `./pathingTest.sh` is a shortcut for one of them.
//
// A scenario only ever SUBTRACTS from or overrides the normal launch: a
// different map, no cities, no currents, no hostile fleet, the player put on
// a named marker, AI boats put on other markers. With no `--scenario` flag
// none of this code runs and the game launches exactly as it always did.
//
// The file is the same flat `key: value` YAML subset as game-config.yml.
// Every key has a default except `terrain`:
//
//     terrain: pathingTest.terrain   # the .terrain file to load (required)
//     cities: false                  # generate the coastal cities? (default true)
//     currents: false                # lay down current zones? (default true)
//     enemyFleet: false              # spawn the legacy hostile fleet? (default true)
//     player: p0                     # marker tag to start the player boat on
//     boats: p1, p2:idle             # AI boats, one per marker, `tag[:state]`
//     ai: idle                       # default state for boats without one
//
// No Godot types: Main reads the file text and hands it to Parse.
public sealed class ScenarioBoat
{
    public string Marker { get; }
    public AiState State { get; }
    public ScenarioBoat(string marker, AiState state) { Marker = marker; State = state; }
}

public sealed class Scenario
{
    public string Name { get; }
    public string TerrainFile { get; }
    public bool Cities { get; }
    public bool Currents { get; }
    public bool EnemyFleet { get; }
    // Marker tag the player boat starts on; null = place it the normal way
    // (off a large city's shore), which needs `cities: true`.
    public string? PlayerMarker { get; }
    public IReadOnlyList<ScenarioBoat> Boats { get; }

    public static string PathOf(string name) => $"res://scenarios/{name}.yml";

    public Scenario(string name, string terrainFile, bool cities, bool currents, bool enemyFleet,
        string? playerMarker, IReadOnlyList<ScenarioBoat>? boats = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("a scenario needs a name");
        if (string.IsNullOrWhiteSpace(terrainFile)) throw new ArgumentException($"scenario {name}: 'terrain' is empty");
        if (!cities && playerMarker == null)
            throw new ArgumentException($"scenario {name}: with 'cities: false' the player needs a 'player' marker to start on");
        Name = name; TerrainFile = terrainFile; Cities = cities; Currents = currents;
        EnemyFleet = enemyFleet; PlayerMarker = playerMarker;
        Boats = boats ?? Array.Empty<ScenarioBoat>();
    }

    public static Scenario Parse(string name, string text)
    {
        var values = GameConfig.ParseFlatYaml(text);
        string Where(string key) => $"scenario {name}: {key}";
        bool Flag(string key, bool fallback)
        {
            if (!values.TryGetValue(key, out var raw)) return fallback;
            return raw.ToLowerInvariant() switch
            {
                "true" or "yes" or "on" => true,
                "false" or "no" or "off" => false,
                _ => throw new Exception($"{Where(key)} '{raw}' is not true or false"),
            };
        }
        foreach (var key in values.Keys)
            if (Array.IndexOf(Keys, key) < 0)
                throw new Exception($"{Where(key)} is not a scenario key (expected {string.Join(", ", Keys)})");
        if (!values.TryGetValue("terrain", out var terrain)) throw new Exception($"scenario {name}: missing 'terrain'");
        var fallbackState = AiStates.Default;
        if (values.TryGetValue("ai", out var aiName))
            fallbackState = AiStates.Parse(aiName) ?? throw new Exception($"{Where("ai")} '{aiName}' is not an AI state (expected {AiStates.Names})");
        var boats = new List<ScenarioBoat>();
        if (values.TryGetValue("boats", out var boatList))
            foreach (var spec in boatList.Split(','))
            {
                var entry = spec.Trim();
                if (entry.Length == 0) continue;
                var parts = entry.Split(':');
                if (parts.Length > 2 || parts[0].Trim().Length == 0)
                    throw new Exception($"{Where("boats")} '{entry}' is not a marker tag or tag:state");
                var state = parts.Length == 1 ? fallbackState
                    : AiStates.Parse(parts[1]) ?? throw new Exception($"{Where("boats")} '{parts[1]}' is not an AI state (expected {AiStates.Names})");
                boats.Add(new ScenarioBoat(parts[0].Trim(), state));
            }
        values.TryGetValue("player", out var player);
        return new Scenario(name, terrain, Flag("cities", true), Flag("currents", true), Flag("enemyFleet", true),
            string.IsNullOrWhiteSpace(player) ? null : player!.Trim(), boats);
    }

    private static readonly string[] Keys = { "terrain", "cities", "currents", "enemyFleet", "player", "boats", "ai" };

    // The marker a scenario named, or a loud failure: a scenario that spawns
    // on a tag the map does not carry is a broken test, not a quiet no-op.
    // Tags are the painter's free text and need not be unique — the LAST one
    // wins, which is the same "later markers win" rule the file format uses.
    public TerrainMarker Marker(TerrainMap map, string tag)
    {
        for (int i = map.Markers.Count - 1; i >= 0; i--)
            if (map.Markers[i].Tag == tag) return map.Markers[i];
        var tags = new List<string>();
        foreach (var m in map.Markers) tags.Add(m.Tag);
        throw new Exception($"scenario {Name}: {TerrainFile} has no marker tagged '{tag}'" +
                            (tags.Count == 0 ? " (it has no markers at all)" : $" (it has: {string.Join(", ", tags)})"));
    }
}
