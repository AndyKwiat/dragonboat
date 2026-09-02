using Godot;
using System.Collections.Generic;
using System.Text.Json;

// What a scripted run says when it finishes.
//
// Two forms of the same facts. The human line is what a person reads in the
// terminal; --json is what a script asserts on, and the difference matters:
// every check in this project used to be a grep over an English sentence,
// which is how a boat that never left its mooring survived a verification
// pass — the run said "arrived (23 legs)" for one boat and nothing at all
// for the other, and a grep for "arrived" found one and looked satisfied.
//
// So the JSON form has two rules the line does not:
//   - it lists EVERY boat, where the line truncates for readability;
//   - every number is its own field, so nothing has to be parsed out of a
//     sentence where the player's `shots` sits next to an AI boat's.
public sealed class RunReport
{
    private readonly Boat player;
    private readonly AiBoats ai;
    private readonly CombatSystem combat;
    private readonly BoatCollisions hulls;
    private readonly CrewStamina stamina;
    private readonly Wind wind;
    private readonly GameTime time;

    public RunReport(Boat player, AiBoats ai, CombatSystem combat, BoatCollisions hulls,
        CrewStamina stamina, Wind wind, GameTime time)
    {
        this.player = player; this.ai = ai; this.combat = combat;
        this.hulls = hulls; this.stamina = stamina; this.wind = wind; this.time = time;
    }

    // The line this project has always printed, plus wherever the screenshot
    // went. Unchanged in shape, so everything already written against it and
    // every example in the docs still reads the same.
    public string Line(int frame, string? screenshot, Error? saveError)
    {
        var shot = screenshot == null ? ""
            : $"screenshot {(saveError == Error.Ok ? "saved to" : "FAILED " + saveError + " for")} {screenshot} ";
        return $"{shot}at frame {frame}; " +
               $"{player.Class.Name} at ({player.X:F1}, {player.Y:F1}) heading {Mathf.RadToDeg(player.Heading):F0}° speed {player.Speed:F1}; " +
               $"sails {(player.SailsUp ? "up" : "down")} stamina {stamina.Level:F2} " +
               $"wind {wind.Force:F0}@{wind.BearingDeg:F0}°; " +
               $"shots {combat.PlayerGuns?.Shots ?? 0} hits {combat.PlayerGuns?.Hits ?? 0} taken {player.HitsTaken} " +
               $"rams {hulls.Rams}" +
               (ai.Count > 0 ? "; " + ai.Report() : "");
    }

    // One object, one line, every boat.
    public string Json(int frame, float seconds)
    {
        var boats = new List<Dictionary<string, object?>>();
        foreach (var b in ai.Boats)
        {
            var d = new Dictionary<string, object?>
            {
                ["order"] = b.Order.Kind.ToString().ToLowerInvariant(),
                ["state"] = AiStates.Name(b.State),
                ["sunk"] = b.Sunk,
                ["team"] = b.Team,
                ["x"] = Round(b.X), ["y"] = Round(b.Y),
                ["heading"] = Round(Mathf.RadToDeg(b.Heading)),
                ["speed"] = Round(b.Speed),
                ["shots"] = b.Shots,
                ["taken"] = b.HitsTaken,
            };
            if (b.Aimed is AiContact t)
            {
                d["target"] = t.Id;
                d["range"] = Round(b.Range);
                d["bearing"] = Round(b.BearingDeg);
                d["los"] = b.HadLineOfSight;
            }
            else d["target"] = null;
            if (b.Route != null)
            {
                d["leg"] = b.Leg;
                d["legs"] = b.Route.Waypoints.Count;
                d["toGo"] = Round(b.DistanceToGo());
            }
            boats.Add(d);
        }
        var report = new Dictionary<string, object?>
        {
            ["frame"] = frame,
            ["seconds"] = Round(seconds),
            ["minutes"] = time.TotalMinutes,
            ["player"] = new Dictionary<string, object?>
            {
                ["boat"] = player.Class.Name,
                ["x"] = Round(player.X), ["y"] = Round(player.Y),
                ["heading"] = Round(Mathf.RadToDeg(player.Heading)),
                ["speed"] = Round(player.Speed),
                ["sails"] = player.SailsUp ? "up" : "down",
                ["stamina"] = Round(stamina.Level),
                ["shots"] = combat.PlayerGuns?.Shots ?? 0,
                ["hits"] = combat.PlayerGuns?.Hits ?? 0,
                ["taken"] = player.HitsTaken,
                ["rams"] = hulls.Rams,
            },
            ["ai"] = boats,
        };
        return JsonSerializer.Serialize(report);
    }

    // A tenth of a world unit is finer than anything here is meaningful to,
    // and it keeps a repeated run byte-identical instead of differing in the
    // seventh decimal place.
    private static double Round(float v) => System.Math.Round(v, 1);
}
