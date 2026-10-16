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
/// Run with: tools/dev/headless.ps1 -Scene res://scenes/test/RaceSim.tscn -UserArgs '--sims=10'
/// </summary>
public partial class RaceSim : Node
{
    private readonly List<RaceMetricsResult> _results = new();
    private readonly List<(int Seed, string Profile)> _queue = new();
    private int _sims = 10;
    private int _seed = 1;
    private int _laps;
    private string _profile = "average";
    private Rules.GameMode _kind = Rules.GameMode.WreckinRacing;
    private bool StockCar => _kind == Rules.GameMode.StockCarRacing;
    private RaceMode? _current;
    private string _currentProfile = "";
    private ulong _startTicks;

    public override void _Ready()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (int.TryParse(DevTools.Instance?.Arg("sims"), out int n)) _sims = Math.Max(1, n);
        if (int.TryParse(DevTools.Instance?.Arg("seed"), out int s)) _seed = s;
        if (int.TryParse(DevTools.Instance?.Arg("laps"), out int l)) _laps = Math.Max(1, l);
        _profile = DevTools.Instance?.Arg("profile") ?? "average";
        _kind = RaceMode.ParseKind(DevTools.Instance?.Arg("kind"));
        AI.RaceBrain.DefenceEnabled = DevTools.Instance?.Arg("nodefence") == null;
        AI.RaceBrain.LagEnabled = DevTools.Instance?.Arg("nolag") == null;
        AI.RaceBrain.TailgateEnabled = DevTools.Instance?.Arg("notailgate") == null;
        AI.RaceBrain.BrakeCheckEnabled = DevTools.Instance?.Arg("nobrakecheck") == null;
        AI.RaceBrain.SqueezeEnabled = DevTools.Instance?.Arg("nosqueeze") == null;
        if (float.TryParse(DevTools.Instance?.Arg("scheadway"), NumberStyles.Float, CultureInfo.InvariantCulture, out float h)) AI.RaceBrain.StockCarHeadway = h;
        if (float.TryParse(DevTools.Instance?.Arg("scdraft"), NumberStyles.Float, CultureInfo.InvariantCulture, out float dr)) AI.RaceBrain.StockCarDraft = dr;
        if (float.TryParse(DevTools.Instance?.Arg("scpace"), NumberStyles.Float, CultureInfo.InvariantCulture, out float sp)) RaceMode.StockCarPace = sp;
        if (int.TryParse(DevTools.Instance?.Arg("scbumplap"), out int bl)) AI.RaceBrain.StockCarBumpLap = bl;
        if (float.TryParse(DevTools.Instance?.Arg("proxypitline"), NumberStyles.Float, CultureInfo.InvariantCulture, out float pl)) RaceMode.ProxyPitLine = pl;
        if (DevTools.Instance?.Arg("leaderhunt") is { } lh && lh.Split(',') is [var behind, var ahead])
        {
            AI.RaceBrain.LeaderHuntBehind = float.Parse(behind, CultureInfo.InvariantCulture);
            AI.RaceBrain.LeaderHuntAhead = float.Parse(ahead, CultureInfo.InvariantCulture);
        }
        Tracks.Speedway.BarrelColliders = DevTools.Instance?.Arg("nobarrels") == null;
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
            // Out of the tree (and the physics space) before the next race builds its own.
            RemoveChild(_current);
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
        _current = new RaceMode
        {
            Headless = true,
            ProxyPlayer = true,
            ProxyProfile = float.TryParse(DevTools.Instance?.Arg("skill"), NumberStyles.Float, CultureInfo.InvariantCulture, out float skill)
                ? ProfileFor(profile) with { Skill = skill }
                : ProfileFor(profile),
            Seed = seed,
            Kind = _kind,
            CountdownTime = 0.5f,
            DumpEvery = double.TryParse(DevTools.Instance?.Arg("dump"), out double dump) ? dump : 0,
        };
        if (_laps > 0) _current.Laps = _laps;
        // --layout=oval|figure8 and --reverse: the Crossroads Speedway layout (default the figure-8, forward).
        if (DevTools.Instance?.Arg("layout") is { } layout)
            _current.LayoutId = layout.ToLowerInvariant() == "oval" ? Tracks.SpeedwayLayout.Oval : Tracks.SpeedwayLayout.Figure8;
        if (DevTools.Instance?.Arg("reverse") != null) _current.Reverse = true;
        // Balance sims (M5): --randomcars puts every driver, the proxy too, in a random model; --car=<id> fixes the proxy's.
        if (DevTools.Instance?.Arg("randomcars") != null)
        {
            _current.RandomCars = true;
            _current.PlayerModel = Vehicles.CarCatalog.Ids[(seed * 7 + 3) % Vehicles.CarCatalog.Ids.Length];
        }
        if (DevTools.Instance?.Arg("car") is { } car) _current.PlayerModel = car;
        _current.DebugWallHits = DevTools.Instance?.Arg("wallhits") != null;
        _current.DebugPlayerHits = DevTools.Instance?.Arg("playerhits") != null;
        // --endurance: the 100-lap AI check (no damage; 8 cars unless --cars says otherwise).
        if (DevTools.Instance?.Arg("endurance") != null)
        {
            _current.Endurance = true;
            _current.Laps = _laps > 0 ? _laps : 100;
            _current.CarCount = 8;
            _current.MaxDuration = 6000;
        }
        if (int.TryParse(DevTools.Instance?.Arg("cars"), out int cars)) _current.CarCount = Math.Clamp(cars, 1, 20);
        _current.Finished += OnFinished;
        AddChild(_current);
    }

    private void OnFinished(RaceMode mode)
    {
        var r = mode.MetricsResult;
        r.Profile = _currentProfile;
        GD.Print("[Sim]   damage by source: " + string.Join(", ", mode.Referee.DamageBySource.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:0}")));
        _results.Add(r);
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"[Sim] #{r.Seed} {r.Profile}: {r.RaceLength:0}s, leader laps {r.LeaderLaps:0.0}, spins {r.Spins} ({r.SpinsPerLap:0.0}/lap), leader spins {r.LeaderSpins}, " +
            $"crossover {r.CrossoverIncidents}, wrecks {r.Wrecks}, player {(r.PlayerWrecked ? $"DNF at {r.PlayerWreckedAtDistance * 100:0}%" : $"P{r.PlayerPlace}")} " +
            $"pts {r.PlayerPoints} (spin {r.PlayerSpinPoints} wreck {r.PlayerWreckPoints} fin {r.PlayerFinishPoints}), pos changes {r.PlayerPositionChanges}, " +
            $"close {r.PlayerCloseShare * 100:0}%, hits {r.PlayerHitsDealt}/{r.PlayerHitsTaken}, dnfs {r.Dnfs}, lap-2 spread {r.Lap2Spread:0.0}s" +
            $", pits {r.PitStops} (player {r.PlayerPitStops}){(r.PlayerLateBattle ? ", late battle" : "")}{(r.TimedOut ? " TIMEOUT" : "")}" +
            $", DD code T1 {r.BrakeChecks} ({r.BrakeCheckHits} hit) T4 {r.Squeezes} tailgates {r.Tailgates} dodges {r.Evasions} blocks {r.Blocks}"));
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

        var rows = new List<(string Metric, string Value, string Target, bool? Pass)>();
        if (StockCar)
        {
            double spread = Median(r.Select(x => x.Lap2Spread)), dnfs = Median(r.Select(x => (double)x.Dnfs));
            rows.Add(("Spread from 1st to 10th on lap 2 (median)", $"{spread:0.0} s", "≤ 6 s", spread <= 6.0));
            rows.Add(("Close battle involving the player in the last 2 laps", $"{share(x => x.PlayerLateBattle) * 100:0}% of races", "≥ 50%", share(x => x.PlayerLateBattle) >= 0.5));
            rows.Add(("DNFs per race (median)", $"{dnfs:0.0}", "1–4", dnfs >= 1 && dnfs <= 4));
        }
        else
        {
            rows.Add(("Spins in the field per lap (median)", $"{Median(r.Select(x => x.SpinsPerLap)):0.0}", "≥ 3", Median(r.Select(x => x.SpinsPerLap)) >= 3));
            if (r.Any(x => x.CrossoverIncidents > 0) || DevTools.Instance?.Arg("layout") is not "oval")
                rows.Add(("Crossover incidents per race (median)", $"{Median(r.Select(x => (double)x.CrossoverIncidents)):0.0}", "≥ 2", Median(r.Select(x => (double)x.CrossoverIncidents)) >= 2));
            rows.Add(("Leader spun per race (median)", $"{Median(r.Select(x => (double)x.LeaderSpins)):0.0}", "≥ 2", Median(r.Select(x => (double)x.LeaderSpins)) >= 2));
        }
        rows.AddRange(new List<(string Metric, string Value, string Target, bool? Pass)>
        {
            ("Player position changes per race (median)", $"{Median(r.Select(x => (double)x.PlayerPositionChanges)):0}", "≥ 6", Median(r.Select(x => (double)x.PlayerPositionChanges)) >= 6),
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
            ("Pit stops per race / by the player (mean)", $"{r.Average(x => x.PitStops):0.0} / {r.Average(x => x.PlayerPitStops):0.00}", "–", null),
            ("DD Code per race (mean): T1 brake-checks (tailgater hit) / T4 squeezes",
                $"{r.Average(x => x.BrakeChecks):0.0} ({r.Average(x => x.BrakeCheckHits):0.0}) / {r.Average(x => x.Squeezes):0.0}", "–", null),
            ("DD Code per race (mean): tailgating stints / dodges / blocks",
                $"{r.Average(x => x.Tailgates):0.0} / {r.Average(x => x.Evasions):0.0} / {r.Average(x => x.Blocks):0.0}", "–", null),
        });

        var racer = r.Where(x => x.Profile == "racer").ToList();
        var hunter = r.Where(x => x.Profile == "hunter").ToList();
        if (racer.Count > 0 && hunter.Count > 0)
        {
            double pr = racer.Average(x => (double)x.PlayerPoints), ph = hunter.Average(x => (double)x.PlayerPoints);
            double diff = Math.Abs(pr - ph) / Math.Max(1.0, Math.Max(pr, ph));
            rows.Insert(3, ("Racer vs. hunter bot points (mean)", $"{pr:0} vs {ph:0} ({diff * 100:0}% apart)", "within ±15%", diff <= 0.15));
        }

        var md = new StringBuilder();
        string layout = $"{DevTools.Instance?.Arg("layout") ?? "figure8"}{(DevTools.Instance?.Arg("reverse") != null ? " reverse" : "")}";
        md.AppendLine($"# {(StockCar ? "Stock Car" : "Wreckin'")} sim report — {layout}, {n} races, PlayerProxy \"{_profile}\", seeds {_seed}..{_seed + _sims - 1}");
        md.AppendLine();
        md.AppendLine("| Metric | Value | Target | |");
        md.AppendLine("|---|---|---|---|");
        foreach (var (metric, value, target, pass) in rows)
            md.AppendLine($"| {metric} | {value} | {target} | {(pass == null ? "" : pass.Value ? "✅" : "❌")} |");
        md.AppendLine();
        md.AppendLine($"Wall-clock time: {(Time.GetTicksMsec() - _startTicks) / 1000.0:0} s.");
        string text = md.ToString();

        // --outdir=out/sims/x: where the report goes (parallel batches, race_targets.sh).
        string dir = ProjectSettings.GlobalizePath("res://" + (DevTools.Instance?.Arg("outdir") ?? "out/sims"));
        DirAccess.MakeDirRecursiveAbsolute(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", ci);
        string name = $"{(StockCar ? "stockcar" : "wreckin")}_{_profile}_{stamp}";
        using (var f = FileAccess.Open($"{dir}/{name}.md", FileAccess.ModeFlags.Write)) f?.StoreString(text);
        using (var f = FileAccess.Open($"{dir}/{name}.json", FileAccess.ModeFlags.Write))
            f?.StoreString(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));

        foreach (var (metric, value, target, pass) in rows)
            GD.Print($"[Sim] {(pass == null ? "  " : pass.Value ? "OK" : "!!")} {metric}: {value} (target {target})");
        GD.Print($"[Sim] report: {dir}/{name}.md");
        DevTools.QuitClean(GetTree(), 0);
    }
}
