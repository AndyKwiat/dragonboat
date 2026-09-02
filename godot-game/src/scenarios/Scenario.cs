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
//     boat: medium                   # which boat the player sails (boats.yml)
//     boats: p1:voyage>p2, ai0:protect*3@1  # `tag[:order[>dest]][*count][@team]`
//     ai: idle                       # default order for boats without one
//     team: 1                        # default side for boats without one
//
// No Godot types: Main reads the file text and hands it to Parse.
public sealed class ScenarioBoat
{
    public string Marker { get; }
    // The standing assignment this boat spawns with (src/ai/AiOrder.cs).
    public AiOrderKind Order { get; }
    // Marker tag this boat is bound for, if its order is going somewhere.
    public string? Destination { get; }
    // Whose side it is on; boats on different teams are enemies. The player
    // is team 0, so a scenario boat defaults to 1 and starts out hostile.
    public int Team { get; }
    // How many boats to put on this marker. More than one is a group, spaced
    // apart so they do not start inside one another.
    public int Count { get; }

    public ScenarioBoat(string marker, AiOrderKind order, string? destination = null, int team = 1, int count = 1)
    {
        if (count < 1) throw new ArgumentException($"a boat count must be at least 1, got {count}");
        Marker = marker; Order = order; Destination = destination; Team = team; Count = count;
    }
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
    // The boat the player sails on the bench. Defaults to the medium — the
    // vessel every scenario here was written against, and the one the AI
    // sails — so a bench measures the AI and not the player's hull. `--boat`
    // on the command line still wins.
    public string PlayerBoat { get; }
    public IReadOnlyList<ScenarioBoat> Boats { get; }

    public static string PathOf(string name) => $"res://scenarios/{name}.yml";

    public Scenario(string name, string terrainFile, bool cities, bool currents, bool enemyFleet,
        string? playerMarker, IReadOnlyList<ScenarioBoat>? boats = null, string? playerBoat = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("a scenario needs a name");
        if (string.IsNullOrWhiteSpace(terrainFile)) throw new ArgumentException($"scenario {name}: 'terrain' is empty");
        if (!cities && playerMarker == null)
            throw new ArgumentException($"scenario {name}: with 'cities: false' the player needs a 'player' marker to start on");
        Name = name; TerrainFile = terrainFile; Cities = cities; Currents = currents;
        EnemyFleet = enemyFleet; PlayerMarker = playerMarker;
        Boats = boats ?? Array.Empty<ScenarioBoat>();
        PlayerBoat = string.IsNullOrWhiteSpace(playerBoat) ? BoatClasses.AiName : playerBoat!.Trim();
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
        var fallbackOrder = AiOrders.Default;
        if (values.TryGetValue("ai", out var aiName))
            fallbackOrder = AiOrders.Parse(aiName) ?? throw new Exception($"{Where("ai")} '{aiName}' is not an AI order (expected {AiOrders.Names})");
        int fallbackTeam = 1;
        if (values.TryGetValue("team", out var teamName) && !int.TryParse(teamName.Trim(), out fallbackTeam))
            throw new Exception($"{Where("team")} '{teamName}' is not a whole number");
        var boats = new List<ScenarioBoat>();
        if (values.TryGetValue("boats", out var boatList))
            foreach (var spec in boatList.Split(','))
            {
                var entry = spec.Trim();
                if (entry.Length == 0) continue;
                // `@team` comes off the end before anything else is parsed.
                int team = fallbackTeam;
                int at = entry.IndexOf('@');
                if (at >= 0)
                {
                    if (!int.TryParse(entry[(at + 1)..].Trim(), out team))
                        throw new Exception($"{Where("boats")} '{entry}' has no team number after '@'");
                    entry = entry[..at].Trim();
                }
                // `*count` for a group on one marker.
                int count = 1;
                int star = entry.IndexOf('*');
                if (star >= 0)
                {
                    if (!int.TryParse(entry[(star + 1)..].Trim(), out count) || count < 1)
                        throw new Exception($"{Where("boats")} '{entry}' has no boat count after '*'");
                    entry = entry[..star].Trim();
                }
                var parts = entry.Split(':');
                if (parts.Length > 2 || parts[0].Trim().Length == 0)
                    throw new Exception($"{Where("boats")} '{entry}' is not a marker tag or tag:order");
                var order = fallbackOrder;
                string? destination = null;
                if (parts.Length == 2)
                {
                    // `order>destination`: where a boat that goes somewhere is bound.
                    var bound = parts[1].Split('>');
                    if (bound.Length > 2) throw new Exception($"{Where("boats")} '{entry}' has more than one destination");
                    order = AiOrders.Parse(bound[0])
                        ?? throw new Exception($"{Where("boats")} '{bound[0]}' is not an AI order (expected {AiOrders.Names})");
                    if (bound.Length == 2)
                    {
                        destination = bound[1].Trim();
                        if (destination.Length == 0)
                            throw new Exception($"{Where("boats")} '{entry}' names no destination after '>'");
                    }
                }
                if (AiOrders.NeedsDestination(order) && destination == null)
                    throw new Exception($"{Where("boats")} '{entry}' is a voyage with no destination (write '{parts[0].Trim()}:voyage>tag')");
                boats.Add(new ScenarioBoat(parts[0].Trim(), order, destination, team, count));
            }
        values.TryGetValue("player", out var player);
        values.TryGetValue("boat", out var playerBoat);
        return new Scenario(name, terrain, Flag("cities", true), Flag("currents", true), Flag("enemyFleet", true),
            string.IsNullOrWhiteSpace(player) ? null : player!.Trim(), boats, playerBoat);
    }

    private static readonly string[] Keys = { "terrain", "cities", "currents", "enemyFleet", "player", "boat", "boats", "ai", "team" };

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
