using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using DestructionRacing.AI;
using DestructionRacing.Core;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>
/// Wreckin' Racing sim harness (plan §17.1/§17.4): runs races back to back, headless, with the PlayerProxy bot in
/// the player's car, and writes the report to out/sims/.
///   --sims=N (default 10)  --seed=S (default 1)  --profile=average|racer|hunter|skilled|compare  --laps=L
///   --skill=X (override the proxy's skill)  --cars=N  --dump=SECONDS  --wallhits  --playerhits
/// "compare" runs every seed twice, once with the racer bot and once with the hunter bot (equal skill).
/// Run with: tools/dev/headless.ps1 -Scene res://scenes/test/WreckinSim.tscn -UserArgs '--sims=10'
/// </summary>
public partial class WreckinSim : Node
{
    private readonly List<RaceMetricsResult> _results = new();
    private readonly List<(int Seed, string Profile)> _queue = new();
    private int _sims = 10;
    private int _seed = 1;
    private int _laps;
    private string _profile = "average";
    private WreckinMode? _current;
    private string _currentProfile = "";
    private ulong _startTicks;

    public override void _Ready()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (int.TryParse(DevTools.Instance?.Arg("sims"), out int n)) _sims = Math.Max(1, n);
        if (int.TryParse(DevTools.Instance?.Arg("seed"), out int s)) _seed = s;
        if (int.TryParse(DevTools.Instance?.Arg("laps"), out int l)) _laps = Math.Max(1, l);
        _profile = DevTools.Instance?.Arg("profile") ?? "average";
        for (int i = 0; i < _sims; i++)
        {
            if (_profile == "compare")
            {
                _queue.Add((_seed + i, "racer"));
                _queue.Add((_seed + i, "hunter"));
            }
            else _queue.Add((_seed + i, _profile));
        }
        _startTicks = Time.GetTicksMsec();
        GD.Print($"[Sim] {_queue.Count} races, profile {_profile}, seeds {_seed}..{_seed + _sims - 1}");
        StartNext();
    }

    private static Personality ProfileFor(string name) => name switch
    {
        "racer" => Personality.RacerPlayer,
        "hunter" => Personality.HunterPlayer,
        "skilled" => Personality.SkilledPlayer,
        _ => Personality.AverageFan,
    };

    private void StartNext()
    {
        if (_current != null)
        {
            _current.QueueFree();
            _current = null;
        }
        if (_queue.Count == 0)
        {
            Report();
            return;
        }
        var (seed, profile) = _queue[0];
        _queue.RemoveAt(0);
        _currentProfile = profile;
        _current = new WreckinMode
        {
            Headless = true,
            ProxyPlayer = true,
            ProxyProfile = float.TryParse(DevTools.Instance?.Arg("skill"), NumberStyles.Float, CultureInfo.InvariantCulture, out float skill)
                ? ProfileFor(profile) with { Skill = skill }
                : ProfileFor(profile),
            Seed = seed,
            CountdownTime = 0.5f,
            DumpEvery = double.TryParse(DevTools.Instance?.Arg("dump"), out double dump) ? dump : 0,
        };
        if (_laps > 0) _current.Laps = _laps;
        _current.DebugWallHits = DevTools.Instance?.Arg("wallhits") != null;
        _current.DebugPlayerHits = DevTools.Instance?.Arg("playerhits") != null;
        if (int.TryParse(DevTools.Instance?.Arg("cars"), out int cars)) _current.CarCount = Math.Clamp(cars, 1, 20);
        _current.Finished += OnFinished;
        AddChild(_current);
    }

    private void OnFinished(WreckinMode mode)
    {
        var r = mode.MetricsResult;
        r.Profile = _currentProfile;
        GD.Print("[Sim]   damage by source: " + string.Join(", ", mode.Referee.DamageBySource.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:0}")));
        _results.Add(r);
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"[Sim] #{r.Seed} {r.Profile}: {r.RaceLength:0}s, leader laps {r.LeaderLaps:0.0}, spins {r.Spins} ({r.SpinsPerLap:0.0}/lap), leader spins {r.LeaderSpins}, " +
            $"crossover {r.CrossoverIncidents}, wrecks {r.Wrecks}, player {(r.PlayerWrecked ? $"DNF at {r.PlayerWreckedAtDistance * 100:0}%" : $"P{r.PlayerPlace}")} " +
            $"pts {r.PlayerPoints} (spin {r.PlayerSpinPoints} wreck {r.PlayerWreckPoints} fin {r.PlayerFinishPoints}), pos changes {r.PlayerPositionChanges}, " +
            $"close {r.PlayerCloseShare * 100:0}%, hits {r.PlayerHitsDealt}/{r.PlayerHitsTaken}{(r.TimedOut ? " TIMEOUT" : "")}"));
        CallDeferred(MethodName.StartNext);
    }

    private static double Median(IEnumerable<double> values)
    {
        var v = values.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
        if (v.Count == 0) return double.NaN;
        return v.Count % 2 == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2 - 1] + v[v.Count / 2]);
    }

    private void Report()
    {
        var r = _results;
        int n = r.Count;
        double share(Func<RaceMetricsResult, bool> f) => n == 0 ? 0 : (double)r.Count(f) / n;
        var ci = CultureInfo.InvariantCulture;

        var rows = new List<(string Metric, string Value, string Target, bool? Pass)>
        {
            ("Spins in the field per lap (median)", $"{Median(r.Select(x => x.SpinsPerLap)):0.0}", "≥ 3", Median(r.Select(x => x.SpinsPerLap)) >= 3),
            ("Crossover incidents per race (median)", $"{Median(r.Select(x => (double)x.CrossoverIncidents)):0.0}", "≥ 2", Median(r.Select(x => (double)x.CrossoverIncidents)) >= 2),
            ("Player position changes per race (median)", $"{Median(r.Select(x => (double)x.PlayerPositionChanges)):0}", "≥ 6", Median(r.Select(x => (double)x.PlayerPositionChanges)) >= 6),
            ("Leader spun per race (median)", $"{Median(r.Select(x => (double)x.LeaderSpins)):0.0}", "≥ 2", Median(r.Select(x => (double)x.LeaderSpins)) >= 2),
            ("Cars within 3 s of the player (median share of race time)", $"{Median(r.Select(x => x.PlayerCloseShare)) * 100:0}%", "≥ 60%", Median(r.Select(x => x.PlayerCloseShare)) >= 0.6),
            ("Player wrecked before half distance", $"{share(x => x.PlayerWreckedBeforeHalf) * 100:0}%", "≤ 10%", share(x => x.PlayerWreckedBeforeHalf) <= 0.10),
            ("Player stuck or recovering", $"{(r.Sum(x => x.PlayerDrivingTime) > 0 ? 100 * r.Sum(x => x.PlayerStuckTime) / r.Sum(x => x.PlayerDrivingTime) : 0):0.0}% of time", "≤ 5%",
                r.Sum(x => x.PlayerDrivingTime) <= 0 || r.Sum(x => x.PlayerStuckTime) / r.Sum(x => x.PlayerDrivingTime) <= 0.05),
            ("Races that ended normally (no time-out)", $"{share(x => !x.TimedOut) * 100:0}%", "100%", r.All(x => !x.TimedOut)),
            ("Race length / winner time (median)", $"{Median(r.Select(x => x.RaceLength)):0} s / {Median(r.Select(x => x.WinnerTime)):0} s", "–", null),
            ("Player finished / mean place", $"{share(x => x.PlayerFinished) * 100:0}% / {r.Average(x => (double)x.PlayerPlace):0.0}", "–", null),
            ("Player points (median): total / spin / wreck / finish",
                $"{Median(r.Select(x => (double)x.PlayerPoints)):0} / {Median(r.Select(x => (double)x.PlayerSpinPoints)):0} / {Median(r.Select(x => (double)x.PlayerWreckPoints)):0} / {Median(r.Select(x => (double)x.PlayerFinishPoints)):0}", "–", null),
            ("Player hits dealt / taken (median)", $"{Median(r.Select(x => (double)x.PlayerHitsDealt)):0} / {Median(r.Select(x => (double)x.PlayerHitsTaken)):0}", "–", null),
            ("Spins / wrecks / finishers per race (mean)", $"{r.Average(x => x.Spins):0.0} / {r.Average(x => x.Wrecks):0.0} / {r.Average(x => x.Finishers):0.0}", "–", null),
        };

        var racer = r.Where(x => x.Profile == "racer").ToList();
        var hunter = r.Where(x => x.Profile == "hunter").ToList();
        if (racer.Count > 0 && hunter.Count > 0)
        {
            double pr = racer.Average(x => (double)x.PlayerPoints), ph = hunter.Average(x => (double)x.PlayerPoints);
            double diff = Math.Abs(pr - ph) / Math.Max(1.0, Math.Max(pr, ph));
            rows.Insert(5, ("Racer vs. hunter bot points (mean)", $"{pr:0} vs {ph:0} ({diff * 100:0}% apart)", "within ±15%", diff <= 0.15));
        }

        var md = new StringBuilder();
        md.AppendLine($"# Wreckin' sim report — {n} races, PlayerProxy \"{_profile}\", seeds {_seed}..{_seed + _sims - 1}");
        md.AppendLine();
        md.AppendLine("| Metric | Value | Target | |");
        md.AppendLine("|---|---|---|---|");
        foreach (var (metric, value, target, pass) in rows)
            md.AppendLine($"| {metric} | {value} | {target} | {(pass == null ? "" : pass.Value ? "✅" : "❌")} |");
        md.AppendLine();
        md.AppendLine($"Wall-clock time: {(Time.GetTicksMsec() - _startTicks) / 1000.0:0} s.");
        string text = md.ToString();

        string dir = ProjectSettings.GlobalizePath("res://out/sims");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", ci);
        using (var f = FileAccess.Open($"{dir}/wreckin_{_profile}_{stamp}.md", FileAccess.ModeFlags.Write)) f?.StoreString(text);
        using (var f = FileAccess.Open($"{dir}/wreckin_{_profile}_{stamp}.json", FileAccess.ModeFlags.Write))
            f?.StoreString(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));

        foreach (var (metric, value, target, pass) in rows)
            GD.Print($"[Sim] {(pass == null ? "  " : pass.Value ? "OK" : "!!")} {metric}: {value} (target {target})");
        GD.Print($"[Sim] report: {dir}/wreckin_{_profile}_{stamp}.md");
        DevTools.QuitClean(GetTree(), 0);
    }
}
